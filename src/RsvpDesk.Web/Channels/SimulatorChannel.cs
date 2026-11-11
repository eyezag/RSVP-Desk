using System.Text.Json;

namespace RsvpDesk.Web.Channels;

/// <summary>Raw event the simulator stores for each message typed or tapped in the browser.</summary>
public sealed record SimulatorInboundEvent(int GuestId, string MessageId, string? Text, string? ReplyId, DateTimeOffset SentAt);

/// <summary>
/// The browser is the "phone". Outbound messages are already in the Messages table, which the
/// simulator panel reads, so sending only assigns an id.
/// </summary>
public sealed class SimulatorChannel : IChannel
{
    public const string SourceName = "simulator";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Source => SourceName;

    public IReadOnlyList<ChannelEvent> Normalise(string rawJson)
    {
        var e = JsonSerializer.Deserialize<SimulatorInboundEvent>(rawJson, Json)
            ?? throw new FormatException("Empty simulator event.");
        return [new NormalisedInbound(new GuestAddress(e.GuestId, null), e.MessageId, e.Text, e.ReplyId, e.SentAt)];
    }

    public Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken) =>
        Task.FromResult(new SendResult(true, $"sim-out-{Guid.NewGuid():N}"));
}

public sealed class ChannelRegistry(IEnumerable<IChannel> channels, Microsoft.Extensions.Options.IOptions<ChannelOptions> options)
{
    private readonly Dictionary<string, IChannel> _bySource = channels.ToDictionary(c => c.Source);

    public ChannelMode Mode => options.Value.Mode;

    /// <summary>The channel outbound messages go through, chosen by <c>Channel:Mode</c>.</summary>
    public IChannel Active => Mode switch
    {
        ChannelMode.Simulator => _bySource[SimulatorChannel.SourceName],
        ChannelMode.WhatsApp => _bySource[WhatsApp.WhatsAppCloudChannel.SourceName],
        _ => throw new NotSupportedException($"Unknown channel mode {Mode}."),
    };

    public IChannel ForSource(string source) =>
        _bySource.TryGetValue(source, out var channel) ? channel : throw new InvalidOperationException($"No channel for source '{source}'.");
}
