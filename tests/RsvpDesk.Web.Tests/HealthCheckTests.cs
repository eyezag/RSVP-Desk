using System.Net;
using System.Text.Json;

namespace RsvpDesk.Web.Tests;

public sealed class HealthCheckTests : IClassFixture<RsvpWebFactory>
{
    private readonly RsvpWebFactory _factory;

    public HealthCheckTests(RsvpWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_reports_healthy_database_after_migrations()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", json.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Home_page_renders()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("RSVP Desk", html);
    }
}
