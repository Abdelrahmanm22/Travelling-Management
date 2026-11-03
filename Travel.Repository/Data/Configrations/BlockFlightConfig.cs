using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BlockFlightConfig : IEntityTypeConfiguration<BlockFlight>
    {
        public void Configure(EntityTypeBuilder<BlockFlight> builder)
        {
            builder.Property(x => x.FlightNumber).IsRequired().HasMaxLength(20);
            builder.Property(x => x.CreatedByName).HasMaxLength(200);
            builder.Property(x => x.UpdatedByName).HasMaxLength(200);

            // Ownership FK — the group's flights are part of its record.
            builder.HasOne(x => x.BlockServiceStatus)
                .WithMany(x => x.Flights)
                .HasForeignKey(x => x.BlockServiceStatusId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class BlockFlightBookingConfig : IEntityTypeConfiguration<BlockFlightBooking>
    {
        public void Configure(EntityTypeBuilder<BlockFlightBooking> builder)
        {
            // The name must NOT contain "_Code" — see BlockRecordedBookingConfig.
            builder.HasIndex(x => new { x.BlockFlightId, x.BookingId })
                .IsUnique()
                .HasDatabaseName("IX_BlockFlightBookings_Flight_Booking");

            builder.HasOne(x => x.BlockFlight)
                .WithMany(x => x.Bookings)
                .HasForeignKey(x => x.BlockFlightId)
                .OnDelete(DeleteBehavior.Cascade);

            // NoAction, as on BlockRecordedBooking: BookingController.Delete clears these rows by hand.
            builder.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
