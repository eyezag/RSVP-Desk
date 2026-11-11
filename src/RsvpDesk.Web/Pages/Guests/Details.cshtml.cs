using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages.Guests;

public class DetailsModel(RsvpDbContext db, GuestCommands commands, AuditLog audit, Conversations.ConversationService conversations) : PageModel
{
    public sealed record MessageView(MessageDirection Direction, string Body, string? ReplyId, DateTimeOffset SentAt, DeliveryStatus? DeliveryStatus, IReadOnlyList<string> ChoiceTitles);

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Guest Guest { get; private set; } = null!;
    public List<MessageView> Messages { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var guest = await db.Guests.AsNoTracking()
            .Include(g => g.Event)
            .Include(g => g.Conversation)
            .Include(g => g.Rsvp).ThenInclude(r => r!.Persons)
            .FirstOrDefaultAsync(g => g.Id == Id, ct);
        if (guest is null)
        {
            return NotFound();
        }

        Guest = guest;
        if (User.IsOrganiser())
        {
            var rows = await db.Messages.AsNoTracking().Where(m => m.GuestId == Id).OrderBy(m => m.Id).ToListAsync(ct);
            Messages = rows.Select(m => new MessageView(
                m.Direction,
                m.Body,
                m.ReplyId,
                m.SentAt,
                m.DeliveryStatus,
                m.ChoicesJson is null ? [] : (JsonSerializer.Deserialize<List<RenderedChoice>>(m.ChoicesJson, SimulatorChannel.Json) ?? []).Select(c => c.Title).ToList())).ToList();
            await audit.WriteAsync(User.UserId(), "GuestViewed", $"guest:{Id}", ct);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostHandledAsync(CancellationToken ct)
    {
        if (!User.IsOrganiser())
        {
            return Forbid();
        }

        if (!await commands.MarkHandledAsync(Id, User.UserId(), ct))
        {
            return NotFound();
        }

        TempData["Flash"] = "Marked as handled.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (!User.IsOrganiser())
        {
            return Forbid();
        }

        var outcome = await conversations.InviteAsync(Id, ct);
        await audit.WriteAsync(User.UserId(), "InvitationSent", $"guest:{Id} outcome:{outcome}", ct);
        TempData["Flash"] = outcome switch
        {
            Conversations.InviteOutcome.Sent => "Invitation sent.",
            Conversations.InviteOutcome.NoConsent => "No consent is recorded for this guest, so the invitation was not sent.",
            Conversations.InviteOutcome.OptedOut => "This guest opted out.",
            Conversations.InviteOutcome.AlreadyInConversation => "This guest is already answering.",
            _ => "Guest not found.",
        };
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostReenableAsync(CancellationToken ct)
    {
        if (!User.IsOrganiser())
        {
            return Forbid();
        }

        TempData["Flash"] = await commands.ReenableAsync(Id, User.UserId(), ct)
            ? "Messages re-enabled. The guest can be invited or messaged again."
            : "Nothing to change.";
        return RedirectToPage(new { id = Id });
    }
}
