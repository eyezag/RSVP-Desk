using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Pages;

public class SimulatorModel(RsvpDbContext db, IOptions<ChannelOptions> channel) : PageModel
{
    public string EventName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (channel.Value.Mode != ChannelMode.Simulator)
        {
            return NotFound();
        }

        EventName = await db.Events.Where(e => e.IsSample).Select(e => e.Name).FirstOrDefaultAsync(ct) ?? "(no sample event)";
        return Page();
    }
}
