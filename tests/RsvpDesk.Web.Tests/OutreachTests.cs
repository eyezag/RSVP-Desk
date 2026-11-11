using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

/// <summary>Step 6: bulk invitations and reminder rules. Each test uses its own event.</summary>
public sealed class OutreachTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    private async Task<int> NewEventAsync(int reminderAfterDays = 3, int daysUntilEvent = 30) => await WithDbAsync(async db =>
    {
        var ev = new Event
        {
            Name = $"Outreach {Guid.NewGuid():N}"[..18],
            StartsAt = Factory.Clock.GetUtcNow().AddDays(daysUntilEvent),
            TimeZoneId = "Africa/Johannesburg",
            Venue = "Hall",
            Capacity = 100,
            ReminderAfterDays = reminderAfterDays,
            IsSample = true,
            CreatedAt = Factory.Clock.GetUtcNow(),
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    });

    private static int _phone;

    private async Task<int> AddGuestAsync(int eventId, bool consent = true, bool optedOut = false) => await WithDbAsync(async db =>
    {
        var g = new Guest
        {
            EventId = eventId,
            Name = "Outreach guest",
            PhoneE164 = $"+2706{Interlocked.Increment(ref _phone):0000000}",
            ConsentAt = consent ? Factory.Clock.GetUtcNow() : null,
            ConsentSource = consent ? "Test" : null,
            OptedOutAt = optedOut ? Factory.Clock.GetUtcNow() : null,
        };
        db.Guests.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    });

    private async Task<HttpResponseMessage> PostWithTokenAsync(HttpClient staff, string url, int eventId)
    {
        var token = await staff.AntiforgeryTokenAsync($"/Events/{eventId}");
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("RequestVerificationToken", token);
        return await staff.SendAsync(request);
    }

    [Fact]
    public async Task Bulk_invitations_skip_guests_without_consent_or_who_opted_out()
    {
        var eventId = await NewEventAsync();
        var ok1 = await AddGuestAsync(eventId);
        var ok2 = await AddGuestAsync(eventId);
        var noConsent = await AddGuestAsync(eventId, consent: false);
        var optedOut = await AddGuestAsync(eventId, optedOut: true);
        var staff = await Factory.SignedInClientAsync();

        var response = await PostWithTokenAsync(staff, $"/api/events/{eventId}/invitations", eventId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<OutreachResult>();
        Assert.Equal(new OutreachResult(2, 0, 1, 1, false), result);
        Assert.Equal("Invited", (await GetGuestAsync(ok1)).Status);
        Assert.Equal("Invited", (await GetGuestAsync(ok2)).Status);
        Assert.Empty((await GetGuestAsync(noConsent)).Messages);
        Assert.Empty((await GetGuestAsync(optedOut)).Messages);

        // Running it again sends nothing new.
        var again = await (await PostWithTokenAsync(staff, $"/api/events/{eventId}/invitations", eventId)).Content.ReadFromJsonAsync<OutreachResult>();
        Assert.Equal(0, again!.Sent);
        Assert.Single((await GetGuestAsync(ok1)).Messages);
    }

    [Fact]
    public async Task Outreach_API_needs_the_antiforgery_header()
    {
        var eventId = await NewEventAsync();
        var staff = await Factory.SignedInClientAsync();

        var response = await staff.PostAsync($"/api/events/{eventId}/invitations", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_send()
    {
        var eventId = await NewEventAsync();
        await Factory.CreateUserAsync($"viewer-out-{eventId}@test.example", UserRole.Viewer);
        var viewer = await Factory.SignedInClientAsync($"viewer-out-{eventId}@test.example");

        var response = await viewer.PostAsync($"/api/events/{eventId}/reminders", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reminder_rules()
    {
        var eventId = await NewEventAsync(reminderAfterDays: 3);
        var silent = await AddGuestAsync(eventId);       // invited, never replied: gets reminders
        var halfWay = await AddGuestAsync(eventId);      // answered some questions, then stopped
        var finished = await AddGuestAsync(eventId);     // confirmed: never reminded
        var declined = await AddGuestAsync(eventId);     // declined: never reminded
        var stopped = await AddGuestAsync(eventId);      // replied STOP: never reminded
        var helped = await AddGuestAsync(eventId);       // asked for HELP: organiser follows up instead
        var notInvited = await AddGuestAsync(eventId);   // never invited: never reminded

        using (var scope = Factory.Services.CreateScope())
        {
            var outreach = scope.ServiceProvider.GetRequiredService<OutreachService>();
            foreach (var id in new[] { silent, halfWay, finished, declined, stopped, helped })
            {
                await InviteAsync(id);
            }

            await SayAsync(halfWay, "yes");
            await SayAsync(halfWay, "2");
            foreach (var t in new[] { "yes", "1", "none", "no", "skip" })
            {
                await SayAsync(finished, t);
            }

            await SayAsync(declined, "no");
            await SayAsync(stopped, "stop");
            await SayAsync(helped, "help");

            // Too soon: nobody is due.
            Assert.Empty(await outreach.ReminderCandidatesAsync(eventId, CancellationToken.None));

            Factory.Clock.Advance(TimeSpan.FromDays(3));
            Assert.Equal([silent, halfWay], await outreach.ReminderCandidatesAsync(eventId, CancellationToken.None));

            var first = await outreach.SendRemindersAsync(eventId, null, CancellationToken.None);
            Assert.Equal(2, first!.Sent);

            // Just reminded: not due again until another interval has passed.
            Assert.Empty(await outreach.ReminderCandidatesAsync(eventId, CancellationToken.None));
            Factory.Clock.Advance(TimeSpan.FromDays(3));
            Assert.Equal(2, (await outreach.SendRemindersAsync(eventId, null, CancellationToken.None))!.Sent);

            // Two reminders is the maximum.
            Factory.Clock.Advance(TimeSpan.FromDays(3));
            Assert.Empty(await outreach.ReminderCandidatesAsync(eventId, CancellationToken.None));
        }

        Assert.DoesNotContain(notInvited, await WithDbAsync(db => db.Guests.Where(g => g.ReminderCount > 0).Select(g => g.Id).ToListAsync()));
        var halfWayGuest = await GetGuestAsync(halfWay);
        var reminder = halfWayGuest.Messages.Last(m => m.Direction == "Out");
        Assert.Contains("Just a reminder to finish your RSVP", reminder.Body);
        Assert.Contains("Any dietary requirement for you?", reminder.Body);

        // Answering the reminder continues where they left off.
        await SayAsync(halfWay, "vegan");
        Assert.Equal("Diet", (await GetGuestAsync(halfWay)).Step);
    }

    [Fact]
    public async Task A_guest_who_replied_recently_is_not_reminded()
    {
        var eventId = await NewEventAsync(reminderAfterDays: 3);
        var guest = await AddGuestAsync(eventId);
        await InviteAsync(guest);
        Factory.Clock.Advance(TimeSpan.FromDays(2));
        await SayAsync(guest, "yes");
        Factory.Clock.Advance(TimeSpan.FromDays(2)); // invited 4 days ago, but replied 2 days ago

        using var scope = Factory.Services.CreateScope();
        var outreach = scope.ServiceProvider.GetRequiredService<OutreachService>();
        Assert.Empty(await outreach.ReminderCandidatesAsync(eventId, CancellationToken.None));

        Factory.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal([guest], await outreach.ReminderCandidatesAsync(eventId, CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_is_sent_once_the_event_has_started()
    {
        var eventId = await NewEventAsync(daysUntilEvent: 1);
        var guest = await AddGuestAsync(eventId);
        Factory.Clock.Advance(TimeSpan.FromDays(2));

        using var scope = Factory.Services.CreateScope();
        var outreach = scope.ServiceProvider.GetRequiredService<OutreachService>();
        var result = await outreach.SendInvitationsAsync(eventId, null, CancellationToken.None);

        Assert.True(result!.EventInPast);
        Assert.Equal(0, result.Sent);
        Assert.Empty((await GetGuestAsync(guest)).Messages);
    }

    [Fact]
    public async Task Dashboard_buttons_send_invitations()
    {
        var eventId = await NewEventAsync();
        var guest = await AddGuestAsync(eventId);
        var staff = await Factory.SignedInClientAsync();

        Assert.Contains("Send invitations (1)", await staff.GetStringAsync($"/Events/{eventId}"));
        var post = await staff.PostFormAsync($"/Events/{eventId}?handler=Invite", [], getUrl: $"/Events/{eventId}");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("Sent 1 invitation.", await staff.GetStringAsync($"/Events/{eventId}"));
        Assert.Equal("Invited", (await GetGuestAsync(guest)).Status);
    }
}
