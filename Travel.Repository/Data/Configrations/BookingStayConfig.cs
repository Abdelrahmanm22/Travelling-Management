using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingStayConfig : IEntityTypeConfiguration<BookingStay>
    {
        public void Configure(EntityTypeBuilder<BookingStay> builder)
        {
            builder.Property(x => x.Nights).IsRequired();
            builder.Property(x => x.SortOrder).IsRequired();
            builder.Property(x => x.HotelName).IsRequired(false).HasMaxLength(200);
            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(1000);

            // Ownership FK — deleting a booking removes its stays.
            builder.HasOne(x => x.Booking)
                .WithMany(b => b.Stays)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Reference FK — NoAction breaks the multiple-cascade-path cycle to Destination.
            builder.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
