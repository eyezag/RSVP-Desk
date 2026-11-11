using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RsvpDesk.Web.Data;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> b)
    {
        b.Property(e => e.Name).HasMaxLength(200);
        b.Property(e => e.Venue).HasMaxLength(300);
        b.Property(e => e.TimeZoneId).HasMaxLength(64);
    }
}

internal sealed class GuestConfiguration : IEntityTypeConfiguration<Guest>
{
    public void Configure(EntityTypeBuilder<Guest> b)
    {
        b.Property(g => g.Name).HasMaxLength(200);
        b.Property(g => g.PhoneE164).HasMaxLength(16);
        b.Property(g => g.ConsentSource).HasMaxLength(200);
        b.Property(g => g.Status).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(g => new { g.EventId, g.PhoneE164 }).IsUnique();
        b.HasIndex(g => new { g.EventId, g.Status });
        b.Property(g => g.WhatsAppUserId).HasMaxLength(128);
        b.HasIndex(g => g.WhatsAppUserId);
        b.HasOne(g => g.Event).WithMany(e => e.Guests).HasForeignKey(g => g.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(g => g.Conversation).WithOne().HasForeignKey<Conversation>(c => c.GuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(g => g.Rsvp).WithOne().HasForeignKey<Rsvp>(r => r.GuestId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(g => g.Messages).WithOne().HasForeignKey(m => m.GuestId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.HasKey(c => c.GuestId);
        b.Property(c => c.State).HasConversion<string>().HasMaxLength(24);
        b.Property(c => c.Version).IsConcurrencyToken();
    }
}

internal sealed class RsvpConfiguration : IEntityTypeConfiguration<Rsvp>
{
    public void Configure(EntityTypeBuilder<Rsvp> b)
    {
        b.HasKey(r => r.GuestId);
        // Notes are capped by the engine at 120 characters as the guest sees them; emoji can take
        // up to several UTF-16 units each, so the column allows more.
        b.Property(r => r.AccessNote).HasMaxLength(600);
        b.HasMany(r => r.Persons).WithOne().HasForeignKey(p => p.RsvpId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RsvpPersonConfiguration : IEntityTypeConfiguration<RsvpPerson>
{
    public void Configure(EntityTypeBuilder<RsvpPerson> b)
    {
        b.Property(p => p.Dietary).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.DietaryNote).HasMaxLength(300);
        b.HasIndex(p => new { p.RsvpId, p.PersonIndex }).IsUnique();
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.Property(m => m.Direction).HasConversion<string>().HasMaxLength(8);
        b.Property(m => m.DeliveryStatus).HasConversion<string>().HasMaxLength(16);
        b.Property(m => m.Body).HasMaxLength(4096);
        b.Property(m => m.ReplyId).HasMaxLength(256);
        b.Property(m => m.ProviderMessageId).HasMaxLength(256);
        b.Property(m => m.DeliveryError).HasMaxLength(300);
        b.HasIndex(m => m.ProviderMessageId).IsUnique();
        b.HasIndex(m => new { m.GuestId, m.Id });
    }
}

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.Property(e => e.Source).HasMaxLength(16);
        b.Property(e => e.ExternalId).HasMaxLength(256);
        b.HasIndex(e => e.ExternalId).IsUnique();
        b.HasIndex(e => e.ProcessedAt);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.Email).HasMaxLength(254);
        b.Property(u => u.PasswordHash).HasMaxLength(512);
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(u => u.Email).IsUnique();
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditLog");
        b.Property(a => a.Action).HasMaxLength(64);
        b.Property(a => a.Target).HasMaxLength(300);
        b.HasIndex(a => a.At);
        b.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
