using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingTourConfig : IEntityTypeConfiguration<BookingTour>
    {
        public void Configure(EntityTypeBuilder<BookingTour> builder)
        {
            builder.Ignore(x => x.Total);
            builder.Ignore(x => x.HasNamedGuests);

            builder.Property(x => x.Pax).IsRequired();
            builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
            builder.Property(x => x.SortOrder).IsRequired();
            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(500);

            // Ownership FK — deleting a booking removes the tours sold on it.
            builder.HasOne(x => x.Booking)
                .WithMany(b => b.Tours)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Reference FK — Restrict both breaks the second cascade path into this table and stops a
            // catalogue delete from silently erasing sold tours. TourController refuses first.
            builder.HasOne(x => x.Tour)
                .WithMany()
                .HasForeignKey(x => x.TourId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
