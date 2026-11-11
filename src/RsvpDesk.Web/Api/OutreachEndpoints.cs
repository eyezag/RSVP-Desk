using Microsoft.AspNetCore.Antiforgery;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Conversations;

namespace RsvpDesk.Web.Api;

/// <summary>
/// POST /api/events/{id}/invitations and /reminders. Organisers only. Because the session is a
/// cookie, these also require the antiforgery token in the RequestVerificationToken header.
/// </summary>
public static class OutreachEndpoints
{
    public static IEndpointRouteBuilder MapOutreachApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events/{id:int}")
            .RequireAuthorization(Policies.Organiser)
            .AddEndpointFilter(ValidateAntiforgery)
            .WithTags("Outreach");
        group.MapPost("/invitations", async (int id, HttpContext http, OutreachService outreach, CancellationToken ct) =>
            await outreach.SendInvitationsAsync(id, http.User.UserId(), ct) is { } r ? Results.Ok(r) : Results.NotFound());
        group.MapPost("/reminders", async (int id, HttpContext http, OutreachService outreach, CancellationToken ct) =>
            await outreach.SendRemindersAsync(id, http.User.UserId(), ct) is { } r ? Results.Ok(r) : Results.NotFound());
        return app;
    }

    private static async ValueTask<object?> ValidateAntiforgery(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing or invalid antiforgery token.");
        }

        return await next(context);
    }
}
