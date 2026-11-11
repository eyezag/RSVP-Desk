using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Pages.Events;

public class IndexModel(RsvpDbContext db) : PageModel
{
    public sealed record EventItem(int Id, string Name, DateTimeOffset StartsAt, string TimeZoneId, string Venue, bool IsSample, int Guests, int Headcount, int Capacity);

    public List<EventItem> Events { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var rows = await db.Events.AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartsAt,
                e.TimeZoneId,
                e.Venue,
                e.IsSample,
                Guests = e.Guests.Count,
                Headcount = e.Guests.Where(g => g.Status == GuestStatus.Attending && g.Rsvp != null).Sum(g => g.Rsvp!.PartySize ?? 0),
                e.Capacity,
            })
            .ToListAsync(ct);
        Events = rows.OrderBy(r => r.StartsAt).Select(r => new EventItem(r.Id, r.Name, r.StartsAt, r.TimeZoneId, r.Venue, r.IsSample, r.Guests, r.Headcount, r.Capacity)).ToList();
    }
}
