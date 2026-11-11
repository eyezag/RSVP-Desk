namespace RsvpDesk.Web.WhatsApp;

/// <summary>
/// Live-mode settings. Secrets (AccessToken, AppSecret, VerifyToken) come from user-secrets or
/// environment variables, never from appsettings files in the repository.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>The version used in Meta's examples when this was written (docs/whatsapp-integration.md §5).</summary>
    public string ApiVersion { get; set; } = "v27.0";

    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }

    /// <summary>Signs webhook POSTs (X-Hub-Signature-256).</summary>
    public string? AppSecret { get; set; }

    /// <summary>Compared with hub.verify_token during webhook verification.</summary>
    public string? VerifyToken { get; set; }

    public string TemplateLanguage { get; set; } = "en";
    public TemplateNames Templates { get; set; } = new();

    /// <summary>Template quick-reply label → conversation option id (docs/templates.md).</summary>
    public Dictionary<string, string> QuickReplies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Yes"] = "attend.yes",
        ["No"] = "attend.no",
        ["Continue"] = "resume",
    };

    public int MaxSendAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Free-form messages are only allowed within 24 hours of the guest's last message. Keep a
    /// margin so a message queued near the boundary doesn't fail with 131047.
    /// </summary>
    public TimeSpan CustomerServiceWindow { get; set; } = TimeSpan.FromHours(23.5);

    public IEnumerable<string> MissingSettings()
    {
        if (string.IsNullOrWhiteSpace(PhoneNumberId)) yield return "WhatsApp:PhoneNumberId";
        if (string.IsNullOrWhiteSpace(AccessToken)) yield return "WhatsApp:AccessToken";
        if (string.IsNullOrWhiteSpace(AppSecret)) yield return "WhatsApp:AppSecret";
        if (string.IsNullOrWhiteSpace(VerifyToken)) yield return "WhatsApp:VerifyToken";
    }

    public sealed class TemplateNames
    {
        public string Invitation { get; set; } = "rsvp_invitation";
        public string ReminderNotReplied { get; set; } = "rsvp_reminder";
        public string ReminderUnfinished { get; set; } = "rsvp_reminder_unfinished";
    }
}
