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

            // Auto-generated ("MUSB-000001"); the index name is matched by UnitOfWork's collision test.
            builder.Property(x => x.Code).IsRequired().HasMaxLength(20);
            builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_Bookings_Code");

            builder.Property(x => x.GuestEmail).IsRequired().HasMaxLength(150);
            builder.Property(x => x.GuestPhone).IsRequired().HasMaxLength(30);

            builder.Property(x => x.Category).IsRequired();

            builder.Property(x => x.Status).IsRequired();

            // Optional — nullability is inferred from the CLR types; only the length is pinned here.
            builder.Property(x => x.ArrivalFlightNo).HasMaxLength(20);
            builder.Property(x => x.DepartureFlightNo).HasMaxLength(20);

            builder.Property(x => x.Adults).IsRequired();
            builder.Property(x => x.Children).IsRequired();
            builder.Property(x => x.Infants).IsRequired();

            // Sale price per person. Nullable on purpose — bookings taken before pricing existed have none.
            builder.Property(x => x.AdultPrice).HasColumnType("decimal(18,2)");
            builder.Property(x => x.ChildPrice).HasColumnType("decimal(18,2)");
            builder.Property(x => x.InfantPrice).HasColumnType("decimal(18,2)");

            builder.Property(x => x.ExtraNights).IsRequired();

            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(2000);
            builder.Property(x => x.ExtensionReason).IsRequired(false).HasMaxLength(500);

            // Reference FK — bookings pin a package; Restrict prevents deleting a package that's in use.
            builder.HasOne(x => x.Package)
                .WithMany()
                .HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Restrict);

            // Reference FK, not ownership — deleting a nationality must never touch bookings.
            // NationalityController refuses the delete first, so this only ever fires as a backstop.
            builder.HasOne(x => x.Nationality)
                .WithMany()
                .HasForeignKey(x => x.NationalityId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
