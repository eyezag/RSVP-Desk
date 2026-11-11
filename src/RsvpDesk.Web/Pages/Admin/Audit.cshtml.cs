using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Pages.Admin;

[Authorize(Policy = Policies.Organiser)]
public class AuditModel(RsvpDbContext db) : PageModel
{
    private const int PageSize = 50;

    public sealed record Row(DateTimeOffset At, string? Email, string Action, string Target);

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public List<Row> Entries { get; private set; } = [];
    public bool HasMore { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        PageNumber = Math.Max(1, PageNumber);
        var rows = await db.AuditLog.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize + 1)
            .Select(a => new Row(a.At, db.Users.Where(u => u.Id == a.UserId).Select(u => u.Email).FirstOrDefault(), a.Action, a.Target))
            .ToListAsync(ct);
        HasMore = rows.Count > PageSize;
        Entries = rows.Take(PageSize).ToList();
    }
}
