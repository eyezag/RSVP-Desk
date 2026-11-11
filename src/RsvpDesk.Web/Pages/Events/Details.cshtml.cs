using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Events;

public class DetailsModel(RsvpDbContext db, GuestQueries queries) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public Event Event { get; private set; } = null!;
    public EventSummary Summary { get; private set; } = null!;
    public GuestPage Guests { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id, ct);
        if (ev is null)
        {
            return NotFound();
        }

        Event = ev;
        Summary = (await queries.SummaryAsync(Id, ct))!;
        Guests = await queries.SearchAsync(Id, Status, Q, PageNumber, GuestQueries.DefaultPageSize, ct);
        return Page();
    }
}
