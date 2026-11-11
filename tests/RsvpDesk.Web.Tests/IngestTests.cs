using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

/// <summary>Spec section 13 "Ingest": idempotency and error recording.</summary>
public sealed class IngestTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    [Fact]
    public async Task The_same_message_posted_twice_creates_one_record_and_advances_once()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);

        var first = await SayAsync(id, "yes", messageId: "dup-test-1");
        var second = await SayAsync(id, "yes", messageId: "dup-test-1");

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        Assert.Equal(first.EventId, second.EventId);

        var (events, inbound) = await WithDbAsync(async db => (
            await db.WebhookEvents.CountAsync(e => e.ExternalId == "dup-test-1"),
            await db.Messages.CountAsync(m => m.ProviderMessageId == "dup-test-1")));
        Assert.Equal(1, events);
        Assert.Equal(1, inbound);

        var guest = await GetGuestAsync(id);
        Assert.Equal("Party", guest.Step);
        Assert.Equal(1, guest.Messages.Count(m => m.Direction == "In"));
    }

    [Fact]
    public async Task Database_rejects_a_second_message_with_the_same_provider_id()
    {
        var id = await CreateGuestAsync();

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.Messages.Add(new Message { GuestId = id, Direction = MessageDirection.In, Body = "a", ProviderMessageId = "unique-test", SentAt = DateTimeOffset.UtcNow });
            db.Messages.Add(new Message { GuestId = id, Direction = MessageDirection.In, Body = "b", ProviderMessageId = "unique-test", SentAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        }));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Database_rejects_a_second_webhook_event_with_the_same_external_id()
    {
        await Assert.ThrowsAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.WebhookEvents.Add(new WebhookEvent { Source = "simulator", ExternalId = "evt-unique", RawJson = "{}", ReceivedAt = DateTimeOffset.UtcNow });
            db.WebhookEvents.Add(new WebhookEvent { Source = "simulator", ExternalId = "evt-unique", RawJson = "{}", ReceivedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        }));
    }

    [Theory]
    [InlineData("simulator", "this is not json")]
    [InlineData("simulator", "{\"guestId\":999999,\"messageId\":\"x-1\",\"text\":\"hi\",\"sentAt\":\"2026-11-11T10:00:00Z\"}")]
    [InlineData("carrier-pigeon", "{}")]
    public async Task Processing_errors_are_recorded_against_the_event(string source, string raw)
    {
        var eventId = await WithDbAsync(async db =>
        {
            var e = new WebhookEvent { Source = source, ExternalId = $"bad-{Guid.NewGuid():N}", RawJson = raw, ReceivedAt = DateTimeOffset.UtcNow };
            db.WebhookEvents.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ConversationService>().ProcessInboundEventAsync(eventId, CancellationToken.None);
        }

        var stored = await WithDbAsync(db => db.WebhookEvents.SingleAsync(e => e.Id == eventId));
        Assert.Null(stored.ProcessedAt);
        Assert.False(string.IsNullOrEmpty(stored.Error));
    }

    [Fact]
    public async Task Processing_an_already_processed_event_does_nothing()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        var sent = await SayAsync(id, "yes");

        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ConversationService>().ProcessInboundEventAsync(sent.EventId, CancellationToken.None);
        }

        var guest = await GetGuestAsync(id);
        Assert.Equal("Party", guest.Step);
        Assert.Equal(1, guest.Messages.Count(m => m.Direction == "In"));
    }
}
