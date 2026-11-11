using Microsoft.EntityFrameworkCore;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Guests;

public sealed record ImportProblem(int Row, string Reason);

public sealed record ImportResult(int Added, IReadOnlyList<ImportProblem> Problems, string? FileError = null);

/// <summary>
/// Imports guests from CSV. Columns (header row required, any order, case-insensitive):
/// <c>name</c>, <c>phone</c>, optional <c>consent</c> (yes/no) and <c>consent_source</c>.
/// Rows with problems are skipped and reported; the rest are added.
/// </summary>
public sealed class GuestImporter(RsvpDbContext db, AuditLog audit, TimeProvider clock)
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxRows = 5000;

    private static readonly string[] Yes = ["yes", "y", "true", "1", "x"];
    private static readonly string[] No = ["no", "n", "false", "0", ""];

    public async Task<ImportResult> ImportAsync(int eventId, string csv, bool defaultConsent, string? defaultConsentSource, int? userId, CancellationToken ct)
    {
        var rows = CsvReader.Parse(csv);
        if (rows.Count < 2)
        {
            return new ImportResult(0, [], "The file needs a header row and at least one guest.");
        }

        if (rows.Count - 1 > MaxRows)
        {
            return new ImportResult(0, [], $"At most {MaxRows} guests per file.");
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant().Replace(' ', '_')).ToList();
        int Col(string name) => header.IndexOf(name);
        var (nameCol, phoneCol, consentCol, sourceCol) = (Col("name"), Col("phone"), Col("consent"), Col("consent_source"));
        if (nameCol < 0 || phoneCol < 0)
        {
            return new ImportResult(0, [], "The header row must include \"name\" and \"phone\" columns.");
        }

        if (defaultConsent && string.IsNullOrWhiteSpace(defaultConsentSource))
        {
            return new ImportResult(0, [], "Say how these guests gave consent.");
        }

        var existing = (await db.Guests.Where(g => g.EventId == eventId).Select(g => g.PhoneE164).ToListAsync(ct)).ToHashSet();
        var problems = new List<ImportProblem>();
        var now = clock.GetUtcNow();
        var added = 0;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var rowNumber = r + 1; // as a spreadsheet shows it
            string Get(int col) => col >= 0 && col < row.Count ? row[col].Trim() : "";

            var name = Get(nameCol);
            if (name.Length == 0 || name.Length > GuestCommands.NameMaxLength)
            {
                problems.Add(new ImportProblem(rowNumber, "Missing or too-long name."));
                continue;
            }

            if (!PhoneNumbers.TryNormalise(Get(phoneCol), out var phone))
            {
                problems.Add(new ImportProblem(rowNumber, "Not a valid mobile number."));
                continue;
            }

            if (!existing.Add(phone))
            {
                problems.Add(new ImportProblem(rowNumber, "This number is already on the guest list."));
                continue;
            }

            var consent = defaultConsent;
            var source = defaultConsentSource?.Trim();
            if (consentCol >= 0)
            {
                var value = Get(consentCol).ToLowerInvariant();
                if (Yes.Contains(value))
                {
                    consent = true;
                }
                else if (No.Contains(value))
                {
                    consent = value.Length == 0 && defaultConsent;
                }
                else
                {
                    existing.Remove(phone);
                    problems.Add(new ImportProblem(rowNumber, "Consent must be yes or no."));
                    continue;
                }

                if (Get(sourceCol) is { Length: > 0 } rowSource)
                {
                    source = rowSource.Length > GuestCommands.ConsentSourceMaxLength ? rowSource[..GuestCommands.ConsentSourceMaxLength] : rowSource;
                }
            }

            if (consent && string.IsNullOrWhiteSpace(source))
            {
                source = "CSV import";
            }

            db.Guests.Add(new Guest
            {
                EventId = eventId,
                Name = name,
                PhoneE164 = phone,
                ConsentAt = consent ? now : null,
                ConsentSource = consent ? source : null,
            });
            added++;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "GuestsImported", $"event:{eventId} added:{added} skipped:{problems.Count}", ct);
        return new ImportResult(added, problems);
    }
}
