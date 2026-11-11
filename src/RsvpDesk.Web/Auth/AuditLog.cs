using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Auth;

/// <summary>
/// Writes the audit trail: logins, views of special personal information, exports, sends and
/// changes. Targets are ids ("guest:12"), never names or phone numbers.
/// </summary>
public sealed class AuditLog(RsvpDbContext db, TimeProvider clock)
{
    public async Task WriteAsync(int? userId, string action, string target, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditEntry { UserId = userId, Action = action, Target = target, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }
}
