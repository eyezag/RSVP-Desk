using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Reports;

namespace RsvpDesk.Web.Pages.Events;

[Authorize(Policy = Policies.Organiser)]
public class ReportsModel(RsvpDbContext db, CateringReport report, AuditLog audit) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Event Event { get; private set; } = null!;
    public CateringReportData Report { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id, ct);
        if (ev is null)
        {
            return NotFound();
        }

        Event = ev;
        Report = await report.BuildAsync(Id, ct);
        await audit.WriteAsync(User.UserId(), "ReportViewed", $"event:{Id}", ct);
        return Page();
    }
}
