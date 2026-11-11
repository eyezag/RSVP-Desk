using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Conversations;

public enum InviteOutcome
{
    Sent,
    GuestNotFound,
    OptedOut,
    NoConsent,
    AlreadyInConversation,
}

/// <summary>
/// The glue between the pure engine and the database: loads a guest's conversation, runs the
/// engine, stores messages and captured answers, then hands outbound messages to the channel.
/// </summary>
public sealed class ConversationService(
    RsvpDbContext db,
    ConversationEngine engine,
    MessageRenderer renderer,
    ChannelRegistry channels,
    TimeProvider clock,
    ILogger<ConversationService> logger)
{
    private const int MaxConcurrencyRetries = 3;

    /// <summary>Processes one stored inbound event. Never throws for bad events; records the error instead.</summary>
    public async Task ProcessInboundEventAsync(long eventId, CancellationToken cancellationToken)
    {
        var stored = await db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (stored is null || stored.ProcessedAt is not null)
        {
            return;
        }

        var (source, raw) = (stored.Source, stored.RawJson);
        string? error = null;
        try
        {
            foreach (var inbound in channels.ForSource(source).Normalise(raw))
            {
                await WithConcurrencyRetryAsync(() => HandleInboundAsync(inbound, cancellationToken), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Exception messages here are ours or EF's; they never contain message bodies or phone numbers.
            logger.LogWarning(ex, "Inbound event {EventId} from {Source} failed", eventId, source);
            error = Truncate($"{ex.GetType().Name}: {ex.Message}", 2000);
        }

        // Retries and failures clear the change tracker, so reload the event row before marking it.
        db.ChangeTracker.Clear();
        stored = await db.WebhookEvents.FirstAsync(e => e.Id == eventId, cancellationToken);
        if (error is null)
        {
            stored.ProcessedAt = clock.GetUtcNow();
        }
        else
        {
            stored.Error = error;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sends the invitation to one guest. Bulk sending and reminders arrive in build step 6.</summary>
    public async Task<InviteOutcome> InviteAsync(int guestId, CancellationToken cancellationToken)
    {
        var guest = await LoadGuestAsync(g => g.Id == guestId, cancellationToken);
        if (guest is null)
        {
            return InviteOutcome.GuestNotFound;
        }

        if (guest.OptedOutAt is not null)
        {
            return InviteOutcome.OptedOut;
        }

        if (guest.ConsentAt is null)
        {
            return InviteOutcome.NoConsent;
        }

        var conversation = EnsureConversation(guest);
        var state = StateSerializer.Deserialise(conversation.StateData);
        if (state.Step is not (Step.NotInvited or Step.Invited))
        {
            return InviteOutcome.AlreadyInConversation;
        }

        var result = engine.Invite(state, guest.Event.ToDetails());
        var outbound = Apply(guest, conversation, result, guestReplied: false);
        guest.InviteSentAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await SendAsync(guest, outbound, cancellationToken);
        return InviteOutcome.Sent;
    }

    /// <summary>Sends a reminder to one guest. The caller (OutreachService) has already applied the reminder rules.</summary>
    public async Task<bool> RemindAsync(int guestId, CancellationToken cancellationToken)
    {
        var guest = await LoadGuestAsync(g => g.Id == guestId, cancellationToken);
        if (guest is null || guest.OptedOutAt is not null || guest.ConsentAt is null || guest.Conversation is null)
        {
            return false;
        }

        var result = engine.Remind(StateSerializer.Deserialise(guest.Conversation.StateData), guest.Event.ToDetails());
        if (result.Messages.Count == 0)
        {
            return false;
        }

        var outbound = Apply(guest, guest.Conversation, result, guestReplied: false);
        guest.LastReminderAt = clock.GetUtcNow();
        guest.ReminderCount++;
        await db.SaveChangesAsync(cancellationToken);
        await SendAsync(guest, outbound, cancellationToken);
        return true;
    }

    private async Task HandleInboundAsync(NormalisedInbound inbound, CancellationToken cancellationToken)
    {
        if (await db.Messages.AnyAsync(m => m.ProviderMessageId == inbound.MessageId, cancellationToken))
        {
            logger.LogDebug("Ignoring repeated inbound message");
            return;
        }

        var guest = inbound.From.GuestId is { } id
            ? await LoadGuestAsync(g => g.Id == id, cancellationToken)
            : await LoadGuestByPhoneAsync(inbound.From.PhoneE164, cancellationToken);
        if (guest is null)
        {
            throw new InvalidOperationException("Inbound message from an unknown guest.");
        }

        var now = clock.GetUtcNow();
        db.Messages.Add(new Message
        {
            GuestId = guest.Id,
            Direction = MessageDirection.In,
            Body = inbound.Text ?? string.Empty,
            ReplyId = inbound.ReplyId,
            ProviderMessageId = inbound.MessageId,
            SentAt = inbound.SentAt,
        });
        guest.LastActivityAt = now;
        guest.LastInboundAt = now;

        var conversation = EnsureConversation(guest);
        var state = StateSerializer.Deserialise(conversation.StateData);
        var result = engine.Handle(state, inbound.ToEngineInbound(), guest.Event.ToDetails());

        List<Outgoing> outbound = result.Duplicate ? [] : Apply(guest, conversation, result, guestReplied: true);
        await db.SaveChangesAsync(cancellationToken);
        await SendAsync(guest, outbound, cancellationToken);
    }

    /// <summary>Applies an engine result to the tracked entities. Returns the outbound messages to send.</summary>
    private List<Outgoing> Apply(Guest guest, Conversation conversation, EngineResult result, bool guestReplied)
    {
        var now = clock.GetUtcNow();

        conversation.State = result.State.Step;
        conversation.StateData = StateSerializer.Serialise(result.State);
        conversation.UpdatedAt = now;
        conversation.Version++;

        foreach (var capture in result.Captures)
        {
            ApplyCapture(guest, capture, now);
        }

        guest.Status = GuestStatusRules.Next(result.State.Step, guest.Status, guestReplied);

        var outbound = new List<Outgoing>();
        foreach (var message in result.Messages)
        {
            var rendered = renderer.Render(message);
            var row = new Message
            {
                GuestId = guest.Id,
                Direction = MessageDirection.Out,
                Body = rendered.Body,
                ChoicesJson = rendered.Choices.Count == 0 ? null : JsonSerializer.Serialize(rendered.Choices, SimulatorChannel.Json),
                SentAt = now,
                DeliveryStatus = DeliveryStatus.Pending,
            };
            db.Messages.Add(row);
            outbound.Add(new Outgoing(row, rendered));
            guest.LastActivityAt = now;
        }

        return outbound;
    }

    private void ApplyCapture(Guest guest, Capture capture, DateTimeOffset now)
    {
        if (capture is OptedOut)
        {
            guest.OptedOutAt ??= now;
            return;
        }

        if (capture is NeedsAttention)
        {
            guest.NeedsAttention = true;
            return;
        }

        var rsvp = guest.Rsvp ??= new Rsvp { GuestId = guest.Id };
        switch (capture)
        {
            case AttendanceCaptured a:
                rsvp.Attending = a.Attending;
                break;
            case PartySizeCaptured p:
                rsvp.PartySize = p.PartySize;
                rsvp.Persons.Clear();
                break;
            case DietaryCaptured d:
                var person = rsvp.Persons.FirstOrDefault(x => x.PersonIndex == d.PersonIndex);
                if (person is null)
                {
                    person = new RsvpPerson { PersonIndex = d.PersonIndex };
                    rsvp.Persons.Add(person);
                }

                person.Dietary = d.Dietary;
                person.DietaryNote = d.Note;
                break;
            case WheelchairUsersCaptured w:
                rsvp.WheelchairUsers = w.Count;
                break;
            case AccessNoteCaptured n:
                rsvp.AccessNote = n.Note;
                break;
            case RsvpCompleted:
                rsvp.ConfirmedAt = now;
                break;
            case AnswersCleared:
                rsvp.Attending = null;
                rsvp.PartySize = null;
                rsvp.WheelchairUsers = null;
                rsvp.AccessNote = null;
                rsvp.ConfirmedAt = null;
                rsvp.Persons.Clear();
                break;
            default:
                throw new InvalidOperationException($"Unhandled capture {capture.GetType().Name}.");
        }
    }

    /// <summary>Sends after the database commit, so a crash never leaves a sent message unrecorded as Pending.</summary>
    private async Task SendAsync(Guest guest, List<Outgoing> outbound, CancellationToken cancellationToken)
    {
        if (outbound.Count == 0)
        {
            return;
        }

        var channel = channels.Active;
        foreach (var (row, rendered) in outbound)
        {
            var result = await channel.SendAsync(new OutboundEnvelope(guest.Id, guest.PhoneE164, rendered), cancellationToken);
            row.ProviderMessageId = result.ProviderMessageId;
            row.DeliveryStatus = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            if (!result.Success)
            {
                logger.LogWarning("Sending to guest {GuestId} failed: {Error}", guest.Id, result.Error);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private Conversation EnsureConversation(Guest guest)
    {
        if (guest.Conversation is null)
        {
            guest.Conversation = new Conversation
            {
                GuestId = guest.Id,
                StateData = StateSerializer.Serialise(ConversationState.New),
                UpdatedAt = clock.GetUtcNow(),
            };
            db.Conversations.Add(guest.Conversation);
        }

        return guest.Conversation;
    }

    private Task<Guest?> LoadGuestAsync(System.Linq.Expressions.Expression<Func<Guest, bool>> predicate, CancellationToken cancellationToken) =>
        db.Guests
            .Include(g => g.Event)
            .Include(g => g.Conversation)
            .Include(g => g.Rsvp).ThenInclude(r => r!.Persons)
            .FirstOrDefaultAsync(predicate, cancellationToken);

    /// <summary>
    /// The same phone can be on several events' guest lists; use the most recent event.
    /// Live-mode routing is revisited in build step 7.
    /// </summary>
    private async Task<Guest?> LoadGuestByPhoneAsync(string? phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(phone))
        {
            return null;
        }

        var id = await db.Guests
            .Where(g => g.PhoneE164 == phone)
            .OrderByDescending(g => g.EventId)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is null ? null : await LoadGuestAsync(g => g.Id == id, cancellationToken);
    }

    private async Task WithConcurrencyRetryAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // Someone else (e.g. an invitation) changed the conversation; reload and retry.
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private sealed record Outgoing(Message Row, RenderedMessage Rendered);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
