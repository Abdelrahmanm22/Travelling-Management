using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingGuestConfig : IEntityTypeConfiguration<BookingGuest>
    {
        public void Configure(EntityTypeBuilder<BookingGuest> builder)
        {
            builder.Property(x => x.FullName).IsRequired().HasMaxLength(150);
            builder.Property(x => x.Type).IsRequired();
            builder.Property(x => x.SortOrder).IsRequired();
            builder.Property(x => x.IsLeadGuest).IsRequired();
            builder.Property(x => x.Age).IsRequired(false);

            // Ownership FK — guests belong to the booking and die with it.
            builder.HasOne(x => x.Booking)
                .WithMany(b => b.Guests)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
