using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Auth;

public enum SignInOutcome
{
    Success,
    Invalid,
    LockedOut,
}

public sealed record SignInResult(SignInOutcome Outcome, User? User = null);

/// <summary>Staff accounts: password checks with lockout, user creation, and claims.</summary>
public sealed class UserService(RsvpDbContext db, IOptions<AuthOptions> options, TimeProvider clock, AuditLog audit)
{
    public const int MinPasswordLength = 12;

    private static readonly PasswordHasher<User> Hasher = new();

    // Verified against when the email is unknown, so both paths take about the same time.
    private static readonly string DummyHash = Hasher.HashPassword(null!, "not-a-real-password-" + Guid.NewGuid());

    public async Task<SignInResult> CheckPasswordAsync(string email, string password, CancellationToken ct)
    {
        var normalised = Normalise(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalised, ct);
        var now = clock.GetUtcNow();

        if (user is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, password);
            await audit.WriteAsync(null, "LoginFailed", "unknown account", ct);
            return new SignInResult(SignInOutcome.Invalid);
        }

        if (user.LockedUntil > now)
        {
            await audit.WriteAsync(user.Id, "LoginRejectedLocked", $"user:{user.Id}", ct);
            return new SignInResult(SignInOutcome.LockedOut);
        }

        var verified = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verified == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= options.Value.MaxFailedLogins)
            {
                user.LockedUntil = now.AddMinutes(options.Value.LockoutMinutes);
                user.FailedLoginCount = 0;
            }

            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user.Id, user.LockedUntil > now ? "LoginLockedOut" : "LoginFailed", $"user:{user.Id}", ct);
            return new SignInResult(user.LockedUntil > now ? SignInOutcome.LockedOut : SignInOutcome.Invalid);
        }

        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user.Id, "Login", $"user:{user.Id}", ct);
        return new SignInResult(SignInOutcome.Success, user);
    }

    public async Task<User> CreateAsync(string email, string password, UserRole role, CancellationToken ct)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new ArgumentException($"Passwords must be at least {MinPasswordLength} characters.", nameof(password));
        }

        var user = new User
        {
            Email = Normalise(email),
            PasswordHash = "",
            Role = role,
            CreatedAt = clock.GetUtcNow(),
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public static ClaimsPrincipal ToPrincipal(User user, string scheme) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            scheme));

    public static string Normalise(string email) => email.Trim().ToLowerInvariant();
}

public static class ClaimsPrincipalExtensions
{
    public static int? UserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool IsOrganiser(this ClaimsPrincipal principal) => principal.IsInRole(nameof(UserRole.Organiser));
}
