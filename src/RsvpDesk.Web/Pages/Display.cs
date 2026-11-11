using System.Globalization;
using RsvpDesk.Engine;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Pages;

/// <summary>Formatting shared by the dashboard pages.</summary>
public static class Display
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-ZA");
    private static readonly MessageRenderer Renderer = new();

    public static string StatusLabel(GuestStatus status) => status switch
    {
        GuestStatus.NotInvited => "Not invited",
        GuestStatus.Invited => "Invited",
        GuestStatus.InChat => "In chat",
        GuestStatus.Attending => "Attending",
        GuestStatus.Declined => "Declined",
        _ => status.ToString(),
    };

    public static string StatusCss(GuestStatus status) => "pill pill-" + status.ToString().ToLowerInvariant();

    public static string DietaryLabel(Dietary dietary) => Renderer.DietaryLabel(dietary);

    /// <summary>"Vegetarian ×2, Halal" (notes for Other are on the detail page and the report).</summary>
    public static string DietSummary(IReadOnlyList<RsvpPersonView> diets) =>
        diets.Count == 0
            ? "–"
            : string.Join(", ", diets.GroupBy(d => d.Dietary).OrderBy(g => g.Min(d => d.PersonIndex))
                .Select(g => g.Count() > 1 ? $"{DietaryLabel(g.Key)} ×{g.Count()}" : DietaryLabel(g.Key)));

    /// <summary>A time in the event's time zone, e.g. "Sat 5 Dec 2026, 18:00".</summary>
    public static string LocalTime(DateTimeOffset? value, string timeZoneId, string format = "ddd d MMM yyyy, HH:mm")
    {
        if (value is null)
        {
            return "–";
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTime(value.Value, zone).ToString(format, Culture);
    }

    public static string Percent(int part, int whole) =>
        whole == 0 ? "0%" : (part / (double)whole).ToString("P0", Culture);
}
