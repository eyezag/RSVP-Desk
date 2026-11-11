using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Events;

public class DetailsModel(RsvpDbContext db, GuestQueries queries, OutreachService outreach) : PageModel
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
    public OutreachPreview? Invitations { get; private set; }
    public int ReminderCandidates { get; private set; }

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
        if (User.IsOrganiser())
        {
            Invitations = await outreach.PreviewInvitationsAsync(Id, ct);
            ReminderCandidates = (await outreach.ReminderCandidatesAsync(Id, ct)).Count;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (!User.IsOrganiser())
        {
            return Forbid();
        }

        var r = await outreach.SendInvitationsAsync(Id, User.UserId(), ct);
        if (r is null)
        {
            return NotFound();
        }

        TempData["Flash"] = r.EventInPast
            ? "The event has started, so no invitations were sent."
            : $"Sent {r.Sent} invitation{(r.Sent == 1 ? "" : "s")}." + (r.Failed > 0 ? $" {r.Failed} failed." : "")
              + (r.SkippedNoConsent > 0 ? $" Skipped {r.SkippedNoConsent} without consent." : "");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRemindAsync(CancellationToken ct)
    {
        if (!User.IsOrganiser())
        {
            return Forbid();
        }

        var r = await outreach.SendRemindersAsync(Id, User.UserId(), ct);
        if (r is null)
        {
            return NotFound();
        }

        TempData["Flash"] = r.EventInPast
            ? "The event has started, so no reminders were sent."
            : $"Sent {r.Sent} reminder{(r.Sent == 1 ? "" : "s")}." + (r.Failed > 0 ? $" {r.Failed} failed." : "");
        return RedirectToPage(new { id = Id });
    }
}
