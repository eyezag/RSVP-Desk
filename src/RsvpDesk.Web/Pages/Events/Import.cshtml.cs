using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Events;

[Authorize(Policy = Policies.Organiser)]
[RequestSizeLimit(GuestImporter.MaxBytes + 64 * 1024)]
public class ImportModel(RsvpDbContext db, GuestImporter importer) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public bool AllConsented { get; set; }

    [BindProperty]
    public string? ConsentSource { get; set; }

    public string EventName { get; private set; } = "";
    public ImportResult? Result { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await LoadAsync(ct) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct))
        {
            return NotFound();
        }

        if (Upload is null || Upload.Length == 0)
        {
            Result = new ImportResult(0, [], "Choose a CSV file.");
            return Page();
        }

        if (Upload.Length > GuestImporter.MaxBytes)
        {
            Result = new ImportResult(0, [], "The file is larger than 1 MB.");
            return Page();
        }

        using var reader = new StreamReader(Upload.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        Result = await importer.ImportAsync(Id, text, AllConsented, ConsentSource, User.UserId(), ct);
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        EventName = await db.Events.Where(e => e.Id == Id).Select(e => e.Name).FirstOrDefaultAsync(ct) ?? "";
        return EventName.Length > 0;
    }
}
