using static RsvpDesk.Engine.Tests.TestSupport;

namespace RsvpDesk.Engine.Tests;

public sealed class ReminderTests
{
    [Theory]
    [InlineData(Step.NotInvited)]
    [InlineData(Step.Confirmed)]
    [InlineData(Step.Declined)]
    [InlineData(Step.OptedOut)]
    public void Finished_uninvited_and_opted_out_guests_get_no_reminder(Step step)
    {
        Assert.Empty(Bot.Remind(At(step), Event).Messages);
    }

    [Fact]
    public void A_guest_who_has_not_replied_is_asked_to_attend_again()
    {
        var result = Bot.Remind(At(Step.Invited), Event);

        var message = result.Single();
        Assert.Equal(MessagePurpose.Reminder, message.Purpose);
        Assert.Equal([MessageKeys.ReminderNotReplied, MessageKeys.AttendQuestion], message.Parts.Select(p => p.Key));
        Assert.Equal(OptionIds.AttendOptions, message.ChoiceIds());
    }

    [Theory]
    [InlineData(Step.Party, MessageKeys.PartyQuestion)]
    [InlineData(Step.Diet, MessageKeys.DietQuestionGuest)]
    [InlineData(Step.Note, MessageKeys.NoteQuestion)]
    public void A_guest_who_stopped_half_way_gets_their_current_question(Step step, string questionKey)
    {
        var from = At(step, partySize: 3, personIndex: 2) with { InvalidStreak = 1 };

        var result = Bot.Remind(from, Event);

        Assert.Equal([MessageKeys.ReminderUnfinished, questionKey], result.Keys());
        Assert.Equal(step, result.State.Step);
        Assert.Equal(from.Answers, result.State.Answers);
        Assert.Equal(0, result.State.InvalidStreak);
        Assert.Empty(result.Captures);
    }

    [Fact]
    public void A_reply_to_a_reminder_continues_the_conversation()
    {
        var state = Bot.Remind(At(Step.Party), Event).State;

        Assert.Equal(Step.Diet, Send(state, Text("2")).State.Step);
    }
}

public sealed class ResumeTests
{
    [Theory]
    [InlineData(Step.Invited, MessageKeys.AttendQuestion)]
    [InlineData(Step.Party, MessageKeys.PartyQuestion)]
    [InlineData(Step.DietOther, MessageKeys.DietOtherQuestion)]
    [InlineData(Step.Note, MessageKeys.NoteQuestion)]
    public void Continue_repeats_the_current_question_without_counting_as_invalid(Step step, string key)
    {
        var from = At(step) with { InvalidStreak = 1 };

        var result = Send(from, Tap(OptionIds.Resume, "Continue"));

        Assert.Equal(step, result.State.Step);
        Assert.Equal(0, result.State.InvalidStreak);
        Assert.Equal([key], result.Keys());
        Assert.Empty(result.Captures);
    }

    [Fact]
    public void Continue_after_finishing_restates_the_answer()
    {
        var result = Send(At(Step.Confirmed), Tap(OptionIds.Resume, "Continue"));

        Assert.Equal(MessageKeys.AlreadyConfirmed, result.Keys().First());
    }

    [Fact]
    public void Typed_continue_in_a_free_text_step_is_just_text()
    {
        var result = Send(At(Step.Note), Text("continue"));

        Assert.Equal(Step.Confirmed, result.State.Step);
        Assert.Equal("continue", result.State.Answers.AccessNote);
    }
}
