using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Conversations;

public sealed class SendingOptions
{
    public const string SectionName = "Sending";

    /// <summary>At most this many reminders per guest.</summary>
    public int MaxRemindersPerGuest { get; set; } = 2;

    /// <summary>Pause between messages in a bulk send, to stay under the provider's rate limits.</summary>
    public int DelayBetweenMessagesMs { get; set; }
}

public sealed record OutreachPreview(int Ready, int NoConsent, int OptedOut, bool EventInPast);

public sealed record OutreachResult(int Sent, int Failed, int SkippedNoConsent, int SkippedOptedOut, bool EventInPast);

/// <summary>
/// Bulk invitations and reminders, and the rules for who gets them (spec section 12, step 6).
/// Opted-out guests and guests without recorded consent never get anything.
/// </summary>
public sealed class OutreachService(
    RsvpDbContext db,
    ConversationService conversations,
    AuditLog audit,
    TimeProvider clock,
    IOptions<SendingOptions> options,
    ILogger<OutreachService> logger)
{
    public async Task<OutreachPreview?> PreviewInvitationsAsync(int eventId, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null)
        {
            return null;
        }

        var notInvited = db.Guests.Where(g => g.EventId == eventId && g.Status == GuestStatus.NotInvited);
        return new OutreachPreview(
            await notInvited.CountAsync(g => g.ConsentAt != null && g.OptedOutAt == null, ct),
            await notInvited.CountAsync(g => g.ConsentAt == null && g.OptedOutAt == null, ct),
            await notInvited.CountAsync(g => g.OptedOutAt != null, ct),
            ev.StartsAt <= clock.GetUtcNow());
    }

    public async Task<OutreachResult?> SendInvitationsAsync(int eventId, int? userId, CancellationToken ct)
    {
        var preview = await PreviewInvitationsAsync(eventId, ct);
        if (preview is null)
        {
            return null;
        }

        if (preview.EventInPast)
        {
            return new OutreachResult(0, 0, preview.NoConsent, preview.OptedOut, true);
        }

        var ids = await db.Guests
            .Where(g => g.EventId == eventId && g.Status == GuestStatus.NotInvited && g.ConsentAt != null && g.OptedOutAt == null)
            .OrderBy(g => g.Id)
            .Select(g => g.Id)
            .ToListAsync(ct);

        var (sent, failed) = await SendEachAsync(ids, async id => await conversations.InviteAsync(id, ct) == InviteOutcome.Sent, ct);
        await audit.WriteAsync(userId, "InvitationsSent", $"event:{eventId} sent:{sent} failed:{failed}", ct);
        return new OutreachResult(sent, failed, preview.NoConsent, preview.OptedOut, false);
    }

    /// <summary>
    /// Who gets a reminder: invited or in chat, consented, not opted out, not waiting on the organiser
    /// (HELP), invited at least <c>ReminderAfterDays</c> ago, quiet for that long, last reminded at
    /// least that long ago, fewer than the maximum reminders so far, and the event hasn't started.
    /// </summary>
    public async Task<List<int>> ReminderCandidatesAsync(int eventId, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        var now = clock.GetUtcNow();
        if (ev is null || ev.StartsAt <= now)
        {
            return [];
        }

        var cutoff = now.AddDays(-ev.ReminderAfterDays);
        var max = options.Value.MaxRemindersPerGuest;
        return await db.Guests
            .Where(g => g.EventId == eventId
                && (g.Status == GuestStatus.Invited || g.Status == GuestStatus.InChat)
                && g.ConsentAt != null
                && g.OptedOutAt == null
                && !g.NeedsAttention
                && g.InviteSentAt != null && g.InviteSentAt <= cutoff
                && (g.LastInboundAt == null || g.LastInboundAt <= cutoff)
                && (g.LastReminderAt == null || g.LastReminderAt <= cutoff)
                && g.ReminderCount < max)
            .OrderBy(g => g.Id)
            .Select(g => g.Id)
            .ToListAsync(ct);
    }

    public async Task<OutreachResult?> SendRemindersAsync(int eventId, int? userId, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null)
        {
            return null;
        }

        if (ev.StartsAt <= clock.GetUtcNow())
        {
            return new OutreachResult(0, 0, 0, 0, true);
        }

        var ids = await ReminderCandidatesAsync(eventId, ct);
        var (sent, failed) = await SendEachAsync(ids, id => conversations.RemindAsync(id, ct), ct);
        await audit.WriteAsync(userId, "RemindersSent", $"event:{eventId} sent:{sent} failed:{failed}", ct);
        return new OutreachResult(sent, failed, 0, 0, false);
    }

    private async Task<(int Sent, int Failed)> SendEachAsync(IReadOnlyList<int> guestIds, Func<int, Task<bool>> send, CancellationToken ct)
    {
        int sent = 0, failed = 0;
        foreach (var id in guestIds)
        {
            try
            {
                if (await send(id))
                {
                    sent++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One guest's failure must not stop the rest of the list.
                failed++;
                logger.LogWarning(ex, "Sending to guest {GuestId} failed", id);
                db.ChangeTracker.Clear();
            }

            if (options.Value.DelayBetweenMessagesMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.Value.DelayBetweenMessagesMs), clock, ct);
            }
        }

        return (sent, failed);
    }
}
