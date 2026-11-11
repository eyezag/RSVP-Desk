using System.Text.Json.Nodes;
using RsvpDesk.Engine;
using RsvpDesk.Web.Channels;

namespace RsvpDesk.Web.WhatsApp;

/// <summary>
/// Builds Cloud API message bodies (docs/whatsapp-integration.md §5–7). Pure, so every shape is unit tested.
/// </summary>
public static class WhatsAppPayloads
{
    public const int MaxButtons = 3;
    public const int MaxButtonTitle = 20;
    public const int MaxButtonBody = 1024;
    public const int MaxListRows = 10;
    public const int MaxListRowTitle = 24;
    public const int MaxListBody = 4096;
    public const int MaxListButton = 20;
    public const int MaxSectionTitle = 24;
    public const int MaxTextBody = 4096;

    public static JsonObject Build(OutboundEnvelope envelope, WhatsAppOptions options, MessageRenderer renderer, DateTimeOffset now)
    {
        var message = envelope.Message;
        var payload = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = envelope.PhoneE164,
        };

        var windowOpen = envelope.Context.LastInboundAt is { } last && now - last < options.CustomerServiceWindow;
        var needsTemplate = message.Purpose == MessagePurpose.Invitation
            || (message.Purpose == MessagePurpose.Reminder && !windowOpen);

        if (needsTemplate)
        {
            AddTemplate(payload, envelope, options, renderer);
        }
        else if (message.Choices.Count == 0 || message.Choices.Count > MaxListRows)
        {
            AddText(payload, message);
        }
        else if (message.Choices.Count <= MaxButtons)
        {
            AddButtons(payload, message);
        }
        else
        {
            AddList(payload, message, renderer);
        }

        return payload;
    }

    private static void AddTemplate(JsonObject payload, OutboundEnvelope envelope, WhatsAppOptions options, MessageRenderer renderer)
    {
        var ev = envelope.Context.Event;
        var (name, parameters) = envelope.Message.Purpose switch
        {
            MessagePurpose.Invitation => (options.Templates.Invitation, new[] { ("event_name", ev.Name), ("event_date", renderer.FormatDate(ev.StartsAt)), ("venue", ev.Venue) }),
            _ when envelope.Context.Step == Step.Invited => (options.Templates.ReminderNotReplied, new[] { ("event_name", ev.Name), ("event_date", renderer.FormatDate(ev.StartsAt)) }),
            _ => (options.Templates.ReminderUnfinished, new[] { ("event_name", ev.Name), ("event_date", renderer.FormatDate(ev.StartsAt)) }),
        };

        payload["type"] = "template";
        payload["template"] = new JsonObject
        {
            ["name"] = name,
            ["language"] = new JsonObject { ["code"] = options.TemplateLanguage },
            ["components"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "body",
                    ["parameters"] = new JsonArray(parameters.Select(p => (JsonNode)new JsonObject
                    {
                        ["type"] = "text",
                        ["parameter_name"] = p.Item1,
                        ["text"] = p.Item2,
                    }).ToArray()),
                },
            },
        };
    }

    private static void AddText(JsonObject payload, RenderedMessage message)
    {
        var body = message.Body;
        if (message.Choices.Count > 0)
        {
            // More options than a list can hold: fall back to a numbered list the guest can type from.
            body += "\n\n" + string.Join("\n", message.Choices.Select((c, i) => $"{i + 1}. {c.Title}"));
        }

        payload["type"] = "text";
        payload["text"] = new JsonObject { ["preview_url"] = false, ["body"] = Truncate(body, MaxTextBody) };
    }

    private static void AddButtons(JsonObject payload, RenderedMessage message)
    {
        payload["type"] = "interactive";
        payload["interactive"] = new JsonObject
        {
            ["type"] = "button",
            ["body"] = new JsonObject { ["text"] = Truncate(message.Body, MaxButtonBody) },
            ["action"] = new JsonObject
            {
                ["buttons"] = new JsonArray(message.Choices.Select(c => (JsonNode)new JsonObject
                {
                    ["type"] = "reply",
                    ["reply"] = new JsonObject { ["id"] = c.Id, ["title"] = Truncate(c.Title, MaxButtonTitle) },
                }).ToArray()),
            },
        };
    }

    private static void AddList(JsonObject payload, RenderedMessage message, MessageRenderer renderer)
    {
        payload["type"] = "interactive";
        payload["interactive"] = new JsonObject
        {
            ["type"] = "list",
            ["body"] = new JsonObject { ["text"] = Truncate(message.Body, MaxListBody) },
            ["action"] = new JsonObject
            {
                ["button"] = Truncate(renderer.ListButtonTitle, MaxListButton),
                ["sections"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["title"] = Truncate(renderer.ListSectionTitle, MaxSectionTitle),
                        ["rows"] = new JsonArray(message.Choices.Select(c => (JsonNode)new JsonObject
                        {
                            ["id"] = c.Id,
                            ["title"] = Truncate(c.Title, MaxListRowTitle),
                        }).ToArray()),
                    },
                },
            },
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
