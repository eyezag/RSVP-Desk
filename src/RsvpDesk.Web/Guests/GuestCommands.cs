using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Guests;

public sealed record GuestInput(string Name, string Phone, bool Consent, string? ConsentSource);

public sealed record CommandResult(bool Ok, int? Id = null, IReadOnlyDictionary<string, string>? Errors = null)
{
    public static CommandResult Fail(string field, string message) => new(false, Errors: new Dictionary<string, string> { [field] = message });
}

/// <summary>Organiser actions on guests. Every change is written to the audit log.</summary>
public sealed class GuestCommands(RsvpDbContext db, AuditLog audit, TimeProvider clock)
{
    public const int NameMaxLength = 200;
    public const int ConsentSourceMaxLength = 200;

    public async Task<CommandResult> AddAsync(int eventId, GuestInput input, int? userId, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct))
        {
            return CommandResult.Fail("event", "Event not found.");
        }

        var validation = await ValidateAsync(eventId, null, input, ct);
        if (validation.Errors is not null)
        {
            return validation;
        }

        var now = clock.GetUtcNow();
        var guest = new Guest
        {
            EventId = eventId,
            Name = input.Name.Trim(),
            PhoneE164 = NormalisedPhone(input),
            ConsentAt = input.Consent ? now : null,
            ConsentSource = input.Consent ? input.ConsentSource?.Trim() : null,
        };
        db.Guests.Add(guest);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "GuestAdded", $"guest:{guest.Id}", ct);
        return new CommandResult(true, guest.Id);
    }

    public async Task<CommandResult> UpdateAsync(int guestId, GuestInput input, int? userId, CancellationToken ct)
    {
        var guest = await db.Guests.FirstOrDefaultAsync(g => g.Id == guestId, ct);
        if (guest is null)
        {
            return CommandResult.Fail("guest", "Guest not found.");
        }

        var validation = await ValidateAsync(guest.EventId, guest.Id, input, ct);
        if (validation.Errors is not null)
        {
            return validation;
        }

        guest.Name = input.Name.Trim();
        guest.PhoneE164 = NormalisedPhone(input);
        if (input.Consent && guest.ConsentAt is null)
        {
            guest.ConsentAt = clock.GetUtcNow();
            guest.ConsentSource = input.ConsentSource?.Trim();
        }
        else if (!input.Consent)
        {
            guest.ConsentAt = null;
            guest.ConsentSource = null;
        }
        else
        {
            guest.ConsentSource = input.ConsentSource?.Trim();
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "GuestEdited", $"guest:{guest.Id}", ct);
        return new CommandResult(true, guest.Id);
    }

    public async Task<bool> MarkHandledAsync(int guestId, int? userId, CancellationToken ct)
    {
        var guest = await db.Guests.FirstOrDefaultAsync(g => g.Id == guestId, ct);
        if (guest is null)
        {
            return false;
        }

        guest.NeedsAttention = false;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "AttentionHandled", $"guest:{guest.Id}", ct);
        return true;
    }

    /// <summary>
    /// Lets an opted-out guest receive messages again (spec 6.2: only an organiser can).
    /// A finished RSVP goes back to its finished state; otherwise the guest can be invited again.
    /// </summary>
    public async Task<bool> ReenableAsync(int guestId, int? userId, CancellationToken ct)
    {
        var guest = await db.Guests.Include(g => g.Conversation).Include(g => g.Rsvp).FirstOrDefaultAsync(g => g.Id == guestId, ct);
        if (guest is null || guest.OptedOutAt is null)
        {
            return false;
        }

        guest.OptedOutAt = null;
        if (guest.Conversation is { } conversation)
        {
            var state = StateSerializer.Deserialise(conversation.StateData);
            var step = guest.Rsvp?.ConfirmedAt is null ? Step.NotInvited : guest.Rsvp.Attending == true ? Step.Confirmed : Step.Declined;
            state = state with { Step = step, InvalidStreak = 0 };
            conversation.State = step;
            conversation.StateData = StateSerializer.Serialise(state);
            conversation.UpdatedAt = clock.GetUtcNow();
            conversation.Version++;
            guest.Status = GuestStatusRules.Next(step, step == Step.NotInvited ? GuestStatus.NotInvited : guest.Status, guestReplied: false);
            if (step == Step.NotInvited)
            {
                guest.InviteSentAt = null;
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "MessagesReenabled", $"guest:{guest.Id}", ct);
        return true;
    }

    private async Task<CommandResult> ValidateAsync(int eventId, int? guestId, GuestInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors["Name"] = "Enter the guest's name.";
        }
        else if (input.Name.Trim().Length > NameMaxLength)
        {
            errors["Name"] = $"Names are limited to {NameMaxLength} characters.";
        }

        if (!PhoneNumbers.TryNormalise(input.Phone, out var phone))
        {
            errors["Phone"] = "Enter a mobile number, for example +27 82 123 4567 or 082 123 4567.";
        }
        else if (await db.Guests.AnyAsync(g => g.EventId == eventId && g.PhoneE164 == phone && g.Id != guestId, ct))
        {
            errors["Phone"] = "Another guest on this event already has this number.";
        }

        if (input.Consent && string.IsNullOrWhiteSpace(input.ConsentSource))
        {
            errors["ConsentSource"] = "Say how consent was given, for example \"Registration form, 3 Nov\".";
        }
        else if (input.ConsentSource?.Trim().Length > ConsentSourceMaxLength)
        {
            errors["ConsentSource"] = $"Limited to {ConsentSourceMaxLength} characters.";
        }

        return errors.Count == 0 ? new CommandResult(true) : new CommandResult(false, Errors: errors);
    }

    private static string NormalisedPhone(GuestInput input) => PhoneNumbers.TryNormalise(input.Phone, out var phone) ? phone : throw new InvalidOperationException();
}
