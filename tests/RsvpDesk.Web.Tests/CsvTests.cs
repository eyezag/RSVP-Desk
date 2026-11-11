using System.Text;
using RsvpDesk.Web.Reports;

namespace RsvpDesk.Web.Tests;

/// <summary>Spec 13 "CSV": commas, quotes, line breaks, accents and formula-looking values.</summary>
public sealed class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("Smith, Jane", "\"Smith, Jane\"")]
    [InlineData("She said \"hi\"", "\"She said \"\"hi\"\"\"")]
    [InlineData("line one\nline two", "\"line one\nline two\"")]
    [InlineData("line one\r\nline two", "\"line one\r\nline two\"")]
    [InlineData("Zoë Mbalí-Ngcobo", "Zoë Mbalí-Ngcobo")]
    public void Values_are_quoted_per_RFC_4180(string? value, string expected)
    {
        Assert.Equal(expected, Csv.Escape(value));
    }

    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+27 82 123", "'+27 82 123")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tTAB", "'\tTAB")]
    [InlineData("\r=1", "\"'\r=1\"")]
    [InlineData("=HYPERLINK(\"http://x\",\"a,b\")", "\"'=HYPERLINK(\"\"http://x\"\",\"\"a,b\"\")\"")]
    [InlineData("a=b", "a=b")]
    public void Formula_looking_values_get_a_leading_apostrophe(string value, string expected)
    {
        Assert.Equal(expected, Csv.Escape(value));
    }

    [Theory]
    [InlineData("+27821234567", "+27821234567")]
    [InlineData("27821234567", "27821234567")]
    [InlineData("+27=1+1", "'+27=1+1")]
    [InlineData("+27 82 123 4567", "'+27 82 123 4567")]
    public void Plain_phone_numbers_are_left_alone(string value, string expected)
    {
        Assert.Equal(expected, Csv.Escape(new Csv.Cell(value, IsPhone: true)));
    }

    [Fact]
    public void Plain_numbers_outside_the_phone_column_are_still_guarded()
    {
        Assert.Equal("'+27821234567", Csv.Escape("+27821234567"));
    }

    [Fact]
    public async Task Output_has_a_BOM_and_CRLF_line_endings()
    {
        using var stream = new MemoryStream();

        await Csv.WriteAsync(stream, ["A", "B"], new[] { (IEnumerable<Csv.Cell>)["1", "x,y"] }.ToAsyncEnumerable(), CancellationToken.None);

        var bytes = stream.ToArray();
        Assert.Equal(Csv.Utf8Bom, bytes[..3]);
        Assert.Equal("A,B\r\n1,\"x,y\"\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }
}
