using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class InternalMovementConfig : IEntityTypeConfiguration<InternalMovement>
    {
        public void Configure(EntityTypeBuilder<InternalMovement> builder)
        {
            // Ownership: deleting a booking removes its movements. The only path from Bookings into
            // this table, so no multiple-cascade-path (1785) problem.
            builder.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // References. Their controllers refuse a delete while a movement uses them.
            builder.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.MovementType)
                .WithMany()
                .HasForeignKey(x => x.MovementTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Driver)
                .WithMany()
                .HasForeignKey(x => x.DriverId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.CreatedByName).HasMaxLength(200);
            builder.Property(x => x.UpdatedByName).HasMaxLength(200);

            // Not unique: a booking can have several movements in one day.
            builder.HasIndex(x => new { x.DestinationId, x.Date })
                .HasDatabaseName("IX_InternalMovements_Destination_Date");
        }
    }
}
