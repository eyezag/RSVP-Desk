using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.WhatsApp;

namespace RsvpDesk.Web.Tests;

/// <summary>The app in live WhatsApp mode, with graph.facebook.com replaced by a fake.</summary>
public sealed class WhatsAppWebFactory : RsvpWebFactory
{
    public FakeGraphApi Graph { get; } = new();

    protected override void ConfigureMore(IWebHostBuilder builder)
    {
        builder.UseSetting("Channel:Mode", "WhatsApp");
        builder.UseSetting("WhatsApp:PhoneNumberId", "100000000000001");
        builder.UseSetting("WhatsApp:AccessToken", "test-access-token");
        builder.UseSetting("WhatsApp:AppSecret", WhatsAppTestOptions.AppSecret);
        builder.UseSetting("WhatsApp:VerifyToken", WhatsAppTestOptions.VerifyToken);
        builder.UseSetting("WhatsApp:RetryBaseDelayMs", "1");
        builder.ConfigureTestServices(s => s.AddHttpClient<WhatsAppCloudChannel>().ConfigurePrimaryHttpMessageHandler(() => Graph));
    }
}

/// <summary>Spec 12 step 7 and 13 "Ingest", end to end through the webhook.</summary>
public sealed class WhatsAppLiveTests(WhatsAppWebFactory factory) : IClassFixture<WhatsAppWebFactory>
{
    private static int _phone;
    private readonly HttpClient _meta = factory.CreateClient();

    private async Task<T> Db<T>(Func<RsvpDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<RsvpDbContext>());
    }

    /// <summary>A consented guest with a unique number; returns the id and the number as WhatsApp writes it.</summary>
    private async Task<(int Id, string Digits, string UserId)> GuestAsync()
    {
        var n = Interlocked.Increment(ref _phone);
        var digits = $"2705{n:0000000}";
        var id = await Db(async db =>
        {
            var ev = await db.Events.FirstAsync(e => e.Name == DemoSeeder.SampleEventName);
            var g = new Guest { EventId = ev.Id, Name = $"Live {n}", PhoneE164 = "+" + digits, ConsentAt = DateTimeOffset.UtcNow, ConsentSource = "Test" };
            db.Guests.Add(g);
            await db.SaveChangesAsync();
            return g.Id;
        });
        return (id, digits, $"ZA.LIVE{n:0000000000}");
    }

    private async Task InviteAsync(int guestId)
    {
        using var scope = factory.Services.CreateScope();
        Assert.Equal(InviteOutcome.Sent, await scope.ServiceProvider.GetRequiredService<ConversationService>().InviteAsync(guestId, CancellationToken.None));
    }

    private static string TextWebhook(string digits, string userId, string messageId, string text, bool withPhone = true) =>
        Fixtures.Read(withPhone ? "text.json" : "username_only.json")
            .Replace("27000000001", digits)
            .Replace("ZA.000000000000000001", userId)
            .Replace(withPhone ? "wamid.FIXTURE.TEXT" : "wamid.FIXTURE.USERNAME", messageId)
            .Replace(withPhone ? "\"body\": \"yes\"" : "\"body\": \"2\"", $"\"body\": \"{text}\"");

    private static string StatusWebhook(string fixture, string providerId) => Fixtures.Read(fixture).Replace("wamid.FIXTURE.OUT.1", providerId);

    private async Task<HttpResponseMessage> PostSignedAsync(string json, string? secret = WhatsAppTestOptions.AppSecret)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        if (secret is not null)
        {
            request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.Compute(body, secret));
        }

        return await _meta.SendAsync(request);
    }

    /// <summary>Posts and waits until the background worker has dealt with every stored item.</summary>
    private async Task DeliverAsync(string json)
    {
        var response = await PostSignedAsync(json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await Db(db => db.WebhookEvents.AnyAsync(e => e.ProcessedAt == null && e.Error == null)))
        {
            Assert.True(DateTime.UtcNow < deadline, "Worker did not finish in time.");
            await Task.Delay(20);
        }
    }

    private Task<Guest> LoadAsync(int id) => Db(db => db.Guests.Include(g => g.Conversation).Include(g => g.Rsvp!).ThenInclude(r => r.Persons).SingleAsync(g => g.Id == id));

    private Task<List<Message>> MessagesAsync(int id) => Db(db => db.Messages.Where(m => m.GuestId == id).OrderBy(m => m.Id).ToListAsync());

    [Fact]
    public async Task Webhook_verification_returns_the_challenge_only_for_the_right_token()
    {
        var ok = await _meta.GetAsync($"/webhooks/whatsapp?hub.mode=subscribe&hub.challenge=1158201444&hub.verify_token={WhatsAppTestOptions.VerifyToken}");
        var wrongToken = await _meta.GetAsync("/webhooks/whatsapp?hub.mode=subscribe&hub.challenge=1&hub.verify_token=nope");
        var wrongMode = await _meta.GetAsync($"/webhooks/whatsapp?hub.mode=unsubscribe&hub.challenge=1&hub.verify_token={WhatsAppTestOptions.VerifyToken}");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("1158201444", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongMode.StatusCode);
    }

    [Fact]
    public async Task Unsigned_or_wrongly_signed_posts_are_rejected_and_not_stored()
    {
        var json = TextWebhook("27059999999", "ZA.X", "wamid.UNSIGNED.1", "yes");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostSignedAsync(json, secret: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostSignedAsync(json, secret: "someone-else")).StatusCode);
        Assert.False(await Db(db => db.WebhookEvents.AnyAsync(e => e.ExternalId == "wamid.UNSIGNED.1")));
    }

    [Fact]
    public async Task Full_RSVP_over_WhatsApp()
    {
        var (id, digits, userId) = await GuestAsync();
        await InviteAsync(id);

        var invitation = factory.Graph.BodiesTo("+" + digits).Single();
        Assert.Equal("template", invitation["type"]!.GetValue<string>());

        // The guest taps the template's "Yes" quick reply.
        await DeliverAsync(Fixtures.Read("template_quick_reply.json").Replace("27000000001", digits).Replace("ZA.000000000000000001", userId).Replace("wamid.FIXTURE.QUICKREPLY", $"wamid.FULL.{id}.1"));
        var i = 2;
        foreach (var answer in new[] { "2", "vegan" })
        {
            await DeliverAsync(TextWebhook(digits, userId, $"wamid.FULL.{id}.{i++}", answer));
        }

        // Guest 2's dietary requirement from the list, then the rest typed.
        await DeliverAsync(Fixtures.Read("list_reply.json").Replace("27000000001", digits).Replace("ZA.000000000000000001", userId).Replace("wamid.FIXTURE.LIST", $"wamid.FULL.{id}.{i++}"));
        foreach (var answer in new[] { "yes", "1", "Ramp please" })
        {
            await DeliverAsync(TextWebhook(digits, userId, $"wamid.FULL.{id}.{i++}", answer));
        }

        var guest = await LoadAsync(id);
        Assert.Equal(GuestStatus.Attending, guest.Status);
        Assert.Equal(2, guest.Rsvp!.PartySize);
        Assert.Equal([Engine.Dietary.Vegan, Engine.Dietary.Vegetarian], guest.Rsvp.Persons.OrderBy(p => p.PersonIndex).Select(p => p.Dietary));
        Assert.Equal(1, guest.Rsvp.WheelchairUsers);
        Assert.Equal("Ramp please", guest.Rsvp.AccessNote);
        Assert.Equal(userId, guest.WhatsAppUserId);

        var sent = factory.Graph.BodiesTo("+" + digits);
        Assert.Equal(8, sent.Count); // invitation + one reply per answer
        Assert.Equal("list", sent[1]["interactive"]!["type"]!.GetValue<string>()); // party size 1–4
        Assert.Equal("list", sent[2]["interactive"]!["type"]!.GetValue<string>()); // dietary options
        Assert.Equal("button", sent[^1]["interactive"]!["type"]!.GetValue<string>()); // summary with a Change button
        Assert.Contains("Party size: 2", sent[^1]["interactive"]!["body"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_same_webhook_delivered_twice_creates_one_record_and_one_answer()
    {
        var (id, digits, userId) = await GuestAsync();
        await InviteAsync(id);
        var json = TextWebhook(digits, userId, $"wamid.DUP.{id}", "yes");

        await DeliverAsync(json);
        await DeliverAsync(json);

        Assert.Equal(1, await Db(db => db.WebhookEvents.CountAsync(e => e.ExternalId == $"wamid.DUP.{id}")));
        var messages = await MessagesAsync(id);
        Assert.Single(messages, m => m.Direction == MessageDirection.In);
        Assert.Equal(Engine.Step.Party, (await LoadAsync(id)).Conversation!.State);
    }

    [Fact]
    public async Task Delivery_statuses_only_move_forward()
    {
        var (id, _, _) = await GuestAsync();
        await InviteAsync(id);
        var invitation = (await MessagesAsync(id)).Single();
        Assert.Equal(DeliveryStatus.Sent, invitation.DeliveryStatus);
        var providerId = invitation.ProviderMessageId!;

        await DeliverAsync(StatusWebhook("status_read.json", providerId));
        await DeliverAsync(StatusWebhook("status_delivered.json", providerId)); // late, out of order
        await DeliverAsync(StatusWebhook("status_failed.json", providerId));    // after read: ignored
        await DeliverAsync(StatusWebhook("status_read.json", providerId));      // repeat: ignored as duplicate

        var message = (await MessagesAsync(id)).Single();
        Assert.Equal(DeliveryStatus.Read, message.DeliveryStatus);
        Assert.Null(message.DeliveryError);
        Assert.Equal(1, await Db(db => db.WebhookEvents.CountAsync(e => e.ExternalId == $"status:{providerId}:read")));
    }

    [Fact]
    public async Task A_failed_delivery_is_recorded_with_its_reason()
    {
        var (id, _, _) = await GuestAsync();
        await InviteAsync(id);
        var providerId = (await MessagesAsync(id)).Single().ProviderMessageId!;

        await DeliverAsync(StatusWebhook("status_failed.json", providerId));

        var message = (await MessagesAsync(id)).Single();
        Assert.Equal(DeliveryStatus.Failed, message.DeliveryStatus);
        Assert.Equal("131026: Message undeliverable", message.DeliveryError);
    }

    [Fact]
    public async Task A_guest_without_a_phone_number_in_the_webhook_is_found_by_user_id()
    {
        var (id, digits, userId) = await GuestAsync();
        await InviteAsync(id);
        await DeliverAsync(TextWebhook(digits, userId, $"wamid.BSUID.{id}.1", "yes"));

        await DeliverAsync(TextWebhook(digits, userId, $"wamid.BSUID.{id}.2", "3", withPhone: false));

        Assert.Equal(3, (await LoadAsync(id)).Rsvp!.PartySize);
    }

    [Fact]
    public async Task Messages_from_unknown_senders_are_recorded_as_errors()
    {
        await DeliverAsync(TextWebhook("27058888888", "ZA.NOBODY", "wamid.UNKNOWN.1", "hello"));

        var stored = await Db(db => db.WebhookEvents.SingleAsync(e => e.ExternalId == "wamid.UNKNOWN.1"));
        Assert.Null(stored.ProcessedAt);
        Assert.Contains("unknown guest", stored.Error);
    }

    [Fact]
    public async Task Failed_sends_are_marked_failed_with_the_provider_error()
    {
        var (id, _, _) = await GuestAsync();
        factory.Graph.Enqueue(HttpStatusCode.BadRequest, FakeGraphApi.Error(132001, "Template name does not exist in the translation"));

        await InviteAsync(id);

        var message = (await MessagesAsync(id)).Single();
        Assert.Equal(DeliveryStatus.Failed, message.DeliveryStatus);
        Assert.StartsWith("132001:", message.DeliveryError);
    }

    [Fact]
    public async Task Simulator_is_not_available_in_live_mode()
    {
        // Not mapped at all; the login fallback policy answers 401 for unknown /api routes.
        Assert.NotEqual(HttpStatusCode.OK, (await _meta.GetAsync("/api/simulator/guests")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await _meta.PostAsync("/api/simulator/1/invite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _meta.GetAsync("/Simulator")).StatusCode);
    }
}
