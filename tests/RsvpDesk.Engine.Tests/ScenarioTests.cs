using static RsvpDesk.Engine.Tests.TestSupport;

namespace RsvpDesk.Engine.Tests;

/// <summary>Whole conversations from invitation to the end.</summary>
public sealed class ScenarioTests
{
    private static (ConversationState State, List<Capture> Captures, List<OutboundMessage> Messages) Run(params Inbound[] inputs)
    {
        var invite = Bot.Invite(ConversationState.New, Event);
        var state = invite.State;
        var captures = new List<Capture>();
        var messages = new List<OutboundMessage>(invite.Messages);
        foreach (var input in inputs)
        {
            var result = Send(state, input);
            state = result.State;
            captures.AddRange(result.Captures);
            messages.AddRange(result.Messages);
        }

        return (state, captures, messages);
    }

    [Fact]
    public void Full_RSVP_with_an_Other_dietary_answer_in_the_middle_of_the_party()
    {
        var (state, captures, messages) = Run(
            Tap(OptionIds.AttendYes),
            Text("3"),
            Tap(OptionIds.DietVegetarian),
            Tap(OptionIds.DietOther),
            Text("Severe nut allergy"),
            Text("halal"),
            Text("yes"),
            Text("1"),
            Text("Accessible parking please"));

        Assert.Equal(Step.Confirmed, state.Step);
        Assert.Equal(
            [new DietAnswer(1, Dietary.Vegetarian), new DietAnswer(2, Dietary.Other, "Severe nut allergy"), new DietAnswer(3, Dietary.Halal)],
            state.Answers.Diets);
        Assert.Equal(1, state.Answers.WheelchairUsers);
        Assert.Equal("Accessible parking please", state.Answers.AccessNote);
        Assert.Equal(new RsvpCompleted(true), captures.Last());

        var summary = new MessageRenderer().Render(messages.Last()).Body;
        Assert.Contains("Party size: 3", summary);
        Assert.Contains("Vegetarian (1), Halal (1), Other: Severe nut allergy", summary);
        Assert.Contains("Wheelchair users: 1", summary);
        Assert.Contains("Accessible parking please", summary);
        Assert.Contains("Reply CHANGE to update.", summary);
    }

    [Fact]
    public void Declined_RSVP()
    {
        var (state, captures, _) = Run(Text("No"));

        Assert.Equal(Step.Declined, state.Step);
        Assert.False(state.Answers.Attending);
        Assert.Equal<Capture>([new AttendanceCaptured(false), new RsvpCompleted(false)], captures);
    }

    [Fact]
    public void Change_after_confirming_then_decline()
    {
        var (state, captures, _) = Run(
            Text("yes"), Text("1"), Text("none"), Text("no"), Text("skip"),
            Text("CHANGE"),
            Text("no"));

        Assert.Equal(Step.Declined, state.Step);
        Assert.Null(state.Answers.PartySize);
        Assert.Empty(state.Answers.Diets);
        Assert.Contains(new AnswersCleared(), captures);
        Assert.Equal(new RsvpCompleted(false), captures.Last());
    }

    [Fact]
    public void Change_after_declining_then_attend()
    {
        var (state, _, _) = Run(
            Text("no"), Text("change"),
            Text("yes"), Text("2"), Text("vegan"), Text("vegan"), Text("no"), Text("skip"));

        Assert.Equal(Step.Confirmed, state.Step);
        Assert.True(state.Answers.Attending);
        Assert.Equal(2, state.Answers.Diets.Count);
    }
}
