using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingConfig : IEntityTypeConfiguration<Booking>
    {
        public void Configure(EntityTypeBuilder<Booking> builder)
        {
            // Derived from the guest breakdown / roster — never columns.
            builder.Ignore(x => x.Pax);
            builder.Ignore(x => x.LeadGuestName);

            builder.Property(x => x.GuestEmail).IsRequired().HasMaxLength(150);
            builder.Property(x => x.GuestPhone).IsRequired().HasMaxLength(30);
            builder.Property(x => x.Nationality).IsRequired().HasMaxLength(100);

            builder.Property(x => x.Category).IsRequired();

            builder.Property(x => x.ArrivalFlightNo).IsRequired().HasMaxLength(20);
            builder.Property(x => x.DepartureFlightNo).IsRequired().HasMaxLength(20);
            builder.Property(x => x.ArrivalTime).IsRequired();
            builder.Property(x => x.DepartureTime).IsRequired();

            builder.Property(x => x.Adults).IsRequired();
            builder.Property(x => x.Children).IsRequired();
            builder.Property(x => x.Infants).IsRequired();

            builder.Property(x => x.ExtraNights).IsRequired();

            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(2000);
            builder.Property(x => x.ExtensionReason).IsRequired(false).HasMaxLength(500);

            // Reference FK — bookings pin a package; Restrict prevents deleting a package that's in use.
            builder.HasOne(x => x.Package)
                .WithMany()
                .HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
