using System.Text.Json;
using System.Text.Json.Serialization;
using RsvpDesk.Engine;

namespace RsvpDesk.Web.Conversations;

/// <summary>JSON for <c>Conversations.StateData</c>. Enums as names so the column is readable.</summary>
public static class StateSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialise(ConversationState state) => JsonSerializer.Serialize(state, Options);

    public static ConversationState Deserialise(string? json) =>
        string.IsNullOrWhiteSpace(json) || json == "{}"
            ? ConversationState.New
            : JsonSerializer.Deserialize<ConversationState>(json, Options) ?? ConversationState.New;
}
