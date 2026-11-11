using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Events;

[Authorize(Policy = Policies.Organiser)]
public class AddGuestModel(RsvpDbContext db, GuestCommands guests) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public GuestForm Input { get; set; } = new();

    public string EventName { get; private set; } = "";

    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
        {
            return NotFound();
        }

        var result = await guests.AddAsync(Id, Input.ToInput(), User.UserId(), ct);
        if (!result.Ok)
        {
            Errors = result.Errors!;
            return Page();
        }

        TempData["Flash"] = $"Added {Input.Name.Trim()}.";
        return RedirectToPage("/Events/Details", new { id = Id });
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        EventName = await db.Events.Where(e => e.Id == Id).Select(e => e.Name).FirstOrDefaultAsync(ct) ?? "";
        return EventName.Length > 0;
    }
}

public sealed class GuestForm
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool Consent { get; set; }
    public string? ConsentSource { get; set; }

    public GuestInput ToInput() => new(Name ?? "", Phone ?? "", Consent, ConsentSource);
}
