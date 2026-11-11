using System.Text.RegularExpressions;

namespace RsvpDesk.Web.Guests;

/// <summary>Normalises phone numbers to E.164 (+27821234567).</summary>
public static partial class PhoneNumbers
{
    /// <summary>Country calling code assumed for numbers written with a leading 0 (South Africa).</summary>
    public const string DefaultCountryCode = "27";

    public static bool TryNormalise(string? input, out string e164)
    {
        e164 = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var compact = Separators().Replace(input.Trim(), "");
        if (compact.StartsWith("00", StringComparison.Ordinal))
        {
            compact = "+" + compact[2..];
        }
        else if (compact.StartsWith('0'))
        {
            compact = "+" + DefaultCountryCode + compact[1..];
        }
        else if (!compact.StartsWith('+'))
        {
            compact = "+" + compact;
        }

        if (!E164().IsMatch(compact))
        {
            return false;
        }

        e164 = compact;
        return true;
    }

    /// <summary>"+27 •••• ••45 67" style masking for logs and low-privilege views.</summary>
    public static string Mask(string? e164) =>
        string.IsNullOrEmpty(e164) || e164.Length < 6 ? "•••" : e164[..3] + new string('•', e164.Length - 7) + e164[^4..];

    [GeneratedRegex(@"[\s\-\.\(\)/]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164();
}
