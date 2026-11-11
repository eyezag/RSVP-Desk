using RsvpDesk.Engine;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Channels;

public enum ChannelMode
{
    Simulator,
    WhatsApp,
}

public sealed class ChannelOptions
{
    public const string SectionName = "Channel";

    public ChannelMode Mode { get; set; } = ChannelMode.Simulator;
}

/// <summary>
/// Who an inbound message is from. Simulator messages name the guest; WhatsApp ones carry a phone
/// number and/or a business-scoped user id (BSUID), see docs/whatsapp-integration.md §3.
/// </summary>
public sealed record GuestAddress(int? GuestId, string? PhoneE164, string? ProviderUserId = null);

/// <summary>Something a channel reports: a message from a guest, or a delivery update for one of ours.</summary>
public abstract record ChannelEvent;

/// <summary>An inbound message in channel-neutral form, ready for the engine.</summary>
public sealed record NormalisedInbound(GuestAddress From, string MessageId, string? Text, string? ReplyId, DateTimeOffset SentAt) : ChannelEvent
{
    public Inbound ToEngineInbound() => new(MessageId, Text, ReplyId);
}

/// <summary>A delivery status for an outbound message, identified by the provider's message id.</summary>
public sealed record DeliveryUpdate(string ProviderMessageId, DeliveryStatus Status, DateTimeOffset At, string? Error = null) : ChannelEvent;

/// <summary>An event the app deliberately ignores (e.g. a reaction).</summary>
public sealed record IgnoredEvent(string Reason) : ChannelEvent;

public sealed record SendResult(bool Success, string? ProviderMessageId, string? Error = null);

/// <summary>What a channel may need besides the text: e.g. WhatsApp picks a template outside the 24-hour window.</summary>
public sealed record OutboundContext(EventDetails Event, Step Step, DateTimeOffset? LastInboundAt);

/// <summary>A rendered message addressed to a guest.</summary>
public sealed record OutboundEnvelope(int GuestId, string PhoneE164, string? ProviderUserId, RenderedMessage Message, OutboundContext Context);

/// <summary>
/// The boundary between the app and a messaging service. A channel turns stored raw inbound
/// events into <see cref="ChannelEvent"/>s and delivers outbound messages.
/// </summary>
public interface IChannel
{
    /// <summary>Matches <c>WebhookEvent.Source</c>.</summary>
    string Source { get; }

    IReadOnlyList<ChannelEvent> Normalise(string rawJson);

    Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken);
}
