using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Privacy;

namespace RsvpDesk.Web.Tests;

/// <summary>Step 8: retention, deletion on request, audit log page, security headers.</summary>
public sealed class HardeningTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    private async Task<int> PastSampleEventAsync(int daysAgo, int deleteAfterDays, bool isSample = true) => await WithDbAsync(async db =>
    {
        var ev = new Event
        {
            Name = $"Retention {Guid.NewGuid():N}"[..20],
            StartsAt = Factory.Clock.GetUtcNow().AddDays(-daysAgo),
            TimeZoneId = "Africa/Johannesburg",
            Venue = "Hall",
            Capacity = 10,
            DeleteDataAfterDays = deleteAfterDays,
            IsSample = isSample,
            CreatedAt = Factory.Clock.GetUtcNow(),
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    });

    private static int _phone;

    private async Task<int> GuestOnAsync(int eventId) => await WithDbAsync(async db =>
    {
        var g = new Guest { EventId = eventId, Name = "Private Person", PhoneE164 = $"+2704{Interlocked.Increment(ref _phone):0000000}", ConsentAt = DateTimeOffset.UtcNow, ConsentSource = "Form" };
        db.Guests.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    });

    private async Task<RetentionResult> RunRetentionAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Guest_data_is_anonymised_after_the_retention_period_but_not_before()
    {
        // The simulator only works on sample events, so make the RSVP first, then turn the flag off.
        var eventId = await PastSampleEventAsync(daysAgo: -5, deleteAfterDays: 10);
        var guestId = await GuestOnAsync(eventId);
        await InviteAsync(guestId);
        foreach (var t in new[] { "yes", "2", "halal", "other", "Nut allergy", "yes", "1", "Ramp" })
        {
            await SayAsync(guestId, t);
        }

        await WithDbAsync(async db =>
        {
            var ev = await db.Events.SingleAsync(e => e.Id == eventId);
            ev.IsSample = false;
            ev.StartsAt = Factory.Clock.GetUtcNow().AddDays(-9); // event 9 days ago, kept for 10
            return await db.SaveChangesAsync();
        });

        await RunRetentionAsync();
        Assert.Equal("Private Person", (await WithDbAsync(db => db.Guests.SingleAsync(g => g.Id == guestId))).Name);

        Factory.Clock.Advance(TimeSpan.FromDays(1));
        var result = await RunRetentionAsync();

        Assert.True(result.EventsAnonymised >= 1);
        var guest = await WithDbAsync(db => db.Guests.Include(g => g.Rsvp!).ThenInclude(r => r.Persons).SingleAsync(g => g.Id == guestId));
        Assert.Equal($"Deleted guest {guestId}", guest.Name);
        Assert.Equal($"deleted:{guestId}", guest.PhoneE164);
        Assert.Null(guest.ConsentSource);
        Assert.Empty(guest.Rsvp!.Persons);
        Assert.Null(guest.Rsvp.AccessNote);
        Assert.Null(guest.Rsvp.WheelchairUsers);
        Assert.Equal(2, guest.Rsvp.PartySize); // anonymous headcount kept
        Assert.Equal(0, await WithDbAsync(db => db.Messages.CountAsync(m => m.GuestId == guestId)));
        Assert.Equal(0, await WithDbAsync(db => db.Conversations.CountAsync(c => c.GuestId == guestId)));
        Assert.NotNull((await WithDbAsync(db => db.Events.SingleAsync(e => e.Id == eventId))).GuestDataDeletedAt);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "RetentionAnonymisedEvent" && a.Target.StartsWith($"event:{eventId} "))));
    }

    [Fact]
    public async Task Sample_events_are_never_anonymised()
    {
        var eventId = await PastSampleEventAsync(daysAgo: 400, deleteAfterDays: 1, isSample: true);
        var guestId = await GuestOnAsync(eventId);

        await RunRetentionAsync();

        Assert.Equal("Private Person", (await WithDbAsync(db => db.Guests.SingleAsync(g => g.Id == guestId))).Name);
    }

    [Fact]
    public async Task Old_raw_webhook_payloads_are_deleted()
    {
        var old = await WithDbAsync(async db =>
        {
            var e = new WebhookEvent { Source = "simulator", ExternalId = $"old-{Guid.NewGuid():N}", RawJson = "{}", ReceivedAt = Factory.Clock.GetUtcNow().AddDays(-31), ProcessedAt = Factory.Clock.GetUtcNow().AddDays(-31) };
            db.WebhookEvents.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

        await RunRetentionAsync();

        Assert.False(await WithDbAsync(db => db.WebhookEvents.AnyAsync(e => e.Id == old)));
    }

    [Fact]
    public async Task Organiser_can_delete_a_guest_and_all_their_data()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "yes", messageId: $"delete-me-{id}");
        var staff = await Factory.SignedInClientAsync();

        var response = await staff.PostFormAsync($"/Guests/{id}?handler=Delete", [], getUrl: $"/Guests/{id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(await WithDbAsync(db => db.Guests.AnyAsync(g => g.Id == id)));
        Assert.False(await WithDbAsync(db => db.Messages.AnyAsync(m => m.GuestId == id)));
        Assert.False(await WithDbAsync(db => db.WebhookEvents.AnyAsync(e => e.ExternalId == $"delete-me-{id}")));
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "GuestDeleted" && a.Target == $"guest:{id}")));
    }

    [Fact]
    public async Task Audit_log_page_is_for_organisers()
    {
        var organiser = await Factory.SignedInClientAsync();
        await Factory.CreateUserAsync("viewer-audit@test.example", UserRole.Viewer);
        var viewer = await Factory.SignedInClientAsync("viewer-audit@test.example");

        var html = await organiser.GetStringAsync("/Admin/Audit");

        Assert.Contains("Login", html);
        Assert.Contains(RsvpWebFactory.OrganiserEmail, html);
        Assert.Equal(HttpStatusCode.Redirect, (await viewer.GetAsync("/Admin/Audit")).StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/health")]
    public async Task Responses_carry_security_headers(string url)
    {
        var response = await Factory.NewClient().GetAsync(url);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public void Pages_have_no_inline_scripts_or_handlers()
    {
        var pages = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/RsvpDesk.Web/Pages"));
        foreach (var file in Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"<script>|<script\s+(?![^>]*\bsrc=)[^>]*>|\son(submit|click|load|change)=", text);
        }
    }
}
