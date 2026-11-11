using Microsoft.EntityFrameworkCore;

namespace RsvpDesk.Web.Data;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Create the fictional sample event and guests when the database has no events.</summary>
    public bool SeedSampleEvent { get; set; } = true;

    /// <summary>
    /// Show the initial organiser's credentials on the sign-in page, so visitors to the public demo
    /// can look around. Ignored in live WhatsApp mode.
    /// </summary>
    public bool ShowLoginHint { get; set; }
}

/// <summary>
/// The fictional sample event from spec section 2. Every name, number and the venue are made up.
/// Phone numbers use the unallocated +27 00 range so they can never reach a real person.
/// </summary>
public static class DemoSeeder
{
    public const string SampleEventName = "Year-End Function 2026";

    private static readonly (string Name, bool Consented)[] SampleGuests =
    [
        ("Thandi Mokoena", true),
        ("Pieter van der Berg", true),
        ("Aisha Patel", true),
        ("Sipho Dlamini", true),
        ("Lerato Nkosi", true),
        ("Johan Botha", true),
        ("Fatima Hendricks", true),
        ("Kagiso Molefe", true),
        ("Nomvula Khumalo", true),
        ("Ravi Naidoo", true),
        ("Annelie Steyn", true),
        ("Zanele Mthembu", false),
    ];

    public static async Task SeedAsync(RsvpDbContext db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        if (await db.Events.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var ev = new Event
        {
            Name = SampleEventName,
            // Sat 5 Dec 2026, 18:00 in Johannesburg (UTC+2).
            StartsAt = new DateTimeOffset(2026, 12, 5, 16, 0, 0, TimeSpan.Zero),
            TimeZoneId = "Africa/Johannesburg",
            Venue = "The Jacaranda Hall, Johannesburg (fictional venue)",
            Capacity = 60,
            PartyMax = 4,
            DeleteDataAfterDays = 30,
            IsSample = true,
            CreatedAt = now,
        };

        for (var i = 0; i < SampleGuests.Length; i++)
        {
            var (name, consented) = SampleGuests[i];
            ev.Guests.Add(new Guest
            {
                Name = name,
                PhoneE164 = $"+2700000{i + 1:0000}",
                ConsentAt = consented ? now : null,
                ConsentSource = consented ? "Sample data" : null,
            });
        }

        db.Events.Add(ev);
        await db.SaveChangesAsync(cancellationToken);
    }
}
