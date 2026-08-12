using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BlockConfirmedBookingConfig : IEntityTypeConfiguration<BlockConfirmedBooking>
    {
        public void Configure(EntityTypeBuilder<BlockConfirmedBooking> builder)
        {
            builder.Property(x => x.RoomsFingerprint).IsRequired().HasMaxLength(200);
            builder.Property(x => x.ConfirmedAt).IsRequired();

            // One snapshot per booking per group. The name must NOT contain "_Code": UnitOfWork
            // translates a unique-index violation on any index matching that substring into
            // DuplicateCodeException, and a collision here must surface as itself.
            builder.HasIndex(x => new { x.BlockServiceStatusId, x.BookingId })
                .IsUnique()
                .HasDatabaseName("IX_BlockConfirmedBookings_Block_Booking");

            // Ownership FK — the snapshot is part of the group's record.
            builder.HasOne(x => x.BlockServiceStatus)
                .WithMany(x => x.ConfirmedBookings)
                .HasForeignKey(x => x.BlockServiceStatusId)
                .OnDelete(DeleteBehavior.Cascade);

            // NoAction breaks the second cascade path into this table (SQL Server error 1785).
            // BookingController.Delete clears these rows explicitly before deleting the booking.
            builder.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
