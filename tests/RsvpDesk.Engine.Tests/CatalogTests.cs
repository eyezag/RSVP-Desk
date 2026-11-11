using System.Reflection;
using System.Text.Json;
using static RsvpDesk.Engine.Tests.TestSupport;

namespace RsvpDesk.Engine.Tests;

public sealed class CatalogTests
{
    private static readonly MessageCatalog Catalog = MessageCatalog.English;

    public static TheoryData<string> AllMessageKeys()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(MessageKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            data.Add((string)field.GetValue(null)!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllMessageKeys))]
    public void Every_message_key_has_text(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(Catalog.Message(key)));
    }

    [Fact]
    public void Every_option_has_a_label()
    {
        foreach (var id in OptionIds.AllFixed())
        {
            Assert.False(string.IsNullOrWhiteSpace(Catalog.OptionLabel(id)));
        }
    }

    [Fact]
    public void Catalog_has_no_unused_message_keys()
    {
        var known = typeof(MessageKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)!).ToHashSet();
        var unused = Catalog.MessageKeysDefined.Where(k => !known.Contains(k)).ToList();
        Assert.True(unused.Count == 0, $"Unused keys: {string.Join(", ", unused)}");
    }

    [Fact]
    public void Aliases_never_point_a_word_at_two_options_in_the_same_question()
    {
        string[][] questions =
        [
            [.. OptionIds.AttendOptions],
            [.. OptionIds.WheelchairOptions],
            [.. OptionIds.DietOptions.Select(o => o.Id)],
        ];
        string[] probes = ["yes", "y", "no", "n", "none", "nope", "veg", "vegan", "other", "gf", "halal"];

        foreach (var options in questions)
        {
            foreach (var probe in probes)
            {
                Assert.True(options.Count(o => Catalog.MatchesOption(o, probe)) <= 1, $"'{probe}' is ambiguous in [{string.Join(", ", options)}]");
            }
        }
    }

    [Fact]
    public void Keywords_do_not_clash_with_option_words()
    {
        foreach (var id in OptionIds.AllFixed())
        {
            foreach (var keyword in new[] { "stop", "help", "change", "unsubscribe" })
            {
                Assert.False(Catalog.MatchesOption(id, keyword), $"'{keyword}' is also a word for {id}");
            }
        }
    }

    // Provisional limits for WhatsApp interactive messages, to be confirmed against Meta's
    // documentation in build step 7 (see docs/decisions.md D-106). If Meta's limits differ,
    // change them here and the labels in messages.en.json.
    private const int ProvisionalButtonTitleMax = 20;
    private const int ProvisionalListRowTitleMax = 24;

    [Fact]
    public void Button_labels_fit_the_provisional_WhatsApp_button_title_limit()
    {
        foreach (var id in OptionIds.AttendOptions.Concat(OptionIds.WheelchairOptions).Append(OptionIds.NoteSkip).Append(OptionIds.Change))
        {
            Assert.True(Catalog.OptionLabel(id).Length <= ProvisionalButtonTitleMax, $"{id} label is too long for a button");
        }
    }

    [Fact]
    public void Dietary_labels_fit_the_provisional_WhatsApp_list_row_title_limit()
    {
        foreach (var (id, _) in OptionIds.DietOptions)
        {
            Assert.True(Catalog.OptionLabel(id).Length <= ProvisionalListRowTitleMax, $"{id} label is too long for a list row");
        }
    }

    [Fact]
    public void Every_message_the_engine_can_produce_renders_without_missing_arguments()
    {
        var renderer = new MessageRenderer();
        var steps = Enum.GetValues<Step>();
        foreach (var step in steps)
        {
            var state = At(step, partySize: 2, personIndex: 2) with
            {
                Answers = At(step, 2).Answers with { AccessNote = "Ramp {please}" },
            };
            Assert.NotEmpty(renderer.Render(Bot.Prompt(state, Event)).Body);
            foreach (var input in new[] { Text("help"), Text("nonsense"), Text("change") })
            {
                foreach (var message in Bot.Handle(state, input, Event).Messages)
                {
                    Assert.NotEmpty(renderer.Render(message).Body);
                }
            }
        }

        Assert.NotEmpty(renderer.Render(Bot.Invite(ConversationState.New, Event).Single()).Body);
    }

    [Fact]
    public void Invitation_renders_event_details_and_says_why_information_is_collected()
    {
        var body = new MessageRenderer().Render(Bot.Invite(ConversationState.New, Event).Single()).Body;

        Assert.Contains("Year-End Function 2026", body);
        Assert.Contains("Saturday 5 December 2026 at 18:00", body);
        Assert.Contains("The Jacaranda Hall, Johannesburg", body);
        Assert.Contains("catering and access", body);
        Assert.Contains("STOP", body);
    }

    [Fact]
    public void Guest_text_with_braces_is_not_treated_as_a_placeholder()
    {
        var state = At(Step.Confirmed) with { Answers = At(Step.Confirmed).Answers with { AccessNote = "{event}" } };

        var body = new MessageRenderer().Render(Bot.Prompt(state, Event)).Body;

        Assert.Contains("Note: {event}", body);
    }

    [Fact]
    public void Number_choices_render_as_digits()
    {
        var titles = new MessageRenderer().Render(Bot.Prompt(At(Step.Party), Event)).Choices.Select(c => c.Title);

        Assert.Equal(["1", "2", "3", "4"], titles);
    }

    [Fact]
    public void Conversation_state_round_trips_through_JSON()
    {
        var (state, _) = (Send(Send(Send(At(Step.Invited), Text("yes")).State, Text("2")).State, Tap(OptionIds.DietOther)).State, 0);
        state = Send(state, Text("No onions")).State;

        var json = JsonSerializer.Serialize(state);
        var back = JsonSerializer.Deserialize<ConversationState>(json)!;

        Assert.Equal(state.Step, back.Step);
        Assert.Equal(state.PersonIndex, back.PersonIndex);
        Assert.Equal(state.Answers.Diets, back.Answers.Diets);
        Assert.Equal(state.RecentMessageIds, back.RecentMessageIds);
        Assert.Equal(state.Answers.PartySize, back.Answers.PartySize);
    }
}
