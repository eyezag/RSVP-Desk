using System.Security.Cryptography;
using System.Text;

namespace RsvpDesk.Web.WhatsApp;

/// <summary>X-Hub-Signature-256: "sha256=" + hex HMAC-SHA256 of the raw body, keyed with the app secret.</summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-Hub-Signature-256";
    private const string Prefix = "sha256=";

    public static bool IsValid(ReadOnlySpan<byte> body, string? header, string appSecret)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] received;
        try
        {
            received = Convert.FromHexString(header.AsSpan(Prefix.Length).Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return received.Length == expected.Length && CryptographicOperations.FixedTimeEquals(received, expected);
    }

    public static string Compute(ReadOnlySpan<byte> body, string appSecret) =>
        Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

    /// <summary>Constant-time string comparison for the verify token.</summary>
    public static bool TokensMatch(string? received, string expected) =>
        received is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));
}
