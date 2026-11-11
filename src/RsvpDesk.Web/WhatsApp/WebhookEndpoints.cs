using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.WhatsApp;

/// <summary>GET/POST /webhooks/whatsapp (live mode only). See docs/whatsapp-integration.md §1–2.</summary>
public static class WebhookEndpoints
{
    /// <summary>Meta's maximum payload size.</summary>
    public const int MaxBodyBytes = 3 * 1024 * 1024;

    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder app)
    {
        app.MapGet("/webhooks/whatsapp", Verify).AllowAnonymous().WithTags("WhatsApp");
        app.MapPost("/webhooks/whatsapp", ReceiveAsync).AllowAnonymous().WithTags("WhatsApp");
        return app;
    }

    private static IResult Verify(HttpRequest request, IOptions<WhatsAppOptions> options)
    {
        var mode = request.Query["hub.mode"].ToString();
        var token = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();
        return mode == "subscribe" && challenge.Length > 0 && WebhookSignature.TokensMatch(token, options.Value.VerifyToken!)
            ? Results.Text(challenge, "text/plain")
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        IOptions<WhatsAppOptions> options,
        RsvpDbContext db,
        InboundQueue queue,
        TimeProvider clock,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = buffer.ToArray();
        if (!WebhookSignature.IsValid(body, request.Headers[WebhookSignature.HeaderName], options.Value.AppSecret!))
        {
            loggers.CreateLogger("WhatsAppWebhook").LogWarning("Rejected a webhook with a missing or invalid signature");
            return Results.Unauthorized();
        }

        var items = WebhookSplitter.Split(System.Text.Encoding.UTF8.GetString(body));
        var now = clock.GetUtcNow();
        var queued = new List<long>();
        foreach (var item in items)
        {
            if (await db.WebhookEvents.AnyAsync(e => e.ExternalId == item.ExternalId, ct))
            {
                continue; // Meta retries and can deliver duplicates.
            }

            var stored = new WebhookEvent { Source = WhatsAppCloudChannel.SourceName, ExternalId = item.ExternalId, ReceivedAt = now, RawJson = item.Json };
            db.WebhookEvents.Add(stored);
            try
            {
                await db.SaveChangesAsync(ct);
                queued.Add(stored.Id);
            }
            catch (DbUpdateException)
            {
                // A concurrent delivery of the same item won the unique index.
                db.Entry(stored).State = EntityState.Detached;
            }
        }

        foreach (var id in queued)
        {
            queue.Enqueue(id);
        }

        return Results.Ok();
    }
}
