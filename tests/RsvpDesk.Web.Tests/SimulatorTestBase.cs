using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Api;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

/// <summary>Helpers for driving conversations through the simulator API, as the browser does.</summary>
public abstract class SimulatorTestBase(RsvpWebFactory factory) : IClassFixture<RsvpWebFactory>
{
    private static int _phoneCounter;

    protected RsvpWebFactory Factory { get; } = factory;

    /// <summary>Plays the guest's phone; the simulator API needs no login.</summary>
    protected HttpClient Client { get; } = factory.CreateClient();

    private HttpClient? _staff;

    protected async Task<T> WithDbAsync<T>(Func<RsvpDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<RsvpDbContext>());
    }

    /// <summary>A fresh guest on the sample event, so tests never share conversation state.</summary>
    protected Task<int> CreateGuestAsync(bool consent = true) => WithDbAsync(async db =>
    {
        var ev = await db.Events.SingleAsync(e => e.Name == DemoSeeder.SampleEventName);
        var guest = new Guest
        {
            EventId = ev.Id,
            Name = "Test Guest",
            PhoneE164 = $"+2709{Interlocked.Increment(ref _phoneCounter):0000000}",
            ConsentAt = consent ? DateTimeOffset.UtcNow : null,
            ConsentSource = consent ? "Test" : null,
        };
        db.Guests.Add(guest);
        await db.SaveChangesAsync();
        return guest.Id;
    });

    protected async Task InviteAsync(int guestId)
    {
        var response = await Client.PostAsync($"/api/simulator/{guestId}/invite", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    protected async Task<SimulatorMessageResponse> SayAsync(int guestId, string? text, string? replyId = null, string? messageId = null)
    {
        var response = await Client.PostAsJsonAsync($"/api/simulator/{guestId}/messages?wait=true", new SimulatorMessageRequest(messageId, text, replyId));
        Assert.True(response.IsSuccessStatusCode, $"POST failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        var body = (await response.Content.ReadFromJsonAsync<SimulatorMessageResponse>())!;
        Assert.True(body.Processed, "The worker did not process the message in time.");
        return body;
    }

    /// <summary>Reads the guest through the staff API, signed in as an organiser.</summary>
    protected async Task<GuestDetailDto> GetGuestAsync(int guestId)
    {
        _staff ??= await Factory.SignedInClientAsync();
        return (await _staff.GetFromJsonAsync<GuestDetailDto>($"/api/guests/{guestId}"))!;
    }
}
