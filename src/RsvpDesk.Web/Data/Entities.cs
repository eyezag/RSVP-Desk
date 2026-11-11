using RsvpDesk.Engine;

namespace RsvpDesk.Web.Data;

public sealed class Event
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Stored in UTC. Shown in <see cref="TimeZoneId"/> (decisions D-202).</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>IANA time zone of the venue, e.g. Africa/Johannesburg.</summary>
    public required string TimeZoneId { get; set; }

    public required string Venue { get; set; }
    public int Capacity { get; set; }
    public int PartyMax { get; set; } = 4;
    public int DeleteDataAfterDays { get; set; } = 30;

    /// <summary>Reminders go only to guests invited at least this many days ago, and at most this often (step 6).</summary>
    public int ReminderAfterDays { get; set; } = 3;

    /// <summary>Marks the fictional demo event.</summary>
    public bool IsSample { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Guest> Guests { get; set; } = [];

    public DateTimeOffset LocalStartsAt()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return TimeZoneInfo.ConvertTime(StartsAt, zone);
    }

    public EventDetails ToDetails() => new(Name, LocalStartsAt(), Venue, PartyMax);
}

public enum GuestStatus
{
    NotInvited,
    Invited,
    InChat,
    Attending,
    Declined,
}

public sealed class Guest
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public required string Name { get; set; }

    /// <summary>E.164, unique per event. Never log this in full.</summary>
    public required string PhoneE164 { get; set; }

    public DateTimeOffset? ConsentAt { get; set; }
    public string? ConsentSource { get; set; }
    public DateTimeOffset? OptedOutAt { get; set; }
    public DateTimeOffset? InviteSentAt { get; set; }
    public bool NeedsAttention { get; set; }

    /// <summary>Derived from the conversation and cached for filtering (see <c>GuestStatusRules</c>).</summary>
    public GuestStatus Status { get; set; } = GuestStatus.NotInvited;

    /// <summary>Last inbound or outbound message, for the dashboard's "last activity" column.</summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    public Conversation? Conversation { get; set; }
    public Rsvp? Rsvp { get; set; }
    public List<Message> Messages { get; set; } = [];
}

public sealed class Conversation
{
    public int GuestId { get; set; }
    public Step State { get; set; } = Step.NotInvited;

    /// <summary>The engine's full <see cref="ConversationState"/> as JSON.</summary>
    public string StateData { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token, bumped on every save.</summary>
    public int Version { get; set; }
}

public sealed class Rsvp
{
    public int GuestId { get; set; }
    public bool? Attending { get; set; }
    public int? PartySize { get; set; }
    public int? WheelchairUsers { get; set; }
    public string? AccessNote { get; set; }

    /// <summary>When the guest finished (confirmed or declined). Null while in progress.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    public List<RsvpPerson> Persons { get; set; } = [];
}

public sealed class RsvpPerson
{
    public int Id { get; set; }
    public int RsvpId { get; set; }
    public int PersonIndex { get; set; }
    public Dietary Dietary { get; set; }
    public string? DietaryNote { get; set; }
}

public enum MessageDirection
{
    In,
    Out,
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Delivered,
    Read,
    Failed,
}

public sealed class Message
{
    public long Id { get; set; }
    public int GuestId { get; set; }
    public MessageDirection Direction { get; set; }
    public required string Body { get; set; }

    /// <summary>Button or list id the guest tapped (inbound only).</summary>
    public string? ReplyId { get; set; }

    /// <summary>Options offered with an outbound message, as JSON [{id,title}].</summary>
    public string? ChoicesJson { get; set; }

    /// <summary>Unique when set: the channel's id for the message (wamid… or sim-…).</summary>
    public string? ProviderMessageId { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>Outbound only.</summary>
    public DeliveryStatus? DeliveryStatus { get; set; }
}

/// <summary>Raw inbound event, stored before any processing (spec section 7).</summary>
public sealed class WebhookEvent
{
    public long Id { get; set; }

    /// <summary>Which channel produced it: "simulator" or "whatsapp".</summary>
    public required string Source { get; set; }

    /// <summary>Unique: repeated deliveries of the same event are ignored.</summary>
    public required string ExternalId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
    public required string RawJson { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}

public enum UserRole
{
    /// <summary>Full access: sends messages, edits guests, sees dietary and access data, exports.</summary>
    Organiser,

    /// <summary>Read-only. Does not see dietary or access answers (special personal information).</summary>
    Viewer,
}

public sealed class User
{
    public int Id { get; set; }

    /// <summary>Stored lower-case.</summary>
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }

    /// <summary>Null for actions by the system (e.g. the retention job) or failed logins.</summary>
    public int? UserId { get; set; }

    public required string Action { get; set; }
    public required string Target { get; set; }
    public DateTimeOffset At { get; set; }
}
