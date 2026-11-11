using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RsvpDesk.Engine;

/// <summary>
/// The language resource: message text, option labels, and the typed words the bot understands.
/// Loaded from an embedded JSON file (Resources/messages.{lang}.json). Immutable once loaded.
/// </summary>
public sealed partial class MessageCatalog
{
    private static readonly Lazy<MessageCatalog> LazyEnglish = new(() => LoadEmbedded("en"));

    private readonly FrozenDictionary<string, string> _messages;
    private readonly FrozenDictionary<string, OptionText> _options;
    private readonly FrozenDictionary<string, FrozenSet<string>> _keywords;
    private readonly FrozenDictionary<string, int> _numberWords;

    private MessageCatalog(CatalogFile file)
    {
        Culture = CultureInfo.GetCultureInfo(file.Culture);
        DateFormat = file.DateFormat;
        _messages = file.Messages.ToFrozenDictionary();
        _options = file.Options.ToFrozenDictionary(
            kv => kv.Key,
            kv => new OptionText(kv.Value.Label, kv.Value.Aliases.Select(Normalise).ToFrozenSet()));
        _keywords = file.Keywords.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.Select(Normalise).ToFrozenSet());
        _numberWords = file.NumberWords.ToFrozenDictionary(kv => Normalise(kv.Key), kv => kv.Value);
    }

    public static MessageCatalog English => LazyEnglish.Value;

    public CultureInfo Culture { get; }

    public string DateFormat { get; }

    public IEnumerable<string> MessageKeysDefined => _messages.Keys;

    public IEnumerable<string> OptionIdsDefined => _options.Keys;

    public static MessageCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        }) ?? throw new InvalidOperationException("Message catalog is empty.");
        return new MessageCatalog(file);
    }

    public static MessageCatalog LoadEmbedded(string language)
    {
        var name = $"RsvpDesk.Engine.Resources.messages.{language}.json";
        using var stream = typeof(MessageCatalog).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Message catalog '{name}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    public string Message(string key) =>
        _messages.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"Message key '{key}' is not in the catalog.");

    public string OptionLabel(string optionId) =>
        _options.TryGetValue(optionId, out var option) ? option.Label : throw new KeyNotFoundException($"Option '{optionId}' is not in the catalog.");

    /// <summary>True if the typed text is one of the words for this option.</summary>
    public bool MatchesOption(string optionId, string normalisedText) =>
        _options.TryGetValue(optionId, out var option) && option.Aliases.Contains(normalisedText);

    /// <summary>True if the typed text is the given keyword (STOP, HELP, CHANGE).</summary>
    public bool IsKeyword(string keyword, string normalisedText) =>
        _keywords.TryGetValue(keyword, out var words) && words.Contains(normalisedText);

    /// <summary>Parses "3" or "three". Returns null for anything else.</summary>
    public int? ParseNumber(string normalisedText)
    {
        if (DigitsOnly().IsMatch(normalisedText))
        {
            return int.TryParse(normalisedText, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
        }

        return _numberWords.TryGetValue(normalisedText, out var word) ? word : null;
    }

    /// <summary>
    /// Lower-case, trim, collapse inner whitespace, and drop surrounding punctuation
    /// so that "Yes!", " YES " and "yes." all read as "yes".
    /// </summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = Whitespace().Replace(text.Trim(), " ").ToLowerInvariant();
        return collapsed.Trim('.', '!', '?', ',', ';', ':', '"', '\'', '“', '”', '‘', '’', ' ');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[0-9]+$")]
    private static partial Regex DigitsOnly();

    private sealed record OptionText(string Label, FrozenSet<string> Aliases);

    private sealed class CatalogFile
    {
        public string Culture { get; set; } = "en";
        public string DateFormat { get; set; } = "f";
        public Dictionary<string, string> Messages { get; set; } = [];
        public Dictionary<string, OptionFile> Options { get; set; } = [];
        public Dictionary<string, string[]> Keywords { get; set; } = [];
        public Dictionary<string, int> NumberWords { get; set; } = [];
    }

    private sealed class OptionFile
    {
        public string Label { get; set; } = "";
        public string[] Aliases { get; set; } = [];
    }
}
