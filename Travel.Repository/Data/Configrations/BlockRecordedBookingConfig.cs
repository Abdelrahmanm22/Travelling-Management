using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BlockRecordedBookingConfig : IEntityTypeConfiguration<BlockRecordedBooking>
    {
        public void Configure(EntityTypeBuilder<BlockRecordedBooking> builder)
        {
            builder.Property(x => x.RoomsFingerprint).IsRequired().HasMaxLength(200);
            builder.Property(x => x.RecordedAt).IsRequired();

            // One record per booking per group. The name must NOT contain "_Code": UnitOfWork
            // translates a unique-index violation on any index matching that substring into
            // DuplicateCodeException, and a collision here must surface as itself.
            builder.HasIndex(x => new { x.BlockServiceStatusId, x.BookingId })
                .IsUnique()
                .HasDatabaseName("IX_BlockRecordedBookings_Block_Booking");

            // Ownership FK — the record is part of the group's own record.
            builder.HasOne(x => x.BlockServiceStatus)
                .WithMany(x => x.RecordedBookings)
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
