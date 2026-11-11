using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Events;

[Authorize(Policy = Policies.Organiser)]
public class EditModel(RsvpDbContext db, EventCommands events) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public EventInput Input { get; set; } = new();

    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id, ct);
        if (ev is null)
        {
            return NotFound();
        }

        Input = EventCommands.ToInput(ev);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await events.SaveAsync(Id, Input, User.UserId(), ct);
        if (!result.Ok)
        {
            if (result.Errors!.ContainsKey("event"))
            {
                return NotFound();
            }

            Errors = result.Errors!;
            return Page();
        }

        return RedirectToPage("/Events/Details", new { id = Id });
    }
}
