using static RsvpDesk.Engine.Tests.TestSupport;

namespace RsvpDesk.Engine.Tests;

/// <summary>Spec 6.2: STOP, HELP, CHANGE, idempotency and partial answers, in every state.</summary>
public sealed class GlobalRuleTests
{
    public static TheoryData<Step> ConversationSteps() =>
    [
        Step.NotInvited, Step.Invited, Step.Party, Step.Diet, Step.DietOther,
        Step.Wheelchair, Step.WheelchairCount, Step.Note, Step.Confirmed, Step.Declined,
    ];

    public static TheoryData<Step> InProgressSteps() =>
    [
        Step.NotInvited, Step.Invited, Step.Party, Step.Diet, Step.DietOther,
        Step.Wheelchair, Step.WheelchairCount, Step.Note,
    ];

    // ---- STOP ----

    [Theory]
    [MemberData(nameof(ConversationSteps))]
    public void STOP_opts_out_from_any_state_with_one_confirmation(Step step)
    {
        var from = At(step);

        var result = Send(from, Text("STOP"));

        Assert.Equal(Step.OptedOut, result.State.Step);
        Assert.Equal([new OptedOut()], result.Captures);
        Assert.Equal([MessageKeys.OptedOut], result.Keys());
        Assert.Equal(from.Answers, result.State.Answers);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("Stop.")]
    [InlineData("UNSUBSCRIBE")]
    public void STOP_is_matched_case_insensitively(string text)
    {
        Assert.Equal(Step.OptedOut, Send(At(Step.Party), Text(text)).State.Step);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("HELP")]
    [InlineData("CHANGE")]
    [InlineData("STOP")]
    public void After_STOP_the_bot_is_silent(string text)
    {
        var optedOut = Send(At(Step.Diet), Text("stop")).State;

        var result = Send(optedOut, Text(text));

        Assert.Empty(result.Messages);
        Assert.Empty(result.Captures);
        Assert.Equal(Step.OptedOut, result.State.Step);
    }

    [Fact]
    public void After_STOP_invitations_are_not_sent()
    {
        var optedOut = Send(At(Step.Invited), Text("stop")).State;

        var result = Bot.Invite(optedOut, Event);

        Assert.Empty(result.Messages);
        Assert.Equal(Step.OptedOut, result.State.Step);
    }

    [Fact]
    public void STOP_inside_a_free_text_answer_is_still_STOP()
    {
        Assert.Equal(Step.OptedOut, Send(At(Step.DietOther), Text("stop")).State.Step);
        Assert.Equal(Step.OptedOut, Send(At(Step.Note), Text("Stop")).State.Step);
    }

    [Fact]
    public void A_sentence_containing_stop_is_not_STOP()
    {
        var result = Send(At(Step.Note), Text("Please stop at the side entrance"));

        Assert.Equal(Step.Confirmed, result.State.Step);
        Assert.Equal("Please stop at the side entrance", result.State.Answers.AccessNote);
    }

    // ---- HELP ----

    [Theory]
    [MemberData(nameof(InProgressSteps))]
    public void HELP_flags_the_guest_and_repeats_the_current_question(Step step)
    {
        var from = At(step) with { InvalidStreak = 1 };

        var result = Send(from, Text("help"));

        Assert.Equal(step, result.State.Step);
        Assert.Equal([new NeedsAttention()], result.Captures);
        Assert.Equal(0, result.State.InvalidStreak);
        var message = result.Single();
        Assert.Equal(MessageKeys.HelpAck, message.Parts[0].Key);
        Assert.Equal(Bot.Prompt(from, Event).Choices, message.Choices);
    }

    [Theory]
    [InlineData(Step.Confirmed)]
    [InlineData(Step.Declined)]
    public void HELP_after_finishing_flags_the_guest_and_acknowledges(Step step)
    {
        var result = Send(At(step), Text("Help"));

        Assert.Equal(step, result.State.Step);
        Assert.Equal([new NeedsAttention()], result.Captures);
        Assert.Equal([MessageKeys.HelpAck], result.Keys());
    }

    // ---- CHANGE ----

    [Theory]
    [MemberData(nameof(InProgressSteps))]
    public void CHANGE_before_finishing_is_invalid(Step step)
    {
        var from = At(step);

        var result = Send(from, Text("change"));

        Assert.Equal(step, result.State.Step);
        Assert.Equal(from.Answers, result.State.Answers);
        Assert.Empty(result.Captures);
        Assert.Equal(MessageKeys.ChangeNotYet, result.Keys().First());
        Assert.Equal(1, result.State.InvalidStreak);
    }

    [Fact]
    public void CHANGE_after_confirming_clears_answers_and_asks_to_attend_again()
    {
        var result = Send(At(Step.Confirmed), Text("change"));

        Assert.Equal(Step.Invited, result.State.Step);
        Assert.Equal(RsvpAnswers.Empty, result.State.Answers);
        Assert.Equal(1, result.State.PersonIndex);
        Assert.Equal([MessageKeys.RestartAck, MessageKeys.AttendQuestion], result.Keys());
        Assert.Equal(OptionIds.AttendOptions, result.Single().ChoiceIds());
    }

    [Theory]
    [InlineData(Step.Confirmed, MessageKeys.AlreadyConfirmed, MessageKeys.ConfirmedSummary)]
    [InlineData(Step.Declined, MessageKeys.AlreadyDeclined, MessageKeys.Declined)]
    public void Other_messages_after_finishing_restate_the_answer(Step step, string firstKey, string secondKey)
    {
        var result = Send(At(step), Text("thanks!"));

        Assert.Equal(step, result.State.Step);
        Assert.Empty(result.Captures);
        Assert.Equal([firstKey, secondKey], result.Keys().Take(2));
        Assert.Equal(0, result.State.InvalidStreak);
    }

    // ---- Idempotency ----

    [Theory]
    [MemberData(nameof(ConversationSteps))]
    public void The_same_inbound_message_twice_does_not_advance_twice(Step step)
    {
        var inbound = Text("1");
        var first = Send(At(step), inbound);

        var second = Send(first.State, inbound);

        Assert.True(second.Duplicate);
        Assert.Same(first.State, second.State);
        Assert.Empty(second.Messages);
        Assert.Empty(second.Captures);
    }

    [Fact]
    public void Duplicate_detection_survives_later_messages_but_is_bounded()
    {
        var state = At(Step.Invited);
        var early = Text("hello");
        state = Send(state, early).State;
        for (var i = 0; i < ConversationState.RecentMessageIdLimit - 1; i++)
        {
            state = Send(state, Text("?")).State;
        }

        Assert.True(Send(state, early).Duplicate);
        Assert.Equal(ConversationState.RecentMessageIdLimit, state.RecentMessageIds.Count);

        state = Send(state, Text("?")).State;
        Assert.Equal(ConversationState.RecentMessageIdLimit, state.RecentMessageIds.Count);
        Assert.DoesNotContain(early.MessageId, state.RecentMessageIds);
    }

    // ---- Partial answers ----

    [Fact]
    public void Partial_answers_are_kept_in_state_and_emitted_as_they_arrive()
    {
        var state = Bot.Invite(ConversationState.New, Event).State;
        var captures = new List<Capture>();
        foreach (var input in new[] { Text("yes"), Text("2"), Text("vegan") })
        {
            var r = Send(state, input);
            captures.AddRange(r.Captures);
            state = r.State;
        }

        Assert.Equal(Step.Diet, state.Step);
        Assert.Equal(2, state.PersonIndex);
        Assert.True(state.Answers.Attending);
        Assert.Equal(2, state.Answers.PartySize);
        Assert.Equal([new DietAnswer(1, Dietary.Vegan)], state.Answers.Diets);
        Assert.Null(state.Answers.WheelchairUsers);
        Assert.Equal<Capture>([new AttendanceCaptured(true), new PartySizeCaptured(2), new DietaryCaptured(1, Dietary.Vegan, null)], captures);
    }
}
