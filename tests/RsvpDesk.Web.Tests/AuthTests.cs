using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

public sealed class AuthTests(RsvpWebFactory factory) : IClassFixture<RsvpWebFactory>
{
    [Theory]
    [InlineData("/Events")]
    [InlineData("/Events/1")]
    [InlineData("/Guests/1")]
    [InlineData("/Events/Create")]
    public async Task Dashboard_pages_redirect_to_sign_in(string url)
    {
        var response = await factory.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/guests/1")]
    [InlineData("/api/events/1/guests")]
    public async Task Staff_API_returns_401_without_a_session(string url)
    {
        var response = await factory.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Simulator")]
    [InlineData("/Account/Login")]
    [InlineData("/health")]
    [InlineData("/api/simulator/guests")]
    public async Task Public_pages_need_no_login(string url)
    {
        var response = await factory.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Organiser_can_sign_in_and_out()
    {
        var client = await factory.SignedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Events")).StatusCode);

        var logout = await client.PostFormAsync("/Account/Logout", [], getUrl: "/Events");
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Events")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_message()
    {
        var client = factory.NewClient();

        var wrong = await (await client.LoginAsync(RsvpWebFactory.OrganiserEmail, "not-the-password")).Content.ReadAsStringAsync();
        var unknown = await (await client.LoginAsync("nobody@test.example", "whatever-password")).Content.ReadAsStringAsync();

        const string message = "That email and password didn&#x27;t work";
        Assert.Contains(message, wrong);
        Assert.Contains(message, unknown);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        await factory.CreateUserAsync("lockme@test.example", UserRole.Organiser);
        var client = factory.NewClient();

        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync("lockme@test.example", "wrong-password");
        }

        var correct = await client.LoginAsync("lockme@test.example", RsvpWebFactory.Password);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode); // page re-rendered, no redirect = not signed in

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RsvpDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "lockme@test.example");
        Assert.NotNull(user.LockedUntil);
        Assert.True(await db.AuditLog.AnyAsync(a => a.UserId == user.Id && a.Action == "LoginLockedOut"));
    }

    [Fact]
    public async Task Login_form_rejects_posts_without_an_antiforgery_token()
    {
        var response = await factory.NewClient().PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = RsvpWebFactory.OrganiserEmail,
            ["Input.Password"] = RsvpWebFactory.Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Open_redirects_are_not_followed_after_sign_in()
    {
        var client = factory.NewClient();

        var response = await client.PostFormAsync("/Account/Login?ReturnUrl=https%3A%2F%2Fevil.example%2F", new()
        {
            ["Input.Email"] = RsvpWebFactory.OrganiserEmail,
            ["Input.Password"] = RsvpWebFactory.Password,
            ["ReturnUrl"] = "https://evil.example/",
        }, getUrl: "/Account/Login");

        Assert.Equal("/Events", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Auth:LoginRateLimitPerMinute", "3"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.GetAsync("/Account/Login")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Viewers_cannot_open_organiser_pages_or_see_dietary_answers()
    {
        await factory.CreateUserAsync("viewer@test.example", UserRole.Viewer);
        var viewer = await factory.SignedInClientAsync("viewer@test.example");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/Events/1")).StatusCode);
        var denied = await viewer.GetAsync("/Events/Create");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", denied.Headers.Location!.PathAndQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Redirect, (await viewer.GetAsync("/Events/1/Guests/Add")).StatusCode);

        var guestJson = await viewer.GetStringAsync("/api/guests/1");
        Assert.Contains("\"diets\":null", guestJson);
        Assert.Contains("\"messages\":[]", guestJson);
    }
}
