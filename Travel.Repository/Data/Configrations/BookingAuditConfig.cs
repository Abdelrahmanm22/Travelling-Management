using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingAuditEventConfig : IEntityTypeConfiguration<BookingAuditEvent>
    {
        public void Configure(EntityTypeBuilder<BookingAuditEvent> builder)
        {
            builder.Property(x => x.OccurredAt).IsRequired();
            builder.Property(x => x.Action).IsRequired();
            builder.Property(x => x.Summary).IsRequired().HasMaxLength(300);

            // Snapshots, not a relationship — a trail that blanks out when someone leaves is not a trail.
            builder.Property(x => x.UserId).IsRequired(false).HasMaxLength(450);
            builder.Property(x => x.UserName).IsRequired(false).HasMaxLength(200);

            builder.HasIndex(x => new { x.BookingId, x.OccurredAt });

            // Ownership. Deleting a booking takes its trail with it — the alternative, a nullable FK
            // preserving orphaned history, is a different feature ("who deleted what") and wasn't asked for.
            builder.HasOne(x => x.Booking)
                .WithMany(b => b.AuditEvents)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class BookingAuditChangeConfig : IEntityTypeConfiguration<BookingAuditChange>
    {
        public void Configure(EntityTypeBuilder<BookingAuditChange> builder)
        {
            builder.Property(x => x.EntityType).IsRequired().HasMaxLength(50);
            builder.Property(x => x.EntityLabel).IsRequired(false).HasMaxLength(200);
            builder.Property(x => x.Field).IsRequired().HasMaxLength(100);
            builder.Property(x => x.OldValue).IsRequired(false).HasMaxLength(1000);
            builder.Property(x => x.NewValue).IsRequired(false).HasMaxLength(1000);
            builder.Property(x => x.Kind).IsRequired();

            builder.HasOne(x => x.Event)
                .WithMany(e => e.Changes)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
