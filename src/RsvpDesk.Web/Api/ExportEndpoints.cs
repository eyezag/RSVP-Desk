using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Reports;

namespace RsvpDesk.Web.Api;

/// <summary>CSV downloads (spec section 10). Organisers only; every export is audited.</summary>
public static class ExportEndpoints
{
    private static readonly MessageRenderer Renderer = new();

    public static IEndpointRouteBuilder MapExportApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events/{id:int}/export").RequireAuthorization(Policies.Organiser).WithTags("Exports");
        group.MapGet("/guests.csv", GuestsCsv);
        group.MapGet("/catering.csv", CateringCsv);
        return app;
    }

    private static async Task GuestsCsv(int id, HttpContext http, RsvpDbContext db, AuditLog audit, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await audit.WriteAsync(http.User.UserId(), "ExportGuests", $"event:{id}", ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId);

        var rows = db.Guests.AsNoTracking()
            .Where(g => g.EventId == id)
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new
            {
                g.Name,
                g.PhoneE164,
                g.Status,
                OptedOut = g.OptedOutAt != null,
                PartySize = g.Rsvp == null ? null : g.Rsvp.PartySize,
                Persons = g.Rsvp == null ? null : g.Rsvp.Persons.OrderBy(p => p.PersonIndex).Select(p => new { p.PersonIndex, p.Dietary, p.DietaryNote }).ToList(),
                Wheelchairs = g.Rsvp == null ? null : g.Rsvp.WheelchairUsers,
                Note = g.Rsvp == null ? null : g.Rsvp.AccessNote,
                g.LastActivityAt,
            })
            .AsAsyncEnumerable()
            .Select(g => (IEnumerable<Csv.Cell>)
            [
                g.Name,
                new Csv.Cell(g.PhoneE164, IsPhone: true),
                g.Status + (g.OptedOut ? " (opted out)" : ""),
                g.PartySize?.ToString(CultureInfo.InvariantCulture),
                g.Persons is null ? "" : string.Join("; ", g.Persons.Select(p =>
                    $"{p.PersonIndex}: {Renderer.DietaryLabel(p.Dietary)}{(p.DietaryNote is null ? "" : $" ({p.DietaryNote})")}")),
                g.Wheelchairs?.ToString(CultureInfo.InvariantCulture),
                g.Note,
                g.LastActivityAt is null ? "" : TimeZoneInfo.ConvertTime(g.LastActivityAt.Value, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ]);

        SetHeaders(http, FileName(ev, "guests"));
        await Csv.WriteAsync(
            http.Response.Body,
            ["Name", "Phone", "Status", "Party size", "Dietary requirements", "Wheelchair users", "Access note", $"Last activity ({ev.TimeZoneId})"],
            rows,
            ct);
    }

    private static async Task CateringCsv(int id, HttpContext http, RsvpDbContext db, CateringReport report, AuditLog audit, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await audit.WriteAsync(http.User.UserId(), "ExportCatering", $"event:{id}", ct);
        var data = await report.BuildAsync(id, ct);
        var rows = data.Diets.Select(d => (IEnumerable<Csv.Cell>)
        [
            Renderer.DietaryLabel(d.Dietary),
            d.People.ToString(CultureInfo.InvariantCulture),
            d.Dietary == Dietary.Other
                ? string.Join("; ", data.Others.Select(o => $"{o.Note} ({o.GuestName}{(o.PersonIndex > 1 ? $", guest {o.PersonIndex}" : "")})"))
                : "",
        ]).Append(
        [
            "Total (confirmed attending only)",
            data.ConfirmedPeople.ToString(CultureInfo.InvariantCulture),
            $"{data.ConfirmedParties} parties",
        ]);

        SetHeaders(http, FileName(ev, "catering"));
        await Csv.WriteAsync(http.Response.Body, ["Requirement", "Number of people", "Details for Other"], rows.ToAsyncEnumerable(), ct);
    }

    private static void SetHeaders(HttpContext http, string fileName)
    {
        http.Response.ContentType = "text/csv; charset=utf-8";
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        http.Response.Headers.CacheControl = "no-store";
    }

    private static string FileName(Event ev, string kind)
    {
        var slug = new string(ev.Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return $"{(slug.Length == 0 ? "event" : slug)}-{kind}.csv";
    }
}
