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
