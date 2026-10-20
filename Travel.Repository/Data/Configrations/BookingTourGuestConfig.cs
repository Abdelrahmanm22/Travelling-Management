using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingTourGuestConfig : IEntityTypeConfiguration<BookingTourGuest>
    {
        public void Configure(EntityTypeBuilder<BookingTourGuest> builder)
        {
            // Named without "_Code": UnitOfWork.IsCodeUniqueViolation string-matches that.
            builder.HasIndex(x => new { x.BookingTourId, x.GuestId })
                .IsUnique()
                .HasDatabaseName("IX_BookingTourGuests_Tour_Guest");

            // Ownership FK — removing a tour row removes who was on it.
            builder.HasOne(x => x.BookingTour)
                .WithMany(t => t.Guests)
                .HasForeignKey(x => x.BookingTourId)
                .OnDelete(DeleteBehavior.Cascade);

            // A second database cascade path from Bookings (via Guests) would be SQL Server 1785, so
            // the database says NO ACTION. ClientCascade still lets EF delete tracked link rows before
            // the guests they point at — Edit rebuilds guests and tours in the same SaveChanges.
            builder.HasOne(x => x.Guest)
                .WithMany()
                .HasForeignKey(x => x.GuestId)
                .OnDelete(DeleteBehavior.ClientCascade);
        }
    }
}
