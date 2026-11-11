using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Tests;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+27 82 123 4567", "+27821234567")]
    [InlineData("082 123 4567", "+27821234567")]
    [InlineData("082-123-4567", "+27821234567")]
    [InlineData("(082) 123 4567", "+27821234567")]
    [InlineData("0027821234567", "+27821234567")]
    [InlineData("27821234567", "+27821234567")]
    [InlineData("+44 7700 900123", "+447700900123")]
    public void Valid_numbers_normalise_to_E164(string input, string expected)
    {
        Assert.True(PhoneNumbers.TryNormalise(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("+0821234567")]
    [InlineData("call me")]
    [InlineData("+2782123456789012")]
    public void Invalid_numbers_are_rejected(string? input)
    {
        Assert.False(PhoneNumbers.TryNormalise(input, out _));
    }

    [Fact]
    public void Masking_keeps_country_code_and_last_four()
    {
        Assert.Equal("+27•••••1234", PhoneNumbers.Mask("+27821231234"));
    }
}
