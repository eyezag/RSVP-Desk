using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RsvpDesk.Web.Auth;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Pages.Account;

[EnableRateLimiting(Policies.LoginRateLimit)]
public class LoginModel(UserService users, IOptions<AuthOptions> auth, IOptions<DemoOptions> demo, IOptions<ChannelOptions> channel) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Only for the public demo, and never in live WhatsApp mode (decisions D-303).</summary>
    public AuthOptions.InitialUser? DemoHint =>
        demo.Value.ShowLoginHint && channel.Value.Mode == ChannelMode.Simulator && auth.Value.InitialOrganiser is { Email: not null, Password: not null } u ? u : null;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Input.Email) || string.IsNullOrEmpty(Input.Password))
        {
            ErrorMessage = "Enter your email and password.";
            return Page();
        }

        var result = await users.CheckPasswordAsync(Input.Email, Input.Password, ct);
        if (result.Outcome != SignInOutcome.Success)
        {
            // One message for every failure, so the page does not reveal which accounts exist.
            ErrorMessage = "That email and password didn't work, or the account is temporarily locked after too many attempts.";
            return Page();
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            UserService.ToPrincipal(result.User!, CookieAuthenticationDefaults.AuthenticationScheme));

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Events");
    }

    public sealed class LoginInput
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
