using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Guests;

/// <summary>Filter values for the guest table, beyond the plain statuses.</summary>
public static class GuestFilters
{
    public const string Awaiting = "awaiting";
    public const string Attention = "attention";
    public const string OptedOut = "optedout";

    public static readonly IReadOnlyList<(string Value, string Label)> Options =
    [
        ("", "All guests"),
        (nameof(GuestStatus.NotInvited), "Not invited"),
        (nameof(GuestStatus.Invited), "Invited, no reply"),
        (nameof(GuestStatus.InChat), "In chat"),
        (Awaiting, "Awaiting reply (invited or in chat)"),
        (nameof(GuestStatus.Attending), "Attending"),
        (nameof(GuestStatus.Declined), "Declined"),
        (Attention, "Needs attention"),
        (OptedOut, "Opted out"),
    ];
}

public sealed record GuestRow(
    int Id,
    string Name,
    string Phone,
    GuestStatus Status,
    int? PartySize,
    IReadOnlyList<RsvpPersonView> Diets,
    int? WheelchairUsers,
    bool HasAccessNote,
    bool NeedsAttention,
    bool OptedOut,
    bool HasConsent,
    DateTimeOffset? LastActivityAt);

public sealed record RsvpPersonView(int PersonIndex, Dietary Dietary, string? Note);

public sealed record GuestPage(IReadOnlyList<GuestRow> Rows, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed record EventSummary(
    int Capacity,
    int Headcount,
    int AttendingParties,
    int AwaitingReply,
    int Declined,
    int NotInvited,
    int WheelchairUsers,
    int NeedsAttention,
    int OptedOut,
    int TotalGuests);

public sealed class GuestQueries(RsvpDbContext db)
{
    public const int DefaultPageSize = 25;

    public async Task<GuestPage> SearchAsync(int eventId, string? filter, string? query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var guests = db.Guests.AsNoTracking().Where(g => g.EventId == eventId);

        guests = filter switch
        {
            null or "" => guests,
            GuestFilters.Awaiting => guests.Where(g => g.Status == GuestStatus.Invited || g.Status == GuestStatus.InChat),
            GuestFilters.Attention => guests.Where(g => g.NeedsAttention),
            GuestFilters.OptedOut => guests.Where(g => g.OptedOutAt != null),
            _ when Enum.TryParse<GuestStatus>(filter, ignoreCase: true, out var status) => guests.Where(g => g.Status == status),
            _ => guests.Where(_ => false),
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            var digits = new string(q.Where(char.IsDigit).ToArray());
            guests = digits.Length >= 3
                ? guests.Where(g => g.Name.ToLower().Contains(q) || g.PhoneE164.Contains(digits))
                : guests.Where(g => g.Name.ToLower().Contains(q));
        }

        var total = await guests.CountAsync(ct);
        var rows = await guests
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.PhoneE164,
                g.Status,
                PartySize = g.Rsvp == null ? null : g.Rsvp.PartySize,
                Persons = g.Rsvp == null ? null : g.Rsvp.Persons.OrderBy(p => p.PersonIndex).Select(p => new RsvpPersonView(p.PersonIndex, p.Dietary, p.DietaryNote)).ToList(),
                WheelchairUsers = g.Rsvp == null ? null : g.Rsvp.WheelchairUsers,
                HasNote = g.Rsvp != null && g.Rsvp.AccessNote != null,
                g.NeedsAttention,
                OptedOut = g.OptedOutAt != null,
                HasConsent = g.ConsentAt != null,
                g.LastActivityAt,
            })
            .ToListAsync(ct);

        return new GuestPage(
            rows.Select(r => new GuestRow(r.Id, r.Name, r.PhoneE164, r.Status, r.PartySize, r.Persons ?? [], r.WheelchairUsers, r.HasNote, r.NeedsAttention, r.OptedOut, r.HasConsent, r.LastActivityAt)).ToList(),
            page,
            pageSize,
            total);
    }

    /// <summary>The summary tiles. Headcount and wheelchair users count confirmed attending guests only.</summary>
    public async Task<EventSummary?> SummaryAsync(int eventId, CancellationToken ct)
    {
        var capacity = await db.Events.Where(e => e.Id == eventId).Select(e => (int?)e.Capacity).FirstOrDefaultAsync(ct);
        if (capacity is null)
        {
            return null;
        }

        var guests = await db.Guests.AsNoTracking()
            .Where(g => g.EventId == eventId)
            .Select(g => new
            {
                g.Status,
                g.NeedsAttention,
                OptedOut = g.OptedOutAt != null,
                PartySize = g.Rsvp == null ? null : g.Rsvp.PartySize,
                Wheelchairs = g.Rsvp == null ? null : g.Rsvp.WheelchairUsers,
            })
            .ToListAsync(ct);

        var attending = guests.Where(g => g.Status == GuestStatus.Attending).ToList();
        return new EventSummary(
            capacity.Value,
            Headcount: attending.Sum(g => g.PartySize ?? 0),
            AttendingParties: attending.Count,
            AwaitingReply: guests.Count(g => g.Status is GuestStatus.Invited or GuestStatus.InChat && !g.OptedOut),
            Declined: guests.Count(g => g.Status == GuestStatus.Declined),
            NotInvited: guests.Count(g => g.Status == GuestStatus.NotInvited),
            WheelchairUsers: attending.Sum(g => g.Wheelchairs ?? 0),
            NeedsAttention: guests.Count(g => g.NeedsAttention),
            OptedOut: guests.Count(g => g.OptedOut),
            TotalGuests: guests.Count);
    }
}
