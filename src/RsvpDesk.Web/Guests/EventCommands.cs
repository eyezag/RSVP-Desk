using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Guests;

public sealed class EventInput
{
    public string Name { get; set; } = "";
    public DateTime StartsAtLocal { get; set; } = DateTime.Today.AddDays(30).AddHours(18);
    public string TimeZoneId { get; set; } = "Africa/Johannesburg";
    public string Venue { get; set; } = "";
    public int Capacity { get; set; } = 60;
    public int PartyMax { get; set; } = 4;
    public int DeleteDataAfterDays { get; set; } = 30;
    public int ReminderAfterDays { get; set; } = 3;
}

public sealed class EventCommands(RsvpDbContext db, AuditLog audit, TimeProvider clock)
{
    public const int MaxPartySize = 10;

    public static EventInput ToInput(Event e) => new()
    {
        Name = e.Name,
        StartsAtLocal = e.LocalStartsAt().DateTime,
        TimeZoneId = e.TimeZoneId,
        Venue = e.Venue,
        Capacity = e.Capacity,
        PartyMax = e.PartyMax,
        DeleteDataAfterDays = e.DeleteDataAfterDays,
        ReminderAfterDays = e.ReminderAfterDays,
    };

    public async Task<CommandResult> SaveAsync(int? eventId, EventInput input, int? userId, CancellationToken ct)
    {
        var errors = Validate(input, out var zone);
        if (errors.Count > 0)
        {
            return new CommandResult(false, Errors: errors);
        }

        Event? ev;
        if (eventId is null)
        {
            ev = new Event { Name = "", TimeZoneId = "", Venue = "", CreatedAt = clock.GetUtcNow() };
            db.Events.Add(ev);
        }
        else
        {
            ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (ev is null)
            {
                return CommandResult.Fail("event", "Event not found.");
            }

            if (input.PartyMax < ev.PartyMax && await db.Rsvps.AnyAsync(r => r.PartySize > input.PartyMax && db.Guests.Any(g => g.Id == r.GuestId && g.EventId == ev.Id), ct))
            {
                return CommandResult.Fail(nameof(EventInput.PartyMax), "Some guests have already replied with a larger party.");
            }
        }

        var local = DateTime.SpecifyKind(input.StartsAtLocal, DateTimeKind.Unspecified);
        ev.Name = input.Name.Trim();
        ev.StartsAt = new DateTimeOffset(local, zone!.GetUtcOffset(local)).ToUniversalTime();
        ev.TimeZoneId = input.TimeZoneId;
        ev.Venue = input.Venue.Trim();
        ev.Capacity = input.Capacity;
        ev.PartyMax = input.PartyMax;
        ev.DeleteDataAfterDays = input.DeleteDataAfterDays;
        ev.ReminderAfterDays = input.ReminderAfterDays;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, eventId is null ? "EventCreated" : "EventEdited", $"event:{ev.Id}", ct);
        return new CommandResult(true, ev.Id);
    }

    private static Dictionary<string, string> Validate(EventInput input, out TimeZoneInfo? zone)
    {
        var errors = new Dictionary<string, string>();
        zone = null;
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
        {
            errors[nameof(input.Name)] = "Enter a name of up to 200 characters.";
        }

        if (string.IsNullOrWhiteSpace(input.Venue) || input.Venue.Trim().Length > 300)
        {
            errors[nameof(input.Venue)] = "Enter a venue of up to 300 characters.";
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            errors[nameof(input.TimeZoneId)] = "Use an IANA time zone, for example Africa/Johannesburg.";
        }

        if (input.Capacity is < 1 or > 100_000)
        {
            errors[nameof(input.Capacity)] = "Capacity must be between 1 and 100 000.";
        }

        if (input.PartyMax is < 1 or > MaxPartySize)
        {
            errors[nameof(input.PartyMax)] = string.Create(CultureInfo.InvariantCulture, $"Party size must be between 1 and {MaxPartySize}.");
        }

        if (input.DeleteDataAfterDays is < 1 or > 3650)
        {
            errors[nameof(input.DeleteDataAfterDays)] = "Choose between 1 and 3650 days.";
        }

        if (input.ReminderAfterDays is < 1 or > 60)
        {
            errors[nameof(input.ReminderAfterDays)] = "Choose between 1 and 60 days.";
        }

        return errors;
    }
}
