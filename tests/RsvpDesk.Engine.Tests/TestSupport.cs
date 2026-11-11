using System.Collections.Immutable;

namespace RsvpDesk.Engine.Tests;

internal static class TestSupport
{
    public static readonly EventDetails Event = new(
        "Year-End Function 2026",
        new DateTimeOffset(2026, 12, 5, 18, 0, 0, TimeSpan.FromHours(2)),
        "The Jacaranda Hall, Johannesburg",
        PartyMax: 4);

    public static readonly ConversationEngine Bot = new();

    private static int _messageCounter;

    public static Inbound Text(string? text) => new(NextId(), text);

    public static Inbound Tap(string replyId, string? title = null) => new(NextId(), title, replyId);

    public static string NextId() => $"wamid.test.{Interlocked.Increment(ref _messageCounter)}";

    /// <summary>A state at the given step with plausible answers so far.</summary>
    public static ConversationState At(Step step, int partySize = 3, int personIndex = 1)
    {
        var answers = RsvpAnswers.Empty;
        if (step is Step.Party or Step.Diet or Step.DietOther or Step.Wheelchair or Step.WheelchairCount or Step.Note or Step.Confirmed)
        {
            answers = answers with { Attending = true };
        }

        if (step is Step.Diet or Step.DietOther or Step.Wheelchair or Step.WheelchairCount or Step.Note or Step.Confirmed)
        {
            answers = answers with { PartySize = partySize };
        }

        if (step is Step.Wheelchair or Step.WheelchairCount or Step.Note or Step.Confirmed)
        {
            answers = answers with
            {
                Diets = Enumerable.Range(1, partySize).Select(i => new DietAnswer(i, Dietary.None)).ToImmutableList(),
            };
        }

        if (step is Step.Note or Step.Confirmed)
        {
            answers = answers with { WheelchairUsers = 0 };
        }

        if (step == Step.Declined)
        {
            answers = answers with { Attending = false };
        }

        return ConversationState.New with { Step = step, PersonIndex = personIndex, Answers = answers };
    }

    public static EngineResult Send(ConversationState state, Inbound inbound, EventDetails? ev = null) =>
        Bot.Handle(state, inbound, ev ?? Event);

    public static OutboundMessage Single(this EngineResult result) => Assert.Single(result.Messages);

    public static IEnumerable<string> Keys(this EngineResult result) => result.Single().Parts.Select(p => p.Key);

    public static IEnumerable<string> ChoiceIds(this OutboundMessage message) => message.Choices.Select(c => c.Id);

    public static string Repeat(char c, int count) => new(c, count);
}
