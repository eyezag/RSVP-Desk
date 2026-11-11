using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Privacy;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Raw webhook payloads (which contain numbers and message text) are kept this long after processing.</summary>
    public int RawEventDays { get; set; } = 30;
}

public sealed record RetentionResult(int EventsAnonymised, int GuestsAnonymised, int RawEventsDeleted);

/// <summary>
/// POPIA retention (spec section 11): a configurable number of days after an event, its guests'
/// personal information is removed. What stays is anonymous: whether a party came and its size.
/// </summary>
public sealed class RetentionService(RsvpDbContext db, AuditLog audit, TimeProvider clock, IOptions<RetentionOptions> options)
{
    public async Task<RetentionResult> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = (await db.Events
                .Where(e => !e.IsSample && e.GuestDataDeletedAt == null)
                .Select(e => new { e.Id, e.StartsAt, e.DeleteDataAfterDays })
                .ToListAsync(ct))
            .Where(e => e.StartsAt.AddDays(e.DeleteDataAfterDays) <= now)
            .Select(e => e.Id)
            .ToList();

        var guests = 0;
        foreach (var eventId in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var ids = await db.Guests.Where(g => g.EventId == eventId).Select(g => g.Id).ToListAsync(ct);
            await db.Messages.Where(m => ids.Contains(m.GuestId)).ExecuteDeleteAsync(ct);
            await db.Conversations.Where(c => ids.Contains(c.GuestId)).ExecuteDeleteAsync(ct);
            await db.RsvpPersons.Where(p => ids.Contains(p.RsvpId)).ExecuteDeleteAsync(ct);
            await db.Rsvps.Where(r => ids.Contains(r.GuestId)).ExecuteUpdateAsync(u => u
                .SetProperty(r => r.AccessNote, (string?)null)
                .SetProperty(r => r.WheelchairUsers, (int?)null), ct);
            foreach (var guest in await db.Guests.Where(g => g.EventId == eventId).ToListAsync(ct))
            {
                Anonymise(guest);
            }

            var ev = await db.Events.SingleAsync(e => e.Id == eventId, ct);
            ev.GuestDataDeletedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            guests += ids.Count;
            await audit.WriteAsync(null, "RetentionAnonymisedEvent", $"event:{eventId} guests:{ids.Count}", ct);
        }

        var rawCutoff = now.AddDays(-options.Value.RawEventDays);
        var raw = await db.WebhookEvents.Where(e => e.ReceivedAt < rawCutoff && (e.ProcessedAt != null || e.Error != null)).ExecuteDeleteAsync(ct);
        if (raw > 0)
        {
            await audit.WriteAsync(null, "RetentionDeletedRawEvents", $"count:{raw}", ct);
        }

        return new RetentionResult(due.Count, guests, raw);
    }

    /// <summary>
    /// Deletes one guest and everything about them, on request (POPIA). Stored raw webhook events
    /// that mention their number or WhatsApp user id are deleted too.
    /// </summary>
    public async Task<bool> DeleteGuestAsync(int guestId, int? userId, CancellationToken ct)
    {
        var guest = await db.Guests.FirstOrDefaultAsync(g => g.Id == guestId, ct);
        if (guest is null)
        {
            return false;
        }

        var digits = new string(guest.PhoneE164.Where(char.IsAsciiDigit).ToArray());
        var userKey = guest.WhatsAppUserId;
        var simulatorKey = $"\"guestId\":{guestId},";
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.WebhookEvents
            .Where(e => (digits.Length >= 8 && e.RawJson.Contains(digits)) || (userKey != null && e.RawJson.Contains(userKey)) || e.RawJson.Contains(simulatorKey))
            .ExecuteDeleteAsync(ct);
        db.Guests.Remove(guest);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await audit.WriteAsync(userId, "GuestDeleted", $"guest:{guestId}", ct);
        return true;
    }

    private static void Anonymise(Guest guest)
    {
        guest.Name = $"Deleted guest {guest.Id}";
        guest.PhoneE164 = $"deleted:{guest.Id}";
        guest.WhatsAppUserId = null;
        guest.ConsentSource = null;
        guest.NeedsAttention = false;
    }
}

/// <summary>Runs the retention job on start-up and then every <see cref="RetentionOptions.Interval"/>.</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, IOptions<RetentionOptions> options, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(stoppingToken);
                if (result.EventsAnonymised + result.RawEventsDeleted > 0)
                {
                    logger.LogInformation("Retention: anonymised {Guests} guests in {Events} events, deleted {Raw} raw events", result.GuestsAnonymised, result.EventsAnonymised, result.RawEventsDeleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Retention job failed; it will run again at the next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
