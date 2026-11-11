namespace RsvpDesk.Engine;

/// <summary>Message keys. The text for each lives in Resources/messages.en.json.</summary>
public static class MessageKeys
{
    public const string Invitation = "invitation";
    public const string AttendQuestion = "attend_question";
    public const string PartyQuestion = "party_question";
    public const string DietQuestionSelf = "diet_question_self";
    public const string DietQuestionGuest = "diet_question_guest";
    public const string DietOtherQuestion = "diet_other_question";
    public const string WheelchairQuestion = "wheelchair_question";
    public const string WheelchairCountQuestion = "wheelchair_count_question";
    public const string NoteQuestion = "note_question";
    public const string ConfirmedSummary = "confirmed_summary";
    public const string ConfirmedSummaryNote = "confirmed_summary_note";
    public const string ChangeHint = "change_hint";
    public const string Declined = "declined";
    public const string AlreadyConfirmed = "already_confirmed";
    public const string AlreadyDeclined = "already_declined";
    public const string RestartAck = "restart_ack";
    public const string OptedOut = "opted_out";
    public const string HelpAck = "help_ack";
    public const string HelpHint = "help_hint";
    public const string InvalidChoice = "invalid_choice";
    public const string InvalidNumber = "invalid_number";
    public const string InvalidTooLong = "invalid_too_long";
    public const string InvalidEmpty = "invalid_empty";
    public const string ChangeNotYet = "change_not_yet";
    public const string ReminderNotReplied = "reminder_not_replied";
    public const string ReminderUnfinished = "reminder_unfinished";

    // Renderer-only keys.
    public const string DietSummaryItem = "diet_summary_item";
    public const string DietSummaryOther = "diet_summary_other";
    public const string DietSummarySeparator = "diet_summary_separator";
    public const string NumberOption = "number_option";
    public const string ListButton = "list_button";
}

/// <summary>Stable ids for tappable options. These are sent to WhatsApp as button and list row ids.</summary>
public static class OptionIds
{
    public const string AttendYes = "attend.yes";
    public const string AttendNo = "attend.no";

    public const string DietNone = "diet.none";
    public const string DietVegetarian = "diet.vegetarian";
    public const string DietVegan = "diet.vegan";
    public const string DietHalal = "diet.halal";
    public const string DietGlutenFree = "diet.gluten_free";
    public const string DietOther = "diet.other";

    public const string WheelchairYes = "wheelchair.yes";
    public const string WheelchairNo = "wheelchair.no";

    public const string NoteSkip = "note.skip";

    public const string Change = "change";

    /// <summary>Prefix for party size options: <c>party.1</c> to <c>party.{max}</c>.</summary>
    public const string PartyPrefix = "party.";

    /// <summary>Prefix for wheelchair count options: <c>wheelchairs.1</c> to <c>wheelchairs.{party size}</c>.</summary>
    public const string WheelchairCountPrefix = "wheelchairs.";

    public static readonly IReadOnlyList<string> AttendOptions = [AttendYes, AttendNo];
    public static readonly IReadOnlyList<string> WheelchairOptions = [WheelchairYes, WheelchairNo];

    public static readonly IReadOnlyList<(string Id, Dietary Dietary)> DietOptions =
    [
        (DietNone, Dietary.None),
        (DietVegetarian, Dietary.Vegetarian),
        (DietVegan, Dietary.Vegan),
        (DietHalal, Dietary.Halal),
        (DietGlutenFree, Dietary.GlutenFree),
        (DietOther, Dietary.Other),
    ];

    public static string ForDietary(Dietary dietary) => DietOptions.First(o => o.Dietary == dietary).Id;

    /// <summary>Every fixed (non-numeric) option id, for catalog completeness checks.</summary>
    public static IEnumerable<string> AllFixed() =>
        AttendOptions.Concat(WheelchairOptions).Concat(DietOptions.Select(o => o.Id)).Append(NoteSkip).Append(Change);
}

public static class Keywords
{
    public const string Stop = "stop";
    public const string Help = "help";
    public const string Change = "change";
}
