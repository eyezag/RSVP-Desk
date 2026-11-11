using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

public static partial class TestAuth
{
    /// <summary>A client that keeps cookies but does not follow redirects, so tests can see them.</summary>
    public static HttpClient NewClient(this RsvpWebFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public static async Task<HttpClient> SignedInClientAsync(this RsvpWebFactory factory, string email = RsvpWebFactory.OrganiserEmail, string password = RsvpWebFactory.Password)
    {
        var client = factory.NewClient();
        var response = await client.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        await client.PostFormAsync("/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = password });

    /// <summary>GETs the page for its antiforgery token, then POSTs the form.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, Dictionary<string, string> fields, string? getUrl = null)
    {
        var token = await AntiforgeryTokenAsync(client, getUrl ?? url.Split('?')[0]);
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    public static async Task<string> AntiforgeryTokenAsync(this HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No antiforgery token on {pageUrl}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<int> CreateUserAsync(this RsvpWebFactory factory, string email, UserRole role, string password = RsvpWebFactory.Password)
    {
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserService>().CreateAsync(email, password, role, CancellationToken.None);
        return user.Id;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
