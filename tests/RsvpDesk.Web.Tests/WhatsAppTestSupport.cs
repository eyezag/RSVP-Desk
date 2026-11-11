using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RsvpDesk.Engine;
using RsvpDesk.Web.WhatsApp;

namespace RsvpDesk.Web.Tests;

public static class Fixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "whatsapp", name));
}

/// <summary>Stands in for graph.facebook.com: records requests and plays scripted responses.</summary>
public sealed class FakeGraphApi : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpResponseMessage>> _scripted = new();
    private int _counter;

    public ConcurrentQueue<(string Url, string? Auth, JsonObject Body)> Requests { get; } = new();

    public void Enqueue(HttpStatusCode status, string body, TimeSpan? retryAfter = null) => _scripted.Enqueue(() =>
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null)
        {
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
        }

        return r;
    });

    public static string Error(int code, string message) => $$$"""{"error":{"message":"(#{{{code}}}) {{{message}}}","type":"OAuthException","code":{{{code}}},"error_data":{"messaging_product":"whatsapp","details":"{{{message}}}"},"fbtrace_id":"Afake"}}""";

    public List<JsonObject> BodiesTo(string phone) => Requests.Where(r => r.Body["to"]?.GetValue<string>() == phone).Select(r => r.Body).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        Requests.Enqueue((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
        if (_scripted.TryDequeue(out var scripted))
        {
            return scripted();
        }

        var id = Interlocked.Increment(ref _counter);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"messaging_product":"whatsapp","contacts":[{"input":"x","wa_id":"x"}],"messages":[{"id":"wamid.TEST.OUT.{{id}}"}]}""", Encoding.UTF8, "application/json"),
        };
    }
}

public static class WhatsAppTestOptions
{
    public const string AppSecret = "test-app-secret";
    public const string VerifyToken = "test-verify-token";

    public static WhatsAppOptions Create() => new()
    {
        PhoneNumberId = "100000000000001",
        AccessToken = "test-access-token",
        AppSecret = AppSecret,
        VerifyToken = VerifyToken,
        RetryBaseDelayMs = 1,
    };

    public static WhatsAppCloudChannel Channel(FakeGraphApi graph, WhatsAppOptions? options = null) =>
        new(new HttpClient(graph), Options.Create(options ?? Create()), new MessageRenderer(), TimeProvider.System, NullLogger<WhatsAppCloudChannel>.Instance);
}
