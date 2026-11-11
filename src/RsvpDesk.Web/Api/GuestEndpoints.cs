using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.Guests;

namespace RsvpDesk.Web.Api;

public sealed record DietDto(int PersonIndex, string Dietary, string? Note);

/// <summary>Dietary and access fields are null for Viewers (decisions D-304).</summary>
public sealed record AnswersDto(bool? Attending, int? PartySize, IReadOnlyList<DietDto>? Diets, int? WheelchairUsers, string? AccessNote, DateTimeOffset? ConfirmedAt);

public sealed record GuestDetailDto(
    int Id,
    int EventId,
    string Name,
    string Phone,
    string Status,
    string Step,
    bool NeedsAttention,
    DateTimeOffset? OptedOutAt,
    DateTimeOffset? InviteSentAt,
    DateTimeOffset? LastActivityAt,
    AnswersDto Answers,
    IReadOnlyList<ChatMessageDto> Messages);

public sealed record GuestRowDto(
    int Id,
    string Name,
    string Phone,
    string Status,
    int? PartySize,
    IReadOnlyList<DietDto>? Diets,
    int? WheelchairUsers,
    bool? HasAccessNote,
    bool NeedsAttention,
    bool OptedOut,
    DateTimeOffset? LastActivityAt);

public sealed record GuestPageDto(IReadOnlyList<GuestRowDto> Guests, int Page, int PageSize, int Total);

/// <summary>Staff API (any signed-in staff user).</summary>
public static class GuestEndpoints
{
    public static IEndpointRouteBuilder MapGuestApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization(Policies.Staff).WithTags("Guests");
        group.MapGet("/guests/{id:int}", GetGuest);
        group.MapGet("/events/{id:int}/guests", ListGuests);
        return app;
    }

    private static async Task<IResult> ListGuests(int id, string? status, string? q, int? page, int? pageSize, ClaimsPrincipal user, RsvpDbContext db, GuestQueries queries, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == id, ct))
        {
            return Results.NotFound();
        }

        var size = Math.Clamp(pageSize ?? GuestQueries.DefaultPageSize, 1, 100);
        var result = await queries.SearchAsync(id, status, q, page ?? 1, size, ct);
        var organiser = user.IsOrganiser();
        return Results.Ok(new GuestPageDto(
            result.Rows.Select(r => new GuestRowDto(
                r.Id,
                r.Name,
                r.Phone,
                r.Status.ToString(),
                r.PartySize,
                organiser ? r.Diets.Select(d => new DietDto(d.PersonIndex, d.Dietary.ToString(), d.Note)).ToList() : null,
                organiser ? r.WheelchairUsers : null,
                organiser ? r.HasAccessNote : null,
                r.NeedsAttention,
                r.OptedOut,
                r.LastActivityAt)).ToList(),
            result.Page,
            result.PageSize,
            result.Total));
    }

    private static async Task<IResult> GetGuest(int id, ClaimsPrincipal user, RsvpDbContext db, AuditLog audit, CancellationToken ct)
    {
        var organiser = user.IsOrganiser();
        var detail = await BuildDetailAsync(db, id, includeSensitive: organiser, ct);
        if (detail is null)
        {
            return Results.NotFound();
        }

        if (organiser)
        {
            await audit.WriteAsync(user.UserId(), "GuestViewed", $"guest:{id}", ct);
        }

        return Results.Ok(detail);
    }

    /// <summary>
    /// Guest, captured answers and conversation. Without <paramref name="includeSensitive"/> the
    /// dietary and access answers and the conversation (which repeats them) are left out.
    /// </summary>
    internal static async Task<GuestDetailDto?> BuildDetailAsync(RsvpDbContext db, int id, bool includeSensitive, CancellationToken ct)
    {
        var guest = await db.Guests
            .AsNoTracking()
            .Include(g => g.Conversation)
            .Include(g => g.Rsvp).ThenInclude(r => r!.Persons)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guest is null)
        {
            return null;
        }

        var messages = includeSensitive
            ? await db.Messages.AsNoTracking().Where(m => m.GuestId == id).OrderBy(m => m.Id).ToListAsync(ct)
            : [];
        var rsvp = guest.Rsvp;
        var answers = new AnswersDto(
            rsvp?.Attending,
            rsvp?.PartySize,
            includeSensitive ? rsvp?.Persons.OrderBy(p => p.PersonIndex).Select(p => new DietDto(p.PersonIndex, p.Dietary.ToString(), p.DietaryNote)).ToList() ?? [] : null,
            includeSensitive ? rsvp?.WheelchairUsers : null,
            includeSensitive ? rsvp?.AccessNote : null,
            rsvp?.ConfirmedAt);

        return new GuestDetailDto(
            guest.Id,
            guest.EventId,
            guest.Name,
            guest.PhoneE164,
            guest.Status.ToString(),
            (guest.Conversation?.State ?? Step.NotInvited).ToString(),
            guest.NeedsAttention,
            guest.OptedOutAt,
            guest.InviteSentAt,
            guest.LastActivityAt,
            answers,
            messages.Select(SimulatorEndpoints.ToDto).ToList());
    }
}
