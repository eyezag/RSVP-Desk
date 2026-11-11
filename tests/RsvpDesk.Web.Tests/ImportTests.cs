using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Tests;

public sealed class CsvReaderTests
{
    [Fact]
    public void Parses_quotes_commas_line_breaks_BOM_and_blank_lines()
    {
        var rows = CsvReader.Parse("﻿name,phone\r\n\"Smith, John\",082\r\n\r\n\"Say \"\"hi\"\"\nthere\",083\nlast,084");

        Assert.Equal(4, rows.Count);
        Assert.Equal(["name", "phone"], rows[0]);
        Assert.Equal(["Smith, John", "082"], rows[1]);
        Assert.Equal(["Say \"hi\"\nthere", "083"], rows[2]);
        Assert.Equal(["last", "084"], rows[3]);
    }
}

public sealed class ImportTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    private async Task<int> NewEventAsync() => await WithDbAsync(async db =>
    {
        var ev = new Event { Name = "Import test", StartsAt = DateTimeOffset.UtcNow.AddDays(30), TimeZoneId = "Africa/Johannesburg", Venue = "Hall", Capacity = 100, CreatedAt = DateTimeOffset.UtcNow };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    });

    private async Task<string> UploadAsync(int eventId, string csv, bool allConsented = false, string? source = null)
    {
        var staff = await Factory.SignedInClientAsync();
        var token = await staff.AntiforgeryTokenAsync($"/Events/{eventId}/Guests/Import");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "Upload", "guests.csv" },
        };
        if (allConsented)
        {
            form.Add(new StringContent("true"), "AllConsented");
            form.Add(new StringContent(source ?? ""), "ConsentSource");
        }

        var response = await staff.PostAsync($"/Events/{eventId}/Guests/Import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Imports_valid_rows_and_reports_the_rest()
    {
        var eventId = await NewEventAsync();
        var csv = "Name,Phone,Consent,Consent Source\r\n"
            + "Jane Example,082 555 0100,yes,Registration form\r\n"
            + "\"Smith, John\",+27 82 555 0101,no,\r\n"
            + "Bad Number,12345,yes,x\r\n"
            + "Dup,0825550100,yes,x\r\n"
            + ",0825550199,yes,x\r\n"
            + "Odd Consent,0825550102,maybe,x\r\n"
            + "Zoë Ngcobo,0825550103,,\r\n";

        var html = await UploadAsync(eventId, csv);

        Assert.Contains("Added 3 guests.", html);
        Assert.Contains("Row 4: Not a valid mobile number.", html);
        Assert.Contains("Row 5: This number is already on the guest list.", html);
        Assert.Contains("Row 6: Missing or too-long name.", html);
        Assert.Contains("Row 7: Consent must be yes or no.", html);

        var guests = await WithDbAsync(db => db.Guests.Where(g => g.EventId == eventId).OrderBy(g => g.Name).ToListAsync());
        Assert.Equal(["Jane Example", "Smith, John", "Zoë Ngcobo"], guests.Select(g => g.Name));
        Assert.NotNull(guests[0].ConsentAt);
        Assert.Equal("Registration form", guests[0].ConsentSource);
        Assert.Null(guests[1].ConsentAt);
        Assert.Null(guests[2].ConsentAt);
        Assert.Equal("+27825550103", guests[2].PhoneE164);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "GuestsImported" && a.Target.StartsWith($"event:{eventId} added:3"))));
    }

    [Fact]
    public async Task Whole_file_consent_applies_to_rows_without_a_consent_column()
    {
        var eventId = await NewEventAsync();

        var html = await UploadAsync(eventId, "name,phone\nA Person,0825550200\n", allConsented: true, source: "Signed list, 1 Nov");

        Assert.Contains("Added 1 guest.", html);
        var guest = await WithDbAsync(db => db.Guests.SingleAsync(g => g.EventId == eventId));
        Assert.Equal("Signed list, 1 Nov", guest.ConsentSource);
    }

    [Theory]
    [InlineData("phone\n0825550300\n", "must include")]
    [InlineData("name,phone\n", "at least one guest")]
    public async Task Bad_files_are_rejected(string csv, string expected)
    {
        var eventId = await NewEventAsync();

        Assert.Contains(expected, await UploadAsync(eventId, csv));
    }
}
