using static RsvpDesk.Engine.Tests.TestSupport;

namespace RsvpDesk.Engine.Tests;

/// <summary>Every row of spec section 6.1: valid input moves to the right state and captures the right data.</summary>
public sealed class TransitionTests
{
    public sealed record Row(string Name, ConversationState From, Inbound Input, Step ExpectedStep, Capture[] ExpectedCaptures, string ExpectedPromptKey)
    {
        public override string ToString() => Name;
    }

    public static TheoryData<Row> ValidRows() =>
    [
        // Invited
        new("Invited: typed yes", At(Step.Invited), Text("yes"), Step.Party, [new AttendanceCaptured(true)], MessageKeys.PartyQuestion),
        new("Invited: typed Y!", At(Step.Invited), Text(" Y! "), Step.Party, [new AttendanceCaptured(true)], MessageKeys.PartyQuestion),
        new("Invited: button yes", At(Step.Invited), Tap(OptionIds.AttendYes), Step.Party, [new AttendanceCaptured(true)], MessageKeys.PartyQuestion),
        new("Invited: typed button label", At(Step.Invited), Text("Yes, I'll be there"), Step.Party, [new AttendanceCaptured(true)], MessageKeys.PartyQuestion),
        new("Invited: typed NO", At(Step.Invited), Text("NO"), Step.Declined, [new AttendanceCaptured(false), new RsvpCompleted(false)], MessageKeys.Declined),
        new("Invited: button no", At(Step.Invited), Tap(OptionIds.AttendNo), Step.Declined, [new AttendanceCaptured(false), new RsvpCompleted(false)], MessageKeys.Declined),

        // Party (max 4): boundaries 1 and max
        new("Party: 1", At(Step.Party), Text("1"), Step.Diet, [new PartySizeCaptured(1)], MessageKeys.DietQuestionSelf),
        new("Party: max (4)", At(Step.Party), Text("4"), Step.Diet, [new PartySizeCaptured(4)], MessageKeys.DietQuestionSelf),
        new("Party: word", At(Step.Party), Text("Two"), Step.Diet, [new PartySizeCaptured(2)], MessageKeys.DietQuestionSelf),
        new("Party: list row", At(Step.Party), Tap("party.3", "3"), Step.Diet, [new PartySizeCaptured(3)], MessageKeys.DietQuestionSelf),

        // Diet(i), not the last person -> Diet(i+1)
        new("Diet(1) of 3: button vegan", At(Step.Diet, 3, 1), Tap(OptionIds.DietVegan), Step.Diet, [new DietaryCaptured(1, Dietary.Vegan, null)], MessageKeys.DietQuestionGuest),
        new("Diet(1) of 3: typed none", At(Step.Diet, 3, 1), Text("none"), Step.Diet, [new DietaryCaptured(1, Dietary.None, null)], MessageKeys.DietQuestionGuest),
        new("Diet(2) of 3: typed halal", At(Step.Diet, 3, 2), Text("Halal"), Step.Diet, [new DietaryCaptured(2, Dietary.Halal, null)], MessageKeys.DietQuestionGuest),
        new("Diet(1): typed gluten free", At(Step.Diet, 3, 1), Text("gluten free"), Step.Diet, [new DietaryCaptured(1, Dietary.GlutenFree, null)], MessageKeys.DietQuestionGuest),
        new("Diet(1): typed veg", At(Step.Diet, 3, 1), Text("veg"), Step.Diet, [new DietaryCaptured(1, Dietary.Vegetarian, null)], MessageKeys.DietQuestionGuest),
        new("Diet(1): list position", At(Step.Diet, 3, 1), Text("2"), Step.Diet, [new DietaryCaptured(1, Dietary.Vegetarian, null)], MessageKeys.DietQuestionGuest),
        // Diet(last) -> Wheelchair
        new("Diet(3) of 3 -> Wheelchair", At(Step.Diet, 3, 3), Tap(OptionIds.DietNone), Step.Wheelchair, [new DietaryCaptured(3, Dietary.None, null)], MessageKeys.WheelchairQuestion),
        new("Diet(1) of 1 -> Wheelchair", At(Step.Diet, 1, 1), Text("vegetarian"), Step.Wheelchair, [new DietaryCaptured(1, Dietary.Vegetarian, null)], MessageKeys.WheelchairQuestion),
        // Other -> DietOther
        new("Diet: Other button", At(Step.Diet, 3, 2), Tap(OptionIds.DietOther), Step.DietOther, [], MessageKeys.DietOtherQuestion),
        new("Diet: typed other", At(Step.Diet, 3, 2), Text("other"), Step.DietOther, [], MessageKeys.DietOtherQuestion),

        // DietOther -> back to the Diet flow
        new("DietOther(2) of 3 -> Diet(3)", At(Step.DietOther, 3, 2), Text("Nut allergy"), Step.Diet, [new DietaryCaptured(2, Dietary.Other, "Nut allergy")], MessageKeys.DietQuestionGuest),
        new("DietOther(3) of 3 -> Wheelchair", At(Step.DietOther, 3, 3), Text("No shellfish"), Step.Wheelchair, [new DietaryCaptured(3, Dietary.Other, "No shellfish")], MessageKeys.WheelchairQuestion),
        new("DietOther: exactly 60 chars", At(Step.DietOther, 1, 1), Text(Repeat('a', 60)), Step.Wheelchair, [new DietaryCaptured(1, Dietary.Other, Repeat('a', 60))], MessageKeys.WheelchairQuestion),
        new("DietOther: typed word 'none' is kept as free text", At(Step.DietOther, 1, 1), Text("none"), Step.Wheelchair, [new DietaryCaptured(1, Dietary.Other, "none")], MessageKeys.WheelchairQuestion),

        // Wheelchair
        new("Wheelchair: no", At(Step.Wheelchair, 3), Text("no"), Step.Note, [new WheelchairUsersCaptured(0)], MessageKeys.NoteQuestion),
        new("Wheelchair: button no", At(Step.Wheelchair, 3), Tap(OptionIds.WheelchairNo), Step.Note, [new WheelchairUsersCaptured(0)], MessageKeys.NoteQuestion),
        new("Wheelchair: yes, party of 3", At(Step.Wheelchair, 3), Tap(OptionIds.WheelchairYes), Step.WheelchairCount, [], MessageKeys.WheelchairCountQuestion),
        new("Wheelchair: yes, party of 1 skips the count", At(Step.Wheelchair, 1), Text("yes"), Step.Note, [new WheelchairUsersCaptured(1)], MessageKeys.NoteQuestion),

        // WheelchairCount: 1 to party size
        new("WheelchairCount: 1", At(Step.WheelchairCount, 3), Text("1"), Step.Note, [new WheelchairUsersCaptured(1)], MessageKeys.NoteQuestion),
        new("WheelchairCount: party size", At(Step.WheelchairCount, 3), Tap("wheelchairs.3"), Step.Note, [new WheelchairUsersCaptured(3)], MessageKeys.NoteQuestion),

        // Note
        new("Note: Skip button", At(Step.Note), Tap(OptionIds.NoteSkip), Step.Confirmed, [new AccessNoteCaptured(null), new RsvpCompleted(true)], MessageKeys.ConfirmedSummary),
        new("Note: typed skip", At(Step.Note), Text("Skip"), Step.Confirmed, [new AccessNoteCaptured(null), new RsvpCompleted(true)], MessageKeys.ConfirmedSummary),
        new("Note: free text", At(Step.Note), Text("  Need a step-free route  "), Step.Confirmed, [new AccessNoteCaptured("Need a step-free route"), new RsvpCompleted(true)], MessageKeys.ConfirmedSummary),
        new("Note: exactly 120 chars", At(Step.Note), Text(Repeat('b', 120)), Step.Confirmed, [new AccessNoteCaptured(Repeat('b', 120)), new RsvpCompleted(true)], MessageKeys.ConfirmedSummary),

        // Confirmed / Declined: CHANGE restarts at the attend question
        new("Confirmed: CHANGE", At(Step.Confirmed), Text("CHANGE"), Step.Invited, [new AnswersCleared()], MessageKeys.RestartAck),
        new("Confirmed: Change button", At(Step.Confirmed), Tap(OptionIds.Change), Step.Invited, [new AnswersCleared()], MessageKeys.RestartAck),
        new("Declined: change", At(Step.Declined), Text("change"), Step.Invited, [new AnswersCleared()], MessageKeys.RestartAck),
    ];

    [Theory]
    [MemberData(nameof(ValidRows))]
    public void Valid_input_moves_to_the_expected_state(Row row)
    {
        var result = Send(row.From, row.Input);

        Assert.Equal(row.ExpectedStep, result.State.Step);
        Assert.Equal(row.ExpectedCaptures, result.Captures);
        Assert.Equal(0, result.State.InvalidStreak);
        Assert.Equal(row.ExpectedPromptKey, result.Keys().First());
        Assert.False(result.Duplicate);
    }

    [Fact]
    public void Diet_question_names_the_guest_number_after_the_first_person()
    {
        var result = Send(At(Step.Diet, 3, 1), Tap(OptionIds.DietNone));

        Assert.Equal(2, result.State.PersonIndex);
        var part = Assert.Single(result.Single().Parts);
        Assert.Equal(MessageKeys.DietQuestionGuest, part.Key);
        Assert.Equal(2, part.Args["index"]);
    }

    [Fact]
    public void Party_max_is_configurable_per_event()
    {
        var bigEvent = Event with { PartyMax = 6 };

        var ok = Send(At(Step.Party), Text("6"), bigEvent);
        var tooMany = Send(At(Step.Party), Text("7"), bigEvent);

        Assert.Equal(Step.Diet, ok.State.Step);
        Assert.Equal(Step.Party, tooMany.State.Step);
        Assert.Equal(6, ok.State.Answers.PartySize);
    }

    [Fact]
    public void Invite_sends_the_invitation_with_yes_no_choices()
    {
        var result = Bot.Invite(ConversationState.New, Event);

        Assert.Equal(Step.Invited, result.State.Step);
        var message = result.Single();
        Assert.Equal(MessagePurpose.Invitation, message.Purpose);
        Assert.Equal([MessageKeys.Invitation, MessageKeys.AttendQuestion], message.Parts.Select(p => p.Key));
        Assert.Equal(OptionIds.AttendOptions, message.ChoiceIds());
    }

    [Fact]
    public void A_guest_who_writes_before_being_invited_gets_the_attend_question()
    {
        var result = Send(ConversationState.New, Text("hi"));

        Assert.Equal(Step.Invited, result.State.Step);
        Assert.Equal(OptionIds.AttendOptions, result.Single().ChoiceIds());
    }

    [Fact]
    public void Questions_offer_the_right_choices()
    {
        Assert.Equal(["party.1", "party.2", "party.3", "party.4"], Send(At(Step.Invited), Text("yes")).Single().ChoiceIds());
        Assert.Equal(OptionIds.DietOptions.Select(o => o.Id), Send(At(Step.Party), Text("2")).Single().ChoiceIds());
        Assert.Equal(["wheelchairs.1", "wheelchairs.2", "wheelchairs.3"], Send(At(Step.Wheelchair, 3), Text("yes")).Single().ChoiceIds());
        Assert.Equal([OptionIds.NoteSkip], Send(At(Step.Wheelchair, 3), Text("no")).Single().ChoiceIds());
        Assert.Equal([OptionIds.Change], Send(At(Step.Note), Text("skip")).Single().ChoiceIds());
        Assert.Empty(Send(At(Step.Diet), Tap(OptionIds.DietOther)).Single().Choices);
    }
}
