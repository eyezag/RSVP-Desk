using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Reports;

public sealed record DietLine(Dietary Dietary, int People, double Share);

public sealed record OtherRequirement(int GuestId, string GuestName, int PersonIndex, string Note);

public sealed record AccessParty(int GuestId, string GuestName, int PartySize, int WheelchairUsers, string? AccessNote);

public sealed record CateringReportData(
    int ConfirmedParties,
    int ConfirmedPeople,
    int InProgressGuests,
    IReadOnlyList<DietLine> Diets,
    IReadOnlyList<OtherRequirement> Others,
    int WheelchairUsers,
    IReadOnlyList<AccessParty> AccessParties);

/// <summary>
/// Catering and access numbers. Only confirmed attending guests are counted: answers from guests
/// still in the chat may change, so including them would make the kitchen's numbers move.
/// </summary>
public sealed class CateringReport(RsvpDbContext db)
{
    public async Task<CateringReportData> BuildAsync(int eventId, CancellationToken ct)
    {
        var confirmed = await db.Guests.AsNoTracking()
            .Where(g => g.EventId == eventId && g.Status == GuestStatus.Attending && g.Rsvp != null)
            .OrderBy(g => g.Name)
            .Select(g => new
            {
                g.Id,
                g.Name,
                PartySize = g.Rsvp!.PartySize ?? 0,
                Wheelchairs = g.Rsvp.WheelchairUsers ?? 0,
                g.Rsvp.AccessNote,
                Persons = g.Rsvp.Persons.OrderBy(p => p.PersonIndex).Select(p => new { p.PersonIndex, p.Dietary, p.DietaryNote }).ToList(),
            })
            .ToListAsync(ct);
        var inProgress = await db.Guests.CountAsync(g => g.EventId == eventId && g.Status == GuestStatus.InChat, ct);

        var persons = confirmed.SelectMany(g => g.Persons).ToList();
        var diets = Enum.GetValues<Dietary>()
            .Select(d =>
            {
                var count = persons.Count(p => p.Dietary == d);
                return new DietLine(d, count, persons.Count == 0 ? 0 : count / (double)persons.Count);
            })
            .ToList();
        var others = confirmed
            .SelectMany(g => g.Persons.Where(p => p.Dietary == Dietary.Other).Select(p => new OtherRequirement(g.Id, g.Name, p.PersonIndex, p.DietaryNote ?? "")))
            .ToList();
        var access = confirmed
            .Where(g => g.Wheelchairs > 0 || !string.IsNullOrWhiteSpace(g.AccessNote))
            .Select(g => new AccessParty(g.Id, g.Name, g.PartySize, g.Wheelchairs, g.AccessNote))
            .ToList();

        return new CateringReportData(
            confirmed.Count,
            confirmed.Sum(g => g.PartySize),
            inProgress,
            diets,
            others,
            confirmed.Sum(g => g.Wheelchairs),
            access);
    }
}
