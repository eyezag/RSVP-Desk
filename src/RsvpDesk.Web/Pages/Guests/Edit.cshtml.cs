using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;
using RsvpDesk.Web.Pages.Events;

namespace RsvpDesk.Web.Pages.Guests;

[Authorize(Policy = Policies.Organiser)]
public class EditModel(RsvpDbContext db, GuestCommands commands) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public GuestForm Input { get; set; } = new();

    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var g = await db.Guests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id, ct);
        if (g is null)
        {
            return NotFound();
        }

        Input = new GuestForm { Name = g.Name, Phone = g.PhoneE164, Consent = g.ConsentAt is not null, ConsentSource = g.ConsentSource };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await commands.UpdateAsync(Id, Input.ToInput(), User.UserId(), ct);
        if (!result.Ok)
        {
            if (result.Errors!.ContainsKey("guest"))
            {
                return NotFound();
            }

            Errors = result.Errors!;
            return Page();
        }

        TempData["Flash"] = "Saved.";
        return RedirectToPage("/Guests/Details", new { id = Id });
    }
}
