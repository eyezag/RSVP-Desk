using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RsvpDesk.Web.WhatsApp;

/// <summary>One message or status from a webhook, stored as its own WebhookEvent.</summary>
public sealed record WebhookItem(string ExternalId, string Json);

/// <summary>
/// Splits a (verified) webhook payload into one item per message and per status, so each gets a
/// unique id and repeated deliveries are recognised individually (docs/whatsapp-integration.md §2–4).
/// </summary>
public static class WebhookSplitter
{
    public const string KindMessage = "message";
    public const string KindStatus = "status";

    public static IReadOnlyList<WebhookItem> Split(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // Signed but unreadable: keep it so the error is visible against a stored event.
            return [new WebhookItem("unparsed:" + Hash(body), JsonSerializer.Serialize(new { kind = "unparsed", body }))];
        }

        if (root?["object"]?.GetValue<string>() != "whatsapp_business_account" || root["entry"] is not JsonArray entries)
        {
            return [];
        }

        var items = new List<WebhookItem>();
        foreach (var change in entries.OfType<JsonObject>().SelectMany(e => e["changes"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (change["field"]?.GetValue<string>() != "messages" || change["value"] is not JsonObject value)
            {
                continue;
            }

            var contacts = (value["contacts"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            foreach (var message in (value["messages"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var id = message["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var contact = MatchContact(contacts, message);
                var item = new JsonObject
                {
                    ["kind"] = KindMessage,
                    ["contact"] = contact?.DeepClone(),
                    ["message"] = message.DeepClone(),
                };
                items.Add(new WebhookItem(id, item.ToJsonString()));
            }

            foreach (var status in (value["statuses"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var id = status["id"]?.GetValue<string>();
                var state = status["status"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(state))
                {
                    continue;
                }

                var item = new JsonObject { ["kind"] = KindStatus, ["status"] = status.DeepClone() };
                items.Add(new WebhookItem($"status:{id}:{state}", item.ToJsonString()));
            }
        }

        return items;
    }

    private static JsonObject? MatchContact(List<JsonObject> contacts, JsonObject message)
    {
        if (contacts.Count <= 1)
        {
            return contacts.FirstOrDefault();
        }

        var from = message["from"]?.GetValue<string>();
        var fromUser = message["from_user_id"]?.GetValue<string>();
        return contacts.FirstOrDefault(c =>
            (from is not null && c["wa_id"]?.GetValue<string>() == from) ||
            (fromUser is not null && c["user_id"]?.GetValue<string>() == fromUser));
    }

    private static string Hash(string body) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..32];
}
