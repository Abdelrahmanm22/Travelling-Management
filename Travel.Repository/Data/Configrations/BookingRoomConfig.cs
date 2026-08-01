using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingRoomConfig : IEntityTypeConfiguration<BookingRoom>
    {
        public void Configure(EntityTypeBuilder<BookingRoom> builder)
        {
            builder.Property(x => x.Type).IsRequired();
            builder.Property(x => x.Quantity).IsRequired();
            builder.Property(x => x.SortOrder).IsRequired();

            // Ownership FK — rooming belongs to the booking and dies with it.
            builder.HasOne(x => x.Booking)
                .WithMany(b => b.Rooms)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
