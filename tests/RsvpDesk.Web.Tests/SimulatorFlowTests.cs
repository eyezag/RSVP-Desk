using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RsvpDesk.Engine;
using RsvpDesk.Web.Api;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

/// <summary>Spec section 13 "Integration": whole conversations through the simulator API.</summary>
public sealed class SimulatorFlowTests(RsvpWebFactory factory) : SimulatorTestBase(factory)
{
    [Fact]
    public async Task Full_RSVP_through_the_simulator_saves_every_answer()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        Assert.Equal("Invited", (await GetGuestAsync(id)).Status);

        await SayAsync(id, "Yes, I'll be there", OptionIds.AttendYes);
        Assert.Equal("InChat", (await GetGuestAsync(id)).Status);
        await SayAsync(id, "3");
        await SayAsync(id, "Vegetarian", OptionIds.DietVegetarian);
        await SayAsync(id, "Other", OptionIds.DietOther);
        await SayAsync(id, "Severe nut allergy");
        await SayAsync(id, "halal");
        await SayAsync(id, "yes");
        await SayAsync(id, "2", "wheelchairs.2");
        await SayAsync(id, "Step-free route please");

        var guest = await GetGuestAsync(id);
        Assert.Equal("Attending", guest.Status);
        Assert.Equal("Confirmed", guest.Step);
        Assert.True(guest.Answers.Attending);
        Assert.Equal(3, guest.Answers.PartySize);
        Assert.Equal(
            [new DietDto(1, "Vegetarian", null), new DietDto(2, "Other", "Severe nut allergy"), new DietDto(3, "Halal", null)],
            guest.Answers.Diets);
        Assert.Equal(2, guest.Answers.WheelchairUsers);
        Assert.Equal("Step-free route please", guest.Answers.AccessNote);
        Assert.NotNull(guest.Answers.ConfirmedAt);
        Assert.NotNull(guest.InviteSentAt);
        Assert.NotNull(guest.LastActivityAt);

        // 1 invitation + 9 guest messages, each answered once.
        Assert.Equal(19, guest.Messages.Count);
        Assert.Equal(9, guest.Messages.Count(m => m.Direction == "In"));
        Assert.All(guest.Messages.Where(m => m.Direction == "Out"), m => Assert.Equal("Sent", m.DeliveryStatus));
        var last = guest.Messages[^1];
        Assert.Contains("Party size: 3", last.Body);
        Assert.Equal([OptionIds.Change], last.Choices.Select(c => c.Id));
    }

    [Fact]
    public async Task Declined_RSVP()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);

        await SayAsync(id, "no");

        var guest = await GetGuestAsync(id);
        Assert.Equal("Declined", guest.Status);
        Assert.False(guest.Answers.Attending);
        Assert.NotNull(guest.Answers.ConfirmedAt);
        Assert.Contains("Sorry you can't make it", guest.Messages[^1].Body);
    }

    [Fact]
    public async Task CHANGE_after_confirming_clears_saved_answers_and_restarts()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        foreach (var text in new[] { "yes", "2", "vegan", "none", "no", "skip" })
        {
            await SayAsync(id, text);
        }

        Assert.Equal("Attending", (await GetGuestAsync(id)).Status);

        await SayAsync(id, "CHANGE");
        var restarted = await GetGuestAsync(id);
        Assert.Equal("InChat", restarted.Status);
        Assert.Equal("Invited", restarted.Step);
        Assert.Null(restarted.Answers.Attending);
        Assert.Null(restarted.Answers.PartySize);
        Assert.Empty(restarted.Answers.Diets!);
        Assert.Null(restarted.Answers.ConfirmedAt);

        await SayAsync(id, "no");
        Assert.Equal("Declined", (await GetGuestAsync(id)).Status);
    }

    [Fact]
    public async Task Partial_answers_are_saved_when_the_guest_stops_half_way()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "yes");
        await SayAsync(id, "2");
        await SayAsync(id, "gluten free");

        var guest = await GetGuestAsync(id);
        Assert.Equal("InChat", guest.Status);
        Assert.Equal("Diet", guest.Step);
        Assert.Equal(2, guest.Answers.PartySize);
        Assert.Equal([new DietDto(1, "GlutenFree", null)], guest.Answers.Diets);
        Assert.Null(guest.Answers.ConfirmedAt);
    }

    [Fact]
    public async Task STOP_opts_out_keeps_answers_and_blocks_invitations()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "yes");
        await SayAsync(id, "STOP");
        await SayAsync(id, "hello?");

        var guest = await GetGuestAsync(id);
        Assert.NotNull(guest.OptedOutAt);
        Assert.True(guest.Answers.Attending);
        Assert.Contains("won't receive any more messages", guest.Messages.Last(m => m.Direction == "Out").Body);
        Assert.Equal("In", guest.Messages[^1].Direction); // no reply to "hello?"

        var invite = await Client.PostAsync($"/api/simulator/{id}/invite", null);
        Assert.Equal(HttpStatusCode.Conflict, invite.StatusCode);
        Assert.Contains("OptedOut", await invite.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HELP_marks_the_guest_as_needing_attention()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);

        await SayAsync(id, "help");

        var guest = await GetGuestAsync(id);
        Assert.True(guest.NeedsAttention);
        Assert.Contains("Someone will get back to you", guest.Messages[^1].Body);
    }

    [Fact]
    public async Task Guests_without_consent_are_not_invited()
    {
        var id = await CreateGuestAsync(consent: false);

        var response = await Client.PostAsync($"/api/simulator/{id}/invite", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty((await GetGuestAsync(id)).Messages);
    }

    [Fact]
    public async Task Reset_puts_the_guest_back_to_not_invited()
    {
        var id = await CreateGuestAsync();
        await InviteAsync(id);
        await SayAsync(id, "yes");
        await SayAsync(id, "stop");

        var response = await Client.PostAsync($"/api/simulator/{id}/reset", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var guest = await GetGuestAsync(id);
        Assert.Equal("NotInvited", guest.Status);
        Assert.Equal("NotInvited", guest.Step);
        Assert.Null(guest.OptedOutAt);
        Assert.Empty(guest.Messages);
        Assert.Null(guest.Answers.Attending);
        await InviteAsync(id);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"text\":\"   \"}")]
    [InlineData("{\"text\":\"hi\",\"messageId\":\"bad id with spaces\"}")]
    public async Task Invalid_simulator_requests_are_rejected(string json)
    {
        var id = await CreateGuestAsync();

        var response = await Client.PostAsync($"/api/simulator/{id}/messages", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_guests_are_404()
    {
        var response = await Client.PostAsJsonAsync("/api/simulator/999999/messages", new SimulatorMessageRequest(null, "hi", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var staff = await Factory.SignedInClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/guests/999999")).StatusCode);
    }

    [Fact]
    public async Task Sample_event_is_seeded_with_fictional_guests()
    {
        var guests = await Client.GetFromJsonAsync<List<SimulatorGuestDto>>("/api/simulator/guests");
        var (ev, seeded) = await WithDbAsync(async db =>
            (await db.Events.SingleAsync(e => e.Name == DemoSeeder.SampleEventName), await db.Guests.Where(g => g.PhoneE164.StartsWith("+2700000")).ToListAsync()));

        Assert.Equal("Year-End Function 2026", ev.Name);
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 18, 0, 0, TimeSpan.FromHours(2)), ev.LocalStartsAt());
        Assert.Equal(60, ev.Capacity);
        Assert.Equal(12, seeded.Count);
        Assert.NotNull(guests);
        Assert.Contains(guests, g => g.Name == "Thandi Mokoena");
    }
}
