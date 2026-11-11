using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Api;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

/// <summary>Step 4: guest table filters and search, summary tiles, guest detail actions.</summary>
public sealed class DashboardTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    private async Task<int> SampleEventIdAsync() => await WithDbAsync(db => db.Events.Where(e => e.Name == DemoSeeder.SampleEventName).Select(e => e.Id).SingleAsync());

    [Fact]
    public async Task Guest_list_filters_by_status_and_searches_by_name_and_number()
    {
        var eventId = await SampleEventIdAsync();
        var attending = await CreateGuestAsync();
        await InviteAsync(attending);
        foreach (var t in new[] { "yes", "1", "none", "no", "skip" })
        {
            await SayAsync(attending, t);
        }

        var staff = await Factory.SignedInClientAsync();

        var all = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?pageSize=100");
        var attendingOnly = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?status=Attending&pageSize=100");
        var byName = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?q=THANDI");
        var byNumber = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?q=000003");
        var unknownFilter = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?status=bogus");

        Assert.True(all!.Total >= 12);
        Assert.Contains(attendingOnly!.Guests, g => g.Id == attending);
        Assert.All(attendingOnly.Guests, g => Assert.Equal("Attending", g.Status));
        Assert.Equal("Thandi Mokoena", Assert.Single(byName!.Guests).Name);
        Assert.Equal("+27000000003", Assert.Single(byNumber!.Guests).Phone);
        Assert.Equal(0, unknownFilter!.Total);
    }

    [Fact]
    public async Task Guest_list_pages()
    {
        var eventId = await SampleEventIdAsync();
        var staff = await Factory.SignedInClientAsync();

        var page1 = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?pageSize=5&page=1");
        var page2 = await staff.GetFromJsonAsync<GuestPageDto>($"/api/events/{eventId}/guests?pageSize=5&page=2");

        Assert.Equal(5, page1!.Guests.Count);
        Assert.Equal(5, page2!.Guests.Count);
        Assert.Empty(page1.Guests.Select(g => g.Id).Intersect(page2.Guests.Select(g => g.Id)));
    }

    [Fact]
    public async Task Summary_counts_confirmed_attendance_only()
    {
        // A separate event, so other tests' guests don't affect the numbers.
        var eventId = await WithDbAsync(async db =>
        {
            var ev = new Event { Name = "Summary test", StartsAt = DateTimeOffset.UtcNow.AddDays(20), TimeZoneId = "Africa/Johannesburg", Venue = "Test", Capacity = 10, IsSample = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            return ev.Id;
        });
        var ids = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(await WithDbAsync(async db =>
            {
                var g = new Guest { EventId = eventId, Name = $"S{i}", PhoneE164 = $"+2708000000{i}", ConsentAt = DateTimeOffset.UtcNow, ConsentSource = "Test" };
                db.Guests.Add(g);
                await db.SaveChangesAsync();
                return g.Id;
            }));
        }

        foreach (var id in ids.Take(3))
        {
            await InviteAsync(id);
        }

        foreach (var t in new[] { "yes", "3", "none", "none", "none", "yes", "2", "skip" })
        {
            await SayAsync(ids[0], t);
        }

        await SayAsync(ids[1], "no");
        await SayAsync(ids[2], "yes");
        await SayAsync(ids[2], "2"); // in chat, not confirmed: must not count
        await SayAsync(ids[2], "help");

        var summary = await WithDbAsync(db => new Guests.GuestQueries(db).SummaryAsync(eventId, CancellationToken.None));

        Assert.NotNull(summary);
        Assert.Equal(3, summary.Headcount);
        Assert.Equal(1, summary.AttendingParties);
        Assert.Equal(2, summary.WheelchairUsers);
        Assert.Equal(1, summary.Declined);
        Assert.Equal(1, summary.AwaitingReply);
        Assert.Equal(1, summary.NotInvited);
        Assert.Equal(1, summary.NeedsAttention);
        Assert.Equal(10, summary.Capacity);
    }

    [Fact]
    public async Task Overview_page_shows_tiles_and_guests()
    {
        var eventId = await SampleEventIdAsync();
        var staff = await Factory.SignedInClientAsync();

        var html = await staff.GetStringAsync($"/Events/{eventId}?status=NotInvited&q=aisha");

        Assert.Contains("Headcount", html);
        Assert.Contains("Aisha Patel", html);
        Assert.DoesNotContain("Thandi Mokoena", html);
        Assert.Contains("Sample, fictional", html);
    }

    [Fact]
    public async Task Organiser_can_add_a_guest_with_a_normalised_number()
    {
        var eventId = await SampleEventIdAsync();
        var staff = await Factory.SignedInClientAsync();

        var response = await staff.PostFormAsync($"/Events/{eventId}/Guests/Add", new()
        {
            ["Input.Name"] = "New Person",
            ["Input.Phone"] = "082 555 0199",
            ["Input.Consent"] = "true",
            ["Input.ConsentSource"] = "Phoned, 10 Nov",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var guest = await WithDbAsync(db => db.Guests.SingleAsync(g => g.Name == "New Person"));
        Assert.Equal("+27825550199", guest.PhoneE164);
        Assert.NotNull(guest.ConsentAt);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "GuestAdded" && a.Target == $"guest:{guest.Id}")));
    }

    [Theory]
    [InlineData("", "082 555 0100", "Enter the guest&#x27;s name.")]
    [InlineData("Someone", "12", "Enter a mobile number")]
    [InlineData("Someone", "+27000000001", "already has this number")]
    public async Task Adding_a_guest_validates_input(string name, string phone, string expected)
    {
        var eventId = await SampleEventIdAsync();
        var staff = await Factory.SignedInClientAsync();

        var response = await staff.PostFormAsync($"/Events/{eventId}/Guests/Add", new() { ["Input.Name"] = name, ["Input.Phone"] = phone });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Consent_needs_a_source()
    {
        var eventId = await SampleEventIdAsync();
        var staff = await Factory.SignedInClientAsync();

        var response = await staff.PostFormAsync($"/Events/{eventId}/Guests/Add", new() { ["Input.Name"] = "X", ["Input.Phone"] = "0825550111", ["Input.Consent"] = "true" });

        Assert.Contains("Say how consent was given", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Guest_detail_shows_the_conversation_and_can_mark_help_handled()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "help");
        var staff = await Factory.SignedInClientAsync();

        var html = await staff.GetStringAsync($"/Guests/{id}");
        Assert.Contains("Needs attention.", html);
        Assert.Contains("You&#x27;re invited to Year-End Function 2026", html);

        var post = await staff.PostFormAsync($"/Guests/{id}?handler=Handled", [], getUrl: $"/Guests/{id}");
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.False((await GetGuestAsync(id)).NeedsAttention);
    }

    [Fact]
    public async Task Organiser_can_reenable_an_opted_out_guest()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "stop");
        var staff = await Factory.SignedInClientAsync();

        await staff.PostFormAsync($"/Guests/{id}?handler=Reenable", [], getUrl: $"/Guests/{id}");

        var guest = await GetGuestAsync(id);
        Assert.Null(guest.OptedOutAt);
        Assert.Equal("NotInvited", guest.Step);
        await InviteAsync(id);
        await SayAsync(id, "yes");
        Assert.Equal("Party", (await GetGuestAsync(id)).Step);
    }

    [Fact]
    public async Task Viewer_cannot_post_organiser_actions()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "help");
        await Factory.CreateUserAsync($"viewer-{id}@test.example", UserRole.Viewer);
        var viewer = await Factory.SignedInClientAsync($"viewer-{id}@test.example");

        var html = await viewer.GetStringAsync($"/Guests/{id}");
        Assert.DoesNotContain("You&#x27;re invited", html);
        var post = await viewer.PostFormAsync($"/Guests/{id}?handler=Handled", [], getUrl: $"/Guests/{id}");

        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.True((await GetGuestAsync(id)).NeedsAttention);
    }

    [Fact]
    public async Task Organiser_can_create_and_edit_an_event()
    {
        var staff = await Factory.SignedInClientAsync();

        var create = await staff.PostFormAsync("/Events/Create", new()
        {
            ["Input.Name"] = "Spring Lunch",
            ["Input.StartsAtLocal"] = "2027-03-20T12:30",
            ["Input.TimeZoneId"] = "Africa/Johannesburg",
            ["Input.Venue"] = "Garden Room",
            ["Input.Capacity"] = "40",
            ["Input.PartyMax"] = "2",
            ["Input.ReminderAfterDays"] = "3",
            ["Input.DeleteDataAfterDays"] = "14",
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        var ev = await WithDbAsync(db => db.Events.SingleAsync(e => e.Name == "Spring Lunch"));
        Assert.Equal(new DateTimeOffset(2027, 3, 20, 10, 30, 0, TimeSpan.Zero), ev.StartsAt);
        Assert.Equal(2, ev.PartyMax);

        var bad = await staff.PostFormAsync($"/Events/{ev.Id}/Edit", new()
        {
            ["Input.Name"] = "Spring Lunch",
            ["Input.StartsAtLocal"] = "2027-03-20T12:30",
            ["Input.TimeZoneId"] = "Mars/Olympus",
            ["Input.Venue"] = "Garden Room",
            ["Input.Capacity"] = "40",
            ["Input.PartyMax"] = "99",
            ["Input.ReminderAfterDays"] = "3",
            ["Input.DeleteDataAfterDays"] = "14",
        });
        var html = await bad.Content.ReadAsStringAsync();
        Assert.Contains("IANA time zone", html);
        Assert.Contains("Party size must be between 1 and 10", html);
    }
}
