using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Api;

public sealed record DietDto(int PersonIndex, string Dietary, string? Note);

public sealed record AnswersDto(bool? Attending, int? PartySize, IReadOnlyList<DietDto> Diets, int? WheelchairUsers, string? AccessNote, DateTimeOffset? ConfirmedAt);

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

/// <summary>Staff API. Login is added with the dashboard in build step 4 (decisions D-206).</summary>
public static class GuestEndpoints
{
    public static IEndpointRouteBuilder MapGuestApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/guests/{id:int}", GetGuest).WithTags("Guests");
        return app;
    }

    private static async Task<IResult> GetGuest(int id, RsvpDbContext db, CancellationToken ct)
    {
        var guest = await db.Guests
            .AsNoTracking()
            .Include(g => g.Conversation)
            .Include(g => g.Rsvp).ThenInclude(r => r!.Persons)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guest is null)
        {
            return Results.NotFound();
        }

        var messages = await db.Messages.AsNoTracking().Where(m => m.GuestId == id).OrderBy(m => m.Id).ToListAsync(ct);
        var rsvp = guest.Rsvp;
        var answers = new AnswersDto(
            rsvp?.Attending,
            rsvp?.PartySize,
            rsvp?.Persons.OrderBy(p => p.PersonIndex).Select(p => new DietDto(p.PersonIndex, p.Dietary.ToString(), p.DietaryNote)).ToList() ?? [],
            rsvp?.WheelchairUsers,
            rsvp?.AccessNote,
            rsvp?.ConfirmedAt);

        return Results.Ok(new GuestDetailDto(
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
            messages.Select(SimulatorEndpoints.ToDto).ToList()));
    }
}
