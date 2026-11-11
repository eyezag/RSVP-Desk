using static RsvpDesk.Engine.Tests.TestSupport;

namespace RsvpDesk.Engine.Tests;

/// <summary>Spec 6.2 "Invalid input": repeat the question with the same options and a hint; suggest HELP after two in a row.</summary>
public sealed class InvalidInputTests
{
    public sealed record Row(string Name, ConversationState From, Inbound Input, string ExpectedHintKey)
    {
        public override string ToString() => Name;
    }

    public static TheoryData<Row> InvalidRows() =>
    [
        new("Invited: maybe", At(Step.Invited), Text("maybe"), MessageKeys.InvalidChoice),
        new("Invited: empty", At(Step.Invited), Text(""), MessageKeys.InvalidChoice),
        new("Invited: null text", At(Step.Invited), Text(null), MessageKeys.InvalidChoice),
        new("Invited: stale diet button", At(Step.Invited), Tap(OptionIds.DietVegan), MessageKeys.InvalidChoice),

        new("Party: 0", At(Step.Party), Text("0"), MessageKeys.InvalidNumber),
        new("Party: max+1 (5)", At(Step.Party), Text("5"), MessageKeys.InvalidNumber),
        new("Party: negative", At(Step.Party), Text("-1"), MessageKeys.InvalidNumber),
        new("Party: huge number", At(Step.Party), Text("99999999999999999999"), MessageKeys.InvalidNumber),
        new("Party: words", At(Step.Party), Text("a few"), MessageKeys.InvalidNumber),
        new("Party: decimal", At(Step.Party), Text("2.5"), MessageKeys.InvalidNumber),
        new("Party: list row beyond max", At(Step.Party), Tap("party.9"), MessageKeys.InvalidNumber),
        new("Party: stale yes button", At(Step.Party), Tap(OptionIds.AttendYes), MessageKeys.InvalidNumber),

        new("Diet: unknown word", At(Step.Diet), Text("pescatarian"), MessageKeys.InvalidChoice),
        new("Diet: position out of range", At(Step.Diet), Text("7"), MessageKeys.InvalidChoice),
        new("Diet: stale button", At(Step.Diet), Tap(OptionIds.WheelchairNo), MessageKeys.InvalidChoice),

        new("DietOther: 61 chars", At(Step.DietOther), Text(Repeat('a', 61)), MessageKeys.InvalidTooLong),
        new("DietOther: blank", At(Step.DietOther), Text("   "), MessageKeys.InvalidEmpty),
        new("DietOther: button tap", At(Step.DietOther), Tap(OptionIds.DietOther), MessageKeys.InvalidEmpty),

        new("Wheelchair: maybe", At(Step.Wheelchair), Text("sometimes"), MessageKeys.InvalidChoice),

        new("WheelchairCount: 0", At(Step.WheelchairCount, 3), Text("0"), MessageKeys.InvalidNumber),
        new("WheelchairCount: party size + 1", At(Step.WheelchairCount, 3), Text("4"), MessageKeys.InvalidNumber),

        new("Note: 121 chars", At(Step.Note), Text(Repeat('b', 121)), MessageKeys.InvalidTooLong),
        new("Note: blank", At(Step.Note), Text(""), MessageKeys.InvalidEmpty),
    ];

    [Theory]
    [MemberData(nameof(InvalidRows))]
    public void Invalid_input_repeats_the_question_with_a_hint(Row row)
    {
        var expectedPrompt = Bot.Prompt(row.From, Event);

        var result = Send(row.From, row.Input);

        Assert.Equal(row.From.Step, result.State.Step);
        Assert.Equal(row.From.Answers, result.State.Answers);
        Assert.Equal(row.From.PersonIndex, result.State.PersonIndex);
        Assert.Empty(result.Captures);
        Assert.Equal(1, result.State.InvalidStreak);

        var message = result.Single();
        Assert.Equal(row.ExpectedHintKey, message.Parts[0].Key);
        Assert.Equal(expectedPrompt.Parts.Select(p => p.Key), message.Parts.Skip(1).Select(p => p.Key));
        Assert.Equal(expectedPrompt.Choices, message.Choices);
        Assert.DoesNotContain(MessageKeys.HelpHint, message.Parts.Select(p => p.Key));
    }

    [Fact]
    public void Second_invalid_answer_in_a_row_suggests_HELP()
    {
        var first = Send(At(Step.Party), Text("lots"));
        var second = Send(first.State, Text("many"));
        var third = Send(second.State, Text("loads"));

        Assert.DoesNotContain(MessageKeys.HelpHint, first.Keys());
        Assert.Equal(MessageKeys.HelpHint, second.Keys().Last());
        Assert.Equal(MessageKeys.HelpHint, third.Keys().Last());
        Assert.Equal(3, third.State.InvalidStreak);
    }

    [Fact]
    public void A_valid_answer_resets_the_invalid_streak()
    {
        var bad = Send(At(Step.Party), Text("lots"));
        var good = Send(bad.State, Text("2"));
        var badAgain = Send(good.State, Text("pizza"));

        Assert.Equal(0, good.State.InvalidStreak);
        Assert.Equal(1, badAgain.State.InvalidStreak);
        Assert.DoesNotContain(MessageKeys.HelpHint, badAgain.Keys());
    }

    [Fact]
    public void Invalid_number_hint_states_the_allowed_range()
    {
        var party = Send(At(Step.Party), Text("9")).Single().Parts[0];
        var wheelchairs = Send(At(Step.WheelchairCount, 2), Text("3")).Single().Parts[0];

        Assert.Equal(Event.PartyMax, party.Args["max"]);
        Assert.Equal(2, wheelchairs.Args["max"]);
    }

    [Fact]
    public void Length_limit_counts_characters_as_people_see_them()
    {
        // 60 emoji: each is one character to the guest but two UTF-16 code units.
        var sixtyEmoji = string.Concat(Enumerable.Repeat("🥜", 60));

        var result = Send(At(Step.DietOther, 1), Text(sixtyEmoji));

        Assert.Equal(Step.Wheelchair, result.State.Step);
    }
}
