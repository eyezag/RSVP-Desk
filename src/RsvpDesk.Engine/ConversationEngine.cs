using System.Collections.Immutable;
using System.Globalization;

namespace RsvpDesk.Engine;

/// <summary>
/// The RSVP conversation (spec section 6) as a pure function. No I/O, no clock, no WhatsApp,
/// HTTP or database types. Text matching uses the catalog's word lists; the engine never
/// produces message text itself, only message keys.
/// </summary>
public sealed class ConversationEngine
{
    public const int DietOtherMaxLength = 60;
    public const int NoteMaxLength = 120;

    /// <summary>After this many invalid answers in a row, the bot suggests HELP.</summary>
    public const int InvalidAnswersBeforeHelpHint = 2;

    private readonly MessageCatalog _catalog;

    public ConversationEngine(MessageCatalog catalog) => _catalog = catalog;

    public ConversationEngine() : this(MessageCatalog.English)
    {
    }

    /// <summary>
    /// Produces the invitation and moves the guest to <see cref="Step.Invited"/>.
    /// Opted-out guests get nothing. Sending again to an invited guest re-sends the invitation
    /// without touching any answers.
    /// </summary>
    public EngineResult Invite(ConversationState state, EventDetails ev)
    {
        ev.Validate();
        if (state.Step == Step.OptedOut)
        {
            return new EngineResult(state, [], []);
        }

        var next = state.Step == Step.NotInvited ? state with { Step = Step.Invited, InvalidStreak = 0 } : state;
        return new EngineResult(next, [InvitationMessage(ev)], []);
    }

    /// <summary>Handles one inbound message from the guest.</summary>
    public EngineResult Handle(ConversationState state, Inbound inbound, EventDetails ev)
    {
        ev.Validate();

        if (!string.IsNullOrEmpty(inbound.MessageId) && state.RecentMessageIds.Contains(inbound.MessageId))
        {
            return new EngineResult(state, [], [], Duplicate: true);
        }

        state = Remember(state, inbound.MessageId);

        if (state.Step == Step.OptedOut)
        {
            // Opted out: stay silent. Only an organiser can re-enable messages.
            return new EngineResult(state, [], []);
        }

        var text = MessageCatalog.Normalise(inbound.Text);
        var replyId = string.IsNullOrWhiteSpace(inbound.ReplyId) ? null : inbound.ReplyId;

        // Global keywords. Only typed text can be STOP or HELP; CHANGE can also be a button.
        if (replyId is null && _catalog.IsKeyword(Keywords.Stop, text))
        {
            return new EngineResult(
                state with { Step = Step.OptedOut, InvalidStreak = 0 },
                [Reply(new MessagePart(MessageKeys.OptedOut))],
                [new OptedOut()]);
        }

        if (replyId is null && _catalog.IsKeyword(Keywords.Help, text))
        {
            return HandleHelp(state, ev);
        }

        if (replyId == OptionIds.Change || (replyId is null && _catalog.IsKeyword(Keywords.Change, text)))
        {
            return HandleChange(state, ev);
        }

        var input = new Input(text, replyId, inbound.Text?.Trim() ?? string.Empty);

        return state.Step switch
        {
            // A guest who writes before being invited gets the attend question straight away.
            Step.NotInvited => Ask(state with { Step = Step.Invited }, ev, InvitationMessage(ev)),
            Step.Invited => HandleAttend(state, input, ev),
            Step.Party => HandleParty(state, input, ev),
            Step.Diet => HandleDiet(state, input, ev),
            Step.DietOther => HandleDietOther(state, input, ev),
            Step.Wheelchair => HandleWheelchair(state, input, ev),
            Step.WheelchairCount => HandleWheelchairCount(state, input, ev),
            Step.Note => HandleNote(state, input, ev),
            Step.Confirmed => new EngineResult(state, [WithParts(Prompt(state, ev), new MessagePart(MessageKeys.AlreadyConfirmed))], []),
            Step.Declined => new EngineResult(state, [WithParts(Prompt(state, ev), new MessagePart(MessageKeys.AlreadyDeclined))], []),
            _ => throw new InvalidOperationException($"Unhandled step {state.Step}."),
        };
    }

    /// <summary>The question (with its options) for the guest's current step. Used for re-prompts and reminders.</summary>
    public OutboundMessage Prompt(ConversationState state, EventDetails ev)
    {
        var a = state.Answers;
        return state.Step switch
        {
            Step.NotInvited or Step.Invited => Reply(Choices(OptionIds.AttendOptions), Part(MessageKeys.AttendQuestion, ("event", ev.Name))),
            Step.Party => Reply(NumberChoices(OptionIds.PartyPrefix, ev.PartyMax), Part(MessageKeys.PartyQuestion, ("max", ev.PartyMax))),
            Step.Diet => Reply(
                Choices(OptionIds.DietOptions.Select(o => o.Id)),
                state.PersonIndex == 1
                    ? new MessagePart(MessageKeys.DietQuestionSelf)
                    : Part(MessageKeys.DietQuestionGuest, ("index", state.PersonIndex))),
            Step.DietOther => Reply(new MessagePart(MessageKeys.DietOtherQuestion)),
            Step.Wheelchair => Reply(Choices(OptionIds.WheelchairOptions), new MessagePart(MessageKeys.WheelchairQuestion)),
            Step.WheelchairCount => Reply(
                NumberChoices(OptionIds.WheelchairCountPrefix, a.PartySize ?? 1),
                Part(MessageKeys.WheelchairCountQuestion, ("max", a.PartySize ?? 1))),
            Step.Note => Reply(Choices([OptionIds.NoteSkip]), new MessagePart(MessageKeys.NoteQuestion)),
            Step.Confirmed => Summary(state, ev),
            Step.Declined => Reply(Choices([OptionIds.Change]), new MessagePart(MessageKeys.Declined)),
            Step.OptedOut => Reply(new MessagePart(MessageKeys.OptedOut)),
            _ => throw new InvalidOperationException($"Unhandled step {state.Step}."),
        };
    }

    private EngineResult HandleAttend(ConversationState state, Input input, EventDetails ev)
    {
        var choice = MatchChoice(input, OptionIds.AttendOptions);
        if (choice == OptionIds.AttendYes)
        {
            var next = state with { Step = Step.Party, Answers = state.Answers with { Attending = true } };
            return Ask(next, ev, captures: [new AttendanceCaptured(true)]);
        }

        if (choice == OptionIds.AttendNo)
        {
            var next = state with { Step = Step.Declined, Answers = state.Answers with { Attending = false } };
            return Ask(next, ev, captures: [new AttendanceCaptured(false), new RsvpCompleted(false)]);
        }

        return Invalid(state, ev, new MessagePart(MessageKeys.InvalidChoice));
    }

    private EngineResult HandleParty(ConversationState state, Input input, EventDetails ev)
    {
        var size = MatchNumber(input, OptionIds.PartyPrefix);
        if (size is null || size < 1 || size > ev.PartyMax)
        {
            return Invalid(state, ev, Part(MessageKeys.InvalidNumber, ("max", ev.PartyMax)));
        }

        var next = state with
        {
            Step = Step.Diet,
            PersonIndex = 1,
            Answers = state.Answers with { PartySize = size, Diets = [] },
        };
        return Ask(next, ev, captures: [new PartySizeCaptured(size.Value)]);
    }

    private EngineResult HandleDiet(ConversationState state, Input input, EventDetails ev)
    {
        var choice = MatchChoice(input, OptionIds.DietOptions.Select(o => o.Id).ToList(), allowPosition: true);
        if (choice is null)
        {
            return Invalid(state, ev, new MessagePart(MessageKeys.InvalidChoice));
        }

        var dietary = OptionIds.DietOptions.First(o => o.Id == choice).Dietary;
        if (dietary == Dietary.Other)
        {
            return Ask(state with { Step = Step.DietOther }, ev);
        }

        return RecordDiet(state, ev, new DietAnswer(state.PersonIndex, dietary));
    }

    private EngineResult HandleDietOther(ConversationState state, Input input, EventDetails ev)
    {
        var problem = ValidateFreeText(input, DietOtherMaxLength);
        if (problem is not null)
        {
            return Invalid(state, ev, problem);
        }

        return RecordDiet(state, ev, new DietAnswer(state.PersonIndex, Dietary.Other, input.Raw));
    }

    private EngineResult RecordDiet(ConversationState state, EventDetails ev, DietAnswer answer)
    {
        var answers = state.Answers.WithDiet(answer);
        var partySize = answers.PartySize ?? 1;
        var next = answer.PersonIndex < partySize
            ? state with { Step = Step.Diet, PersonIndex = answer.PersonIndex + 1, Answers = answers }
            : state with { Step = Step.Wheelchair, Answers = answers };
        return Ask(next, ev, captures: [new DietaryCaptured(answer.PersonIndex, answer.Dietary, answer.Note)]);
    }

    private EngineResult HandleWheelchair(ConversationState state, Input input, EventDetails ev)
    {
        var choice = MatchChoice(input, OptionIds.WheelchairOptions);
        if (choice == OptionIds.WheelchairNo)
        {
            return RecordWheelchairUsers(state, ev, 0);
        }

        if (choice == OptionIds.WheelchairYes)
        {
            // A party of one needs no follow-up question: the answer is 1 (decisions D-104).
            return (state.Answers.PartySize ?? 1) == 1
                ? RecordWheelchairUsers(state, ev, 1)
                : Ask(state with { Step = Step.WheelchairCount }, ev);
        }

        return Invalid(state, ev, new MessagePart(MessageKeys.InvalidChoice));
    }

    private EngineResult HandleWheelchairCount(ConversationState state, Input input, EventDetails ev)
    {
        var partySize = state.Answers.PartySize ?? 1;
        var count = MatchNumber(input, OptionIds.WheelchairCountPrefix);
        if (count is null || count < 1 || count > partySize)
        {
            return Invalid(state, ev, Part(MessageKeys.InvalidNumber, ("max", partySize)));
        }

        return RecordWheelchairUsers(state, ev, count.Value);
    }

    private EngineResult RecordWheelchairUsers(ConversationState state, EventDetails ev, int count)
    {
        var next = state with { Step = Step.Note, Answers = state.Answers with { WheelchairUsers = count } };
        return Ask(next, ev, captures: [new WheelchairUsersCaptured(count)]);
    }

    private EngineResult HandleNote(ConversationState state, Input input, EventDetails ev)
    {
        string? note;
        if (MatchChoice(input, [OptionIds.NoteSkip]) == OptionIds.NoteSkip)
        {
            note = null;
        }
        else
        {
            var problem = ValidateFreeText(input, NoteMaxLength);
            if (problem is not null)
            {
                return Invalid(state, ev, problem);
            }

            note = input.Raw;
        }

        var next = state with { Step = Step.Confirmed, Answers = state.Answers with { AccessNote = note } };
        return Ask(next, ev, captures: [new AccessNoteCaptured(note), new RsvpCompleted(true)]);
    }

    private EngineResult HandleHelp(ConversationState state, EventDetails ev)
    {
        var ack = new MessagePart(MessageKeys.HelpAck);
        var reset = state with { InvalidStreak = 0 };
        OutboundMessage message = state.Step switch
        {
            // Finished conversations just get the acknowledgement.
            Step.Confirmed or Step.Declined => Reply(ack),
            // Mid-conversation: acknowledge, then repeat the question so they can carry on.
            _ => WithParts(Prompt(reset, ev), ack),
        };
        return new EngineResult(reset, [message], [new NeedsAttention()]);
    }

    private EngineResult HandleChange(ConversationState state, EventDetails ev)
    {
        if (state.Step is not (Step.Confirmed or Step.Declined))
        {
            return Invalid(state, ev, new MessagePart(MessageKeys.ChangeNotYet));
        }

        var restarted = state with
        {
            Step = Step.Invited,
            PersonIndex = 1,
            InvalidStreak = 0,
            Answers = RsvpAnswers.Empty,
        };
        return new EngineResult(
            restarted,
            [WithParts(Prompt(restarted, ev), new MessagePart(MessageKeys.RestartAck))],
            [new AnswersCleared()]);
    }

    /// <summary>Repeats the current question with a hint, and after repeated failures suggests HELP.</summary>
    private EngineResult Invalid(ConversationState state, EventDetails ev, MessagePart hint)
    {
        var next = state with { InvalidStreak = state.InvalidStreak + 1 };
        var prompt = Prompt(next, ev);
        var parts = new List<MessagePart> { hint };
        parts.AddRange(prompt.Parts);
        if (next.InvalidStreak >= InvalidAnswersBeforeHelpHint)
        {
            parts.Add(new MessagePart(MessageKeys.HelpHint));
        }

        return new EngineResult(next, [prompt with { Parts = parts }], []);
    }

    /// <summary>Moves to a new question: resets the invalid streak and sends that question.</summary>
    private EngineResult Ask(ConversationState next, EventDetails ev, OutboundMessage? message = null, IReadOnlyList<Capture>? captures = null)
    {
        next = next with { InvalidStreak = 0 };
        return new EngineResult(next, [message ?? Prompt(next, ev)], captures ?? []);
    }

    private OutboundMessage InvitationMessage(EventDetails ev) =>
        new(
            MessagePurpose.Invitation,
            [
                Part(MessageKeys.Invitation, ("event", ev.Name), ("date", ev.StartsAt), ("venue", ev.Venue)),
                Part(MessageKeys.AttendQuestion, ("event", ev.Name)),
            ],
            Choices(OptionIds.AttendOptions));

    private static OutboundMessage Summary(ConversationState state, EventDetails ev)
    {
        var a = state.Answers;
        var parts = new List<MessagePart>
        {
            Part(
                MessageKeys.ConfirmedSummary,
                ("event", ev.Name),
                ("date", ev.StartsAt),
                ("party", a.PartySize ?? 0),
                ("diets", a.Diets),
                ("wheelchairs", a.WheelchairUsers ?? 0)),
        };
        if (!string.IsNullOrEmpty(a.AccessNote))
        {
            parts.Add(Part(MessageKeys.ConfirmedSummaryNote, ("note", a.AccessNote)));
        }

        parts.Add(new MessagePart(MessageKeys.ChangeHint));
        return new OutboundMessage(MessagePurpose.Reply, parts, Choices([OptionIds.Change]));
    }

    /// <summary>
    /// Resolves the guest's answer to one of <paramref name="optionIds"/>. A button or list id wins;
    /// otherwise typed text is matched case-insensitively against the catalog's word lists, and
    /// (for lists) the option's position, e.g. "2".
    /// </summary>
    private string? MatchChoice(Input input, IReadOnlyList<string> optionIds, bool allowPosition = false)
    {
        if (input.ReplyId is not null)
        {
            // A tap on an option from an earlier question is not a valid answer to this one.
            return optionIds.Contains(input.ReplyId) ? input.ReplyId : null;
        }

        foreach (var id in optionIds)
        {
            if (_catalog.MatchesOption(id, input.Text))
            {
                return id;
            }
        }

        // Some channels send the tapped title as text with no id; match labels too.
        foreach (var id in optionIds)
        {
            if (MessageCatalog.Normalise(_catalog.OptionLabel(id)) == input.Text)
            {
                return id;
            }
        }

        if (allowPosition && _catalog.ParseNumber(input.Text) is { } position && position >= 1 && position <= optionIds.Count)
        {
            return optionIds[position - 1];
        }

        return null;
    }

    private int? MatchNumber(Input input, string prefix)
    {
        if (input.ReplyId is not null)
        {
            return input.ReplyId.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(input.ReplyId.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;
        }

        return _catalog.ParseNumber(input.Text);
    }

    private static MessagePart? ValidateFreeText(Input input, int maxLength)
    {
        if (input.ReplyId is not null || input.Raw.Length == 0)
        {
            return new MessagePart(MessageKeys.InvalidEmpty);
        }

        return new StringInfo(input.Raw).LengthInTextElements > maxLength
            ? Part(MessageKeys.InvalidTooLong, ("max", maxLength))
            : null;
    }

    private static ConversationState Remember(ConversationState state, string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return state;
        }

        var ids = state.RecentMessageIds.Add(messageId);
        if (ids.Count > ConversationState.RecentMessageIdLimit)
        {
            ids = ids.RemoveRange(0, ids.Count - ConversationState.RecentMessageIdLimit);
        }

        return state with { RecentMessageIds = ids };
    }

    private static OutboundMessage Reply(params MessagePart[] parts) => new(MessagePurpose.Reply, parts, []);

    private static OutboundMessage Reply(IReadOnlyList<Choice> choices, params MessagePart[] parts) => new(MessagePurpose.Reply, parts, choices);

    private static OutboundMessage WithParts(OutboundMessage message, MessagePart first) =>
        message with { Parts = [first, .. message.Parts] };

    private static IReadOnlyList<Choice> Choices(IEnumerable<string> ids) => ids.Select(id => new Choice(id)).ToList();

    private static IReadOnlyList<Choice> NumberChoices(string prefix, int max) =>
        Enumerable.Range(1, max).Select(n => new Choice(prefix + n.ToString(CultureInfo.InvariantCulture), n)).ToList();

    private static MessagePart Part(string key, params (string Name, object Value)[] args) =>
        new(key, args.ToImmutableDictionary(a => a.Name, a => a.Value));

    /// <param name="Text">Normalised text used for matching.</param>
    /// <param name="ReplyId">Button or list id, if the guest tapped one.</param>
    /// <param name="Raw">Trimmed original text, kept for free-text answers.</param>
    private sealed record Input(string Text, string? ReplyId, string Raw);
}
