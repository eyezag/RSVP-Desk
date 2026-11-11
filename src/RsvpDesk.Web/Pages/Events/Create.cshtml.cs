using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Events;

[Authorize(Policy = Policies.Organiser)]
public class CreateModel(EventCommands events) : PageModel
{
    [BindProperty]
    public EventInput Input { get; set; } = new();

    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await events.SaveAsync(null, Input, User.UserId(), ct);
        if (!result.Ok)
        {
            Errors = result.Errors!;
            return Page();
        }

        return RedirectToPage("/Events/Details", new { id = result.Id });
    }
}
