using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingConfig : IEntityTypeConfiguration<Booking>
    {
        public void Configure(EntityTypeBuilder<Booking> builder)
        {
            builder.Property(x => x.GuestFullName)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(x => x.GuestEmail).IsRequired(false).HasMaxLength(150);
            builder.Property(x => x.GuestPhone).IsRequired(false).HasMaxLength(30);
            builder.Property(x => x.Nationality).IsRequired(false).HasMaxLength(100);

            builder.Property(x => x.Pax).IsRequired();
            builder.Property(x => x.Status).IsRequired();
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
