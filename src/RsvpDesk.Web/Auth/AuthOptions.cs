namespace RsvpDesk.Web.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Created at start-up when there are no users yet. Set via user-secrets or environment variables.</summary>
    public InitialUser? InitialOrganiser { get; set; }

    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Login attempts (page requests) per client IP per minute.</summary>
    public int LoginRateLimitPerMinute { get; set; } = 20;

    public int SessionHours { get; set; } = 8;

    public sealed class InitialUser
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}

public static class Policies
{
    public const string Staff = "Staff";
    public const string Organiser = "Organiser";
    public const string LoginRateLimit = "login";
}
