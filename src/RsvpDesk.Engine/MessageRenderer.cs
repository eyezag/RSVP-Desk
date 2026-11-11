using System.Globalization;
using System.Text.RegularExpressions;

namespace RsvpDesk.Engine;

public sealed record RenderedChoice(string Id, string Title);

public sealed record RenderedMessage(MessagePurpose Purpose, string Body, IReadOnlyList<RenderedChoice> Choices);

/// <summary>
/// Turns an <see cref="OutboundMessage"/> into text using the catalog. Channels call this;
/// the engine does not. Parts are joined with a blank line.
/// </summary>
public sealed partial class MessageRenderer
{
    private readonly MessageCatalog _catalog;

    public MessageRenderer(MessageCatalog catalog) => _catalog = catalog;

    public MessageRenderer() : this(MessageCatalog.English)
    {
    }

    public RenderedMessage Render(OutboundMessage message)
    {
        var body = string.Join("\n\n", message.Parts.Select(RenderPart));
        var choices = message.Choices.Select(c => new RenderedChoice(c.Id, ChoiceTitle(c))).ToList();
        return new RenderedMessage(message.Purpose, body, choices);
    }

    public string ChoiceTitle(Choice choice) =>
        choice.Number is { } n
            ? Fill(_catalog.Message(MessageKeys.NumberOption), new Dictionary<string, object> { ["n"] = n })
            : _catalog.OptionLabel(choice.Id);

    public string ListButtonTitle => _catalog.Message(MessageKeys.ListButton);

    public string ListSectionTitle => _catalog.Message(MessageKeys.ListSectionTitle);

    /// <summary>The event date as the catalog formats it in messages, e.g. "Saturday 5 December 2026 at 18:00".</summary>
    public string FormatDate(DateTimeOffset value) => value.ToString(_catalog.DateFormat, _catalog.Culture);

    public string DietaryLabel(Dietary dietary) => _catalog.OptionLabel(OptionIds.ForDietary(dietary));

    public string RenderPart(MessagePart part) => Fill(_catalog.Message(part.Key), part.Args);

    private string Fill(string template, IReadOnlyDictionary<string, object> args) =>
        Placeholder().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            return args.TryGetValue(name, out var value)
                ? Format(value)
                : throw new InvalidOperationException($"Message is missing the argument '{name}'.");
        });

    private string Format(object value) => value switch
    {
        string s => s,
        DateTimeOffset d => d.ToString(_catalog.DateFormat, _catalog.Culture),
        IEnumerable<DietAnswer> diets => FormatDiets(diets.ToList()),
        IFormattable f => f.ToString(null, _catalog.Culture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>"Vegetarian (2), No requirements (1), Other: nut allergy".</summary>
    private string FormatDiets(IReadOnlyList<DietAnswer> diets)
    {
        var items = new List<string>();
        foreach (var group in diets.Where(d => d.Dietary != Dietary.Other).GroupBy(d => d.Dietary).OrderBy(g => g.Min(d => d.PersonIndex)))
        {
            items.Add(Fill(
                _catalog.Message(MessageKeys.DietSummaryItem),
                new Dictionary<string, object> { ["label"] = DietaryLabel(group.Key), ["count"] = group.Count() }));
        }

        foreach (var other in diets.Where(d => d.Dietary == Dietary.Other))
        {
            items.Add(Fill(
                _catalog.Message(MessageKeys.DietSummaryOther),
                new Dictionary<string, object> { ["label"] = DietaryLabel(Dietary.Other), ["note"] = other.Note ?? string.Empty }));
        }

        return string.Join(_catalog.Message(MessageKeys.DietSummarySeparator), items);
    }

    [GeneratedRegex(@"\{([a-zA-Z]+)\}")]
    private static partial Regex Placeholder();
}
