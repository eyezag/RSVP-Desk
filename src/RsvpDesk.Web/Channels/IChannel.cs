using RsvpDesk.Engine;

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

/// <summary>Who an inbound message is from. Simulator messages name the guest; WhatsApp ones carry a phone number.</summary>
public sealed record GuestAddress(int? GuestId, string? PhoneE164);

/// <summary>An inbound message in channel-neutral form, ready for the engine.</summary>
public sealed record NormalisedInbound(GuestAddress From, string MessageId, string? Text, string? ReplyId, DateTimeOffset SentAt)
{
    public Inbound ToEngineInbound() => new(MessageId, Text, ReplyId);
}

public sealed record SendResult(bool Success, string? ProviderMessageId, string? Error = null);

/// <summary>A rendered message addressed to a guest.</summary>
public sealed record OutboundEnvelope(int GuestId, string PhoneE164, RenderedMessage Message);

/// <summary>
/// The boundary between the app and a messaging service. A channel turns raw inbound
/// events into <see cref="NormalisedInbound"/> and delivers outbound messages.
/// </summary>
public interface IChannel
{
    /// <summary>Matches <c>WebhookEvent.Source</c>.</summary>
    string Source { get; }

    IReadOnlyList<NormalisedInbound> Normalise(string rawJson);

    Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken);
}
