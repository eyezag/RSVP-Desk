using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RsvpDesk.Engine;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.WhatsApp;

/// <summary>Live mode: Meta WhatsApp Cloud API. See docs/whatsapp-integration.md for every rule used here.</summary>
public sealed class WhatsAppCloudChannel(
    HttpClient http,
    IOptions<WhatsAppOptions> options,
    MessageRenderer renderer,
    TimeProvider clock,
    ILogger<WhatsAppCloudChannel> logger) : IChannel
{
    public const string SourceName = "whatsapp";

    /// <summary>Error codes worth retrying (rate limits, temporary failures), docs §10.</summary>
    private static readonly HashSet<int> RetryableCodes = [4, 80007, 130429, 131000, 131016, 131056];

    public string Source => SourceName;

    public IReadOnlyList<ChannelEvent> Normalise(string rawJson)
    {
        var item = JsonNode.Parse(rawJson) as JsonObject ?? throw new FormatException("Empty WhatsApp event.");
        return item["kind"]?.GetValue<string>() switch
        {
            WebhookSplitter.KindMessage => [NormaliseMessage(item)],
            WebhookSplitter.KindStatus => [NormaliseStatus(item["status"] as JsonObject ?? throw new FormatException("Status missing."))],
            _ => throw new FormatException("Unreadable webhook payload."),
        };
    }

    private ChannelEvent NormaliseMessage(JsonObject item)
    {
        var message = item["message"] as JsonObject ?? throw new FormatException("Message missing.");
        var contact = item["contact"] as JsonObject;
        var id = message["id"]!.GetValue<string>();
        var type = message["type"]?.GetValue<string>();

        if (type is "reaction" or "system")
        {
            return new IgnoredEvent($"WhatsApp {type} message");
        }

        var from = message["from"]?.GetValue<string>() ?? contact?["wa_id"]?.GetValue<string>();
        var phone = string.IsNullOrEmpty(from) ? null : "+" + new string(from.Where(char.IsAsciiDigit).ToArray());
        var userId = message["from_user_id"]?.GetValue<string>() ?? contact?["user_id"]?.GetValue<string>();
        var sentAt = long.TryParse(message["timestamp"]?.GetValue<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : clock.GetUtcNow();

        string? text = null, replyId = null;
        switch (type)
        {
            case "text":
                text = message["text"]?["body"]?.GetValue<string>();
                break;
            case "interactive":
                var interactive = message["interactive"];
                var reply = interactive?["button_reply"] ?? interactive?["list_reply"];
                replyId = reply?["id"]?.GetValue<string>();
                text = reply?["title"]?.GetValue<string>();
                break;
            case "button":
                // Template quick reply: map the label to an option id (docs/templates.md).
                text = message["button"]?["text"]?.GetValue<string>();
                var key = message["button"]?["payload"]?.GetValue<string>() ?? text;
                replyId = key is not null && options.Value.QuickReplies.TryGetValue(key, out var mapped) ? mapped : null;
                break;
            default:
                // Images, audio, stickers, etc.: the engine sees "no answer" and repeats its question.
                break;
        }

        return new NormalisedInbound(new GuestAddress(null, phone, userId), id, text, replyId, sentAt);
    }

    private ChannelEvent NormaliseStatus(JsonObject status)
    {
        var id = status["id"]!.GetValue<string>();
        var at = long.TryParse(status["timestamp"]?.GetValue<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : clock.GetUtcNow();
        var error = status["errors"] is JsonArray { Count: > 0 } errors && errors[0] is JsonObject e
            ? $"{e["code"]}: {e["title"]?.GetValue<string>() ?? e["message"]?.GetValue<string>()}"
            : null;

        return status["status"]?.GetValue<string>() switch
        {
            "sent" => new DeliveryUpdate(id, DeliveryStatus.Sent, at),
            "delivered" => new DeliveryUpdate(id, DeliveryStatus.Delivered, at),
            "read" or "played" => new DeliveryUpdate(id, DeliveryStatus.Read, at),
            "failed" => new DeliveryUpdate(id, DeliveryStatus.Failed, at, error),
            var other => new IgnoredEvent($"Unknown status '{other}'"),
        };
    }

    public async Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var payload = WhatsAppPayloads.Build(message, o, renderer, clock.GetUtcNow()).ToJsonString();
        var url = $"{o.GraphApiBaseUrl.TrimEnd('/')}/{o.ApiVersion}/{o.PhoneNumberId}/messages";

        for (var attempt = 1; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            string error;
            bool retryable;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
                using var response = await http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var id = JsonNode.Parse(body)?["messages"]?[0]?["id"]?.GetValue<string>();
                    return id is null
                        ? new SendResult(false, null, "Response had no message id.")
                        : new SendResult(true, id);
                }

                (error, var code) = ParseError(body, response.StatusCode);
                retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500 || (code is { } c && RetryableCodes.Contains(c));
                retryAfter = response.Headers.RetryAfter?.Delta;
            }
            catch (HttpRequestException ex)
            {
                error = "Network error: " + ex.Message;
                retryable = true;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                error = "Request timed out.";
                retryable = true;
            }

            if (!retryable || attempt >= o.MaxSendAttempts)
            {
                // Guest id only: never phone numbers or message text in logs.
                logger.LogWarning("WhatsApp send to guest {GuestId} failed after {Attempts} attempt(s): {Error}", message.GuestId, attempt, error);
                return new SendResult(false, null, error);
            }

            var delay = retryAfter ?? TimeSpan.FromMilliseconds(o.RetryBaseDelayMs * Math.Pow(2, attempt - 1));
            logger.LogInformation("WhatsApp send to guest {GuestId} will retry in {Delay} ({Error})", message.GuestId, delay, error);
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static (string Error, int? Code) ParseError(string body, HttpStatusCode status)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            var code = error?["code"]?.GetValue<int>();
            var details = error?["error_data"]?["details"]?.GetValue<string>();
            var text = error?["message"]?.GetValue<string>() ?? status.ToString();
            return ($"{code?.ToString(CultureInfo.InvariantCulture) ?? ((int)status).ToString(CultureInfo.InvariantCulture)}: {text}{(details is null ? "" : $" ({details})")}", code);
        }
        catch (JsonException)
        {
            return ($"HTTP {(int)status}", null);
        }
    }
}
