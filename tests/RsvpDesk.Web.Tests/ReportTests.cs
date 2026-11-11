using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Reports;

namespace RsvpDesk.Web.Tests;

/// <summary>Step 5: catering and access report and CSV exports, on an event of their own.</summary>
public sealed class ReportTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    private async Task<(int EventId, int[] Guests)> EventWithGuestsAsync(int count)
    {
        var eventId = await WithDbAsync(async db =>
        {
            var ev = new Event { Name = $"Report Test {Guid.NewGuid():N}"[..20], StartsAt = DateTimeOffset.UtcNow.AddDays(10), TimeZoneId = "Africa/Johannesburg", Venue = "Hall", Capacity = 50, IsSample = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            return ev.Id;
        });
        var ids = new int[count];
        for (var i = 0; i < count; i++)
        {
            var name = i == 0 ? "=cmd|' /C calc'!A0" : $"Guest, Number {i} \"Zoë\"";
            ids[i] = await WithDbAsync(async db =>
            {
                var g = new Guest { EventId = eventId, Name = name, PhoneE164 = $"+2707{eventId:000}{i:00000}", ConsentAt = DateTimeOffset.UtcNow, ConsentSource = "Test" };
                db.Guests.Add(g);
                await db.SaveChangesAsync();
                return g.Id;
            });
            await InviteAsync(ids[i]);
        }

        return (eventId, ids);
    }

    private async Task RsvpAsync(int guestId, params string[] answers)
    {
        foreach (var a in answers)
        {
            await SayAsync(guestId, a);
        }
    }

    [Fact]
    public async Task Report_counts_only_confirmed_guests()
    {
        var (eventId, g) = await EventWithGuestsAsync(4);
        await RsvpAsync(g[0], "yes", "3", "vegetarian", "other", "Nut allergy", "halal", "yes", "2", "Ramp at side door");
        await RsvpAsync(g[1], "yes", "1", "vegetarian", "no", "skip");
        await RsvpAsync(g[2], "yes", "2", "vegan"); // in progress: excluded
        await RsvpAsync(g[3], "no");

        var report = await WithDbAsync(db => new CateringReport(db).BuildAsync(eventId, CancellationToken.None));

        Assert.Equal(2, report.ConfirmedParties);
        Assert.Equal(4, report.ConfirmedPeople);
        Assert.Equal(1, report.InProgressGuests);
        Assert.Equal(2, report.Diets.Single(d => d.Dietary == Engine.Dietary.Vegetarian).People);
        Assert.Equal(0.5, report.Diets.Single(d => d.Dietary == Engine.Dietary.Vegetarian).Share);
        Assert.Equal(0, report.Diets.Single(d => d.Dietary == Engine.Dietary.Vegan).People);
        var other = Assert.Single(report.Others);
        Assert.Equal(("Nut allergy", 2), (other.Note, other.PersonIndex));
        Assert.Equal(2, report.WheelchairUsers);
        var party = Assert.Single(report.AccessParties);
        Assert.Equal("Ramp at side door", party.AccessNote);
    }

    [Fact]
    public async Task Guest_list_export_is_safe_CSV_and_is_audited()
    {
        var (eventId, g) = await EventWithGuestsAsync(2);
        await RsvpAsync(g[0], "yes", "2", "other", "=1+1, \"spicy\"", "vegan", "no", "-rm note");
        var staff = await Factory.SignedInClientAsync();

        var response = await staff.GetAsync($"/api/events/{eventId}/export/guests.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Csv.Utf8Bom, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        var lines = text.Split("\r\n");
        Assert.StartsWith("Name,Phone,Status,Party size,Dietary requirements,Wheelchair users,Access note,Last activity", lines[0]);
        Assert.Contains("'=cmd|' /C calc'!A0,+2707", text); // name guarded, phone untouched
        // "=" inside the cell is harmless: only a cell's first character matters to spreadsheets.
        Assert.Contains("\"1: Other (=1+1, \"\"spicy\"\"); 2: Vegan\"", text);
        Assert.Contains(",'-rm note,", text);
        Assert.Contains("\"Guest, Number 1 \"\"Zoë\"\"\"", text);
        Assert.Equal(4, lines.Length); // header + 2 rows + trailing empty
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "ExportGuests" && a.Target == $"event:{eventId}")));
    }

    [Fact]
    public async Task Catering_export_lists_every_requirement_with_details_for_Other()
    {
        var (eventId, g) = await EventWithGuestsAsync(1);
        await RsvpAsync(g[0], "yes", "2", "halal", "other", "No onions", "no", "skip");
        var staff = await Factory.SignedInClientAsync();

        var text = Encoding.UTF8.GetString((await staff.GetByteArrayAsync($"/api/events/{eventId}/export/catering.csv"))[3..]);

        Assert.StartsWith("Requirement,Number of people,Details for Other\r\n", text);
        Assert.Contains("No requirements,0,\r\n", text);
        Assert.Contains("Halal,1,\r\n", text);
        Assert.Contains("Other,1,\"No onions (", text);
        Assert.Contains(", guest 2)", text);
        Assert.Contains("Total (confirmed attending only),2,1 parties", text);
    }

    [Fact]
    public async Task Viewers_cannot_export_or_open_the_report()
    {
        await Factory.CreateUserAsync("viewer-report@test.example", UserRole.Viewer);
        var viewer = await Factory.SignedInClientAsync("viewer-report@test.example");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/events/1/export/guests.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/events/1/export/catering.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await viewer.GetAsync("/Events/1/Reports")).StatusCode);
    }

    [Fact]
    public async Task Report_page_says_only_confirmed_guests_are_counted_and_is_audited()
    {
        var (eventId, g) = await EventWithGuestsAsync(1);
        await RsvpAsync(g[0], "yes", "1");
        var staff = await Factory.SignedInClientAsync();

        var html = await staff.GetStringAsync($"/Events/{eventId}/Reports");

        Assert.Contains("Only confirmed attending guests are counted", html);
        Assert.Contains("1 guest is", html);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "ReportViewed" && a.Target == $"event:{eventId}")));
    }

    [Fact]
    public async Task Exports_of_unknown_events_are_404()
    {
        var staff = await Factory.SignedInClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/events/99999/export/guests.csv")).StatusCode);
    }
}
