using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Api;

public sealed record SimulatorMessageRequest(string? MessageId, string? Text, string? ReplyId);

public sealed record SimulatorMessageResponse(long EventId, bool Processed, bool Duplicate);

public sealed record ChatMessageDto(long Id, string Direction, string Body, string? ReplyId, IReadOnlyList<RenderedChoice> Choices, DateTimeOffset SentAt, string? DeliveryStatus);

public sealed record SimulatorGuestDto(int Id, string Name, string Status, string Step, bool OptedOut, bool HasConsent);

/// <summary>
/// The guest side of the simulator: the browser plays the guest's phone. These endpoints exist
/// only in simulator mode and need no login (spec section 9).
/// </summary>
public static partial class SimulatorEndpoints
{
    private static readonly TimeSpan WaitForProcessing = TimeSpan.FromSeconds(5);

    public static IEndpointRouteBuilder MapSimulatorApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/simulator").WithTags("Simulator");
        group.MapGet("/guests", ListGuests);
        group.MapPost("/{guestId:int}/messages", PostMessage);
        group.MapGet("/{guestId:int}/messages", GetMessages);
        group.MapPost("/{guestId:int}/invite", Invite);
        group.MapPost("/{guestId:int}/reset", Reset);
        group.MapGet("/{guestId:int}", GetGuest);
        group.AllowAnonymous();
        return app;
    }

    private static async Task<IResult> ListGuests(RsvpDbContext db, CancellationToken ct)
    {
        var guests = await db.Guests
            .Where(g => g.Event.IsSample)
            .OrderBy(g => g.Name)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Status,
                Step = g.Conversation == null ? Step.NotInvited : g.Conversation.State,
                OptedOut = g.OptedOutAt != null,
                HasConsent = g.ConsentAt != null,
            })
            .ToListAsync(ct);
        return Results.Ok(guests.Select(g => new SimulatorGuestDto(g.Id, g.Name, g.Status.ToString(), g.Step.ToString(), g.OptedOut, g.HasConsent)));
    }

    /// <summary>
    /// Stores the message as a raw inbound event (exactly as the WhatsApp webhook will) and queues it.
    /// With <c>wait=true</c> the response waits briefly until the worker has processed it.
    /// </summary>
    private static async Task<IResult> PostMessage(
        int guestId,
        SimulatorMessageRequest request,
        bool? wait,
        RsvpDbContext db,
        InboundQueue queue,
        ProcessingNotifier notifier,
        TimeProvider clock,
        CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await db.Guests.AnyAsync(g => g.Id == guestId && g.Event.IsSample, ct))
        {
            return Results.NotFound();
        }

        var messageId = string.IsNullOrWhiteSpace(request.MessageId) ? $"sim-in-{Guid.NewGuid():N}" : request.MessageId;
        var existing = await db.WebhookEvents.Where(e => e.ExternalId == messageId).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct);
        if (existing is { } existingId)
        {
            return Results.Ok(new SimulatorMessageResponse(existingId, Processed: true, Duplicate: true));
        }

        var now = clock.GetUtcNow();
        var raw = JsonSerializer.Serialize(new SimulatorInboundEvent(guestId, messageId, request.Text, request.ReplyId, now), SimulatorChannel.Json);
        var stored = new WebhookEvent { Source = SimulatorChannel.SourceName, ExternalId = messageId, ReceivedAt = now, RawJson = raw };
        db.WebhookEvents.Add(stored);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Most likely lost a race with an identical request, and the unique index did its job.
            db.ChangeTracker.Clear();
            var winner = await db.WebhookEvents.Where(e => e.ExternalId == messageId).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct);
            if (winner is null)
            {
                throw;
            }

            return Results.Ok(new SimulatorMessageResponse(winner.Value, Processed: true, Duplicate: true));
        }

        var registration = wait == true ? notifier.Register(stored.Id) : null;
        queue.Enqueue(stored.Id);
        var processed = registration is not null && await notifier.WaitAsync(stored.Id, registration, WaitForProcessing, ct);
        return Results.Accepted($"/api/simulator/{guestId}/messages", new SimulatorMessageResponse(stored.Id, processed, Duplicate: false));
    }

    /// <summary>The guest's chat and saved answers, for the simulator page. Sample (fictional) guests only.</summary>
    private static async Task<IResult> GetGuest(int guestId, RsvpDbContext db, CancellationToken ct)
    {
        if (!await db.Guests.AnyAsync(g => g.Id == guestId && g.Event.IsSample, ct))
        {
            return Results.NotFound();
        }

        return Results.Ok(await GuestEndpoints.BuildDetailAsync(db, guestId, includeSensitive: true, ct));
    }

    private static async Task<IResult> GetMessages(int guestId, long? afterId, RsvpDbContext db, CancellationToken ct)
    {
        if (!await db.Guests.AnyAsync(g => g.Id == guestId && g.Event.IsSample, ct))
        {
            return Results.NotFound();
        }

        var after = afterId ?? 0;
        var rows = await db.Messages
            .Where(m => m.GuestId == guestId && m.Id > after)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(ToDto));
    }

    private static async Task<IResult> Invite(int guestId, RsvpDbContext db, ConversationService conversations, CancellationToken ct)
    {
        if (!await db.Guests.AnyAsync(g => g.Id == guestId && g.Event.IsSample, ct))
        {
            return Results.NotFound();
        }

        var outcome = await conversations.InviteAsync(guestId, ct);
        return outcome switch
        {
            InviteOutcome.Sent => Results.Ok(new { outcome = outcome.ToString() }),
            InviteOutcome.GuestNotFound => Results.NotFound(),
            _ => Results.Conflict(new { outcome = outcome.ToString() }),
        };
    }

    /// <summary>Puts a sample guest back to "not invited" so the demo can be run again.</summary>
    private static async Task<IResult> Reset(int guestId, RsvpDbContext db, CancellationToken ct)
    {
        var guest = await db.Guests.FirstOrDefaultAsync(g => g.Id == guestId && g.Event.IsSample, ct);
        if (guest is null)
        {
            return Results.NotFound();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Messages.Where(m => m.GuestId == guestId).ExecuteDeleteAsync(ct);
        await db.RsvpPersons.Where(p => p.RsvpId == guestId).ExecuteDeleteAsync(ct);
        await db.Rsvps.Where(r => r.GuestId == guestId).ExecuteDeleteAsync(ct);
        await db.Conversations.Where(c => c.GuestId == guestId).ExecuteDeleteAsync(ct);
        guest.Status = GuestStatus.NotInvited;
        guest.OptedOutAt = null;
        guest.InviteSentAt = null;
        guest.NeedsAttention = false;
        guest.LastActivityAt = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    internal static ChatMessageDto ToDto(Message m) => new(
        m.Id,
        m.Direction.ToString(),
        m.Body,
        m.ReplyId,
        m.ChoicesJson is null ? [] : JsonSerializer.Deserialize<List<RenderedChoice>>(m.ChoicesJson, SimulatorChannel.Json) ?? [],
        m.SentAt,
        m.DeliveryStatus?.ToString());

    private static Dictionary<string, string[]> Validate(SimulatorMessageRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Text) && string.IsNullOrWhiteSpace(r.ReplyId))
        {
            errors["text"] = ["Send text or a replyId."];
        }

        if (r.Text is { Length: > 4096 })
        {
            errors["text"] = ["Text is limited to 4096 characters."];
        }

        if (r.ReplyId is { Length: > 256 })
        {
            errors["replyId"] = ["ReplyId is limited to 256 characters."];
        }

        if (!string.IsNullOrEmpty(r.MessageId) && !MessageIdPattern().IsMatch(r.MessageId))
        {
            errors["messageId"] = ["MessageId must be 1 to 100 letters, digits, '.', '-' or '_'."];
        }

        return errors;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,100}$")]
    private static partial Regex MessageIdPattern();
}
