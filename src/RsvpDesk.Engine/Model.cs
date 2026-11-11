using System.Collections.Immutable;

namespace RsvpDesk.Engine;

/// <summary>Where a guest is in the RSVP conversation (spec section 6.1).</summary>
public enum Step
{
    /// <summary>No invitation sent yet.</summary>
    NotInvited,
    Invited,
    Party,
    Diet,
    DietOther,
    Wheelchair,
    WheelchairCount,
    Note,
    Confirmed,
    Declined,
    OptedOut,
}

public enum Dietary
{
    None,
    Vegetarian,
    Vegan,
    Halal,
    GlutenFree,
    Other,
}

/// <summary>The event details the engine needs to word its messages and validate input.</summary>
public sealed record EventDetails(string Name, DateTimeOffset StartsAt, string Venue, int PartyMax)
{
    public EventDetails Validate()
    {
        if (PartyMax < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PartyMax), PartyMax, "PartyMax must be at least 1.");
        }

        return this;
    }
}

/// <summary>
/// A message from the guest, already normalised by a channel. <see cref="ReplyId"/> is set when the
/// guest tapped a button or list row; <see cref="Text"/> holds typed text (or the tapped title).
/// </summary>
public sealed record Inbound(string MessageId, string? Text, string? ReplyId = null);

public sealed record DietAnswer(int PersonIndex, Dietary Dietary, string? Note = null);

/// <summary>Everything the guest has answered so far. Partial answers are normal.</summary>
public sealed record RsvpAnswers
{
    public static readonly RsvpAnswers Empty = new();

    public bool? Attending { get; init; }
    public int? PartySize { get; init; }
    public ImmutableList<DietAnswer> Diets { get; init; } = [];
    public int? WheelchairUsers { get; init; }
    public string? AccessNote { get; init; }

    internal RsvpAnswers WithDiet(DietAnswer answer) =>
        this with { Diets = Diets.RemoveAll(d => d.PersonIndex == answer.PersonIndex).Add(answer).Sort((a, b) => a.PersonIndex.CompareTo(b.PersonIndex)) };
}

/// <summary>
/// The full conversation state. Plain data, safe to serialise as JSON (the Conversations.StateData column).
/// </summary>
public sealed record ConversationState
{
    /// <summary>How many recent inbound message ids are remembered for duplicate detection.</summary>
    public const int RecentMessageIdLimit = 50;

    public static readonly ConversationState New = new();

    public Step Step { get; init; } = Step.NotInvited;

    /// <summary>1-based index of the person whose dietary requirement is being asked (Diet and DietOther).</summary>
    public int PersonIndex { get; init; } = 1;

    /// <summary>Invalid answers in a row for the current question.</summary>
    public int InvalidStreak { get; init; }

    public RsvpAnswers Answers { get; init; } = RsvpAnswers.Empty;

    public ImmutableList<string> RecentMessageIds { get; init; } = [];
}

/// <summary>Why a message is sent. A channel may need to treat these differently (e.g. WhatsApp templates).</summary>
public enum MessagePurpose
{
    /// <summary>Business-initiated invitation.</summary>
    Invitation,

    /// <summary>A reply inside the conversation.</summary>
    Reply,
}

/// <summary>A piece of message text, identified by a key in the message catalog plus its arguments.</summary>
public sealed record MessagePart(string Key, IReadOnlyDictionary<string, object> Args)
{
    private static readonly IReadOnlyDictionary<string, object> NoArgs = ImmutableDictionary<string, object>.Empty;

    public MessagePart(string key) : this(key, NoArgs)
    {
    }
}

/// <summary>A tappable option. <see cref="Number"/> is set for numeric options such as party size.</summary>
public sealed record Choice(string Id, int? Number = null);

/// <summary>
/// One outbound message. The engine never produces text: the parts are message keys that a
/// renderer turns into text, and the channel decides whether choices become buttons or a list.
/// </summary>
public sealed record OutboundMessage(MessagePurpose Purpose, IReadOnlyList<MessagePart> Parts, IReadOnlyList<Choice> Choices);

/// <summary>Data captured from the guest, emitted as it happens so partial answers can be stored.</summary>
public abstract record Capture;

public sealed record AttendanceCaptured(bool Attending) : Capture;

public sealed record PartySizeCaptured(int PartySize) : Capture;

public sealed record DietaryCaptured(int PersonIndex, Dietary Dietary, string? Note) : Capture;

public sealed record WheelchairUsersCaptured(int Count) : Capture;

public sealed record AccessNoteCaptured(string? Note) : Capture;

/// <summary>The guest finished: confirmed attendance or declined.</summary>
public sealed record RsvpCompleted(bool Attending) : Capture;

/// <summary>The guest replied CHANGE; previous answers are discarded.</summary>
public sealed record AnswersCleared : Capture;

/// <summary>The guest replied STOP.</summary>
public sealed record OptedOut : Capture;

/// <summary>The guest replied HELP and should be shown as "Needs attention".</summary>
public sealed record NeedsAttention : Capture;

public sealed record EngineResult(
    ConversationState State,
    IReadOnlyList<OutboundMessage> Messages,
    IReadOnlyList<Capture> Captures,
    bool Duplicate = false);
