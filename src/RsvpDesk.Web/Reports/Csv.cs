using System.Text;
using System.Text.RegularExpressions;

namespace RsvpDesk.Web.Reports;

/// <summary>
/// CSV for spreadsheets (spec section 10): RFC 4180 quoting, CRLF line endings, UTF-8 with a BOM
/// (so Excel reads accents correctly), and protection against formula injection.
/// </summary>
public static partial class Csv
{
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    public const string LineEnding = "\r\n";

    /// <summary>A cell value, and whether it is a phone number column (exempt from the formula guard when it is a plain number).</summary>
    public readonly record struct Cell(string? Value, bool IsPhone = false)
    {
        public static implicit operator Cell(string? value) => new(value);
    }

    public static string Row(IEnumerable<Cell> cells) => string.Join(",", cells.Select(Escape)) + LineEnding;

    public static string Escape(Cell cell)
    {
        var value = cell.Value ?? string.Empty;
        if (NeedsFormulaGuard(value) && !(cell.IsPhone && PlainNumber().IsMatch(value)))
        {
            // A leading apostrophe makes Excel and Sheets treat the cell as text, not a formula.
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static bool NeedsFormulaGuard(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    /// <summary>Writes the BOM and then rows as they come, so large lists are never held in memory.</summary>
    public static async Task WriteAsync(Stream output, IEnumerable<Cell> header, IAsyncEnumerable<IEnumerable<Cell>> rows, CancellationToken ct)
    {
        await output.WriteAsync(Utf8Bom, ct);
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16 * 1024, leaveOpen: true);
        await writer.WriteAsync(Row(header));
        await foreach (var row in rows.WithCancellation(ct))
        {
            await writer.WriteAsync(Row(row));
        }

        await writer.FlushAsync(ct);
    }

    [GeneratedRegex(@"^\+?[0-9]{4,15}$")]
    private static partial Regex PlainNumber();
}
