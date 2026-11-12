using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    // Contract generation: the package program, the company terms and the per-booking draft.
    // No index here may contain "_Code" — UnitOfWork.IsCodeUniqueViolation string-matches it.

    public class PackageContractLegConfig : IEntityTypeConfiguration<PackageContractLeg>
    {
        public void Configure(EntityTypeBuilder<PackageContractLeg> builder)
        {
            builder.Property(x => x.Heading).HasMaxLength(150);

            // A plain snapshot, no FK: it records which destination the leg was written for, so a later
            // segment edit can be noticed. A second path into Destinations would buy nothing.
            builder.Property(x => x.DestinationId).IsRequired();

            builder.HasOne(x => x.Package)
                .WithMany(p => p.ContractLegs)
                .HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => new { x.PackageId, x.Leg })
                .IsUnique()
                .HasDatabaseName("IX_PackageContractLegs_Package_Leg");
        }
    }

    public class PackageContractDayConfig : IEntityTypeConfiguration<PackageContractDay>
    {
        public void Configure(EntityTypeBuilder<PackageContractDay> builder)
        {
            builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
            builder.Property(x => x.Body).IsRequired();

            builder.HasOne(x => x.Package)
                .WithMany(p => p.ContractDays)
                .HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class PackageContractListItemConfig : IEntityTypeConfiguration<PackageContractListItem>
    {
        public void Configure(EntityTypeBuilder<PackageContractListItem> builder)
        {
            builder.Property(x => x.Text).IsRequired().HasMaxLength(500);

            builder.HasOne(x => x.Package)
                .WithMany(p => p.ContractListItems)
                .HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class ContractTermsSectionConfig : IEntityTypeConfiguration<ContractTermsSection>
    {
        public void Configure(EntityTypeBuilder<ContractTermsSection> builder)
        {
            builder.Property(x => x.Heading).HasMaxLength(300);
            builder.Property(x => x.Body).IsRequired();
        }
    }

    public class ContractSettingsConfig : IEntityTypeConfiguration<ContractSettings>
    {
        public void Configure(EntityTypeBuilder<ContractSettings> builder)
        {
            builder.Property(x => x.DocumentTitle).IsRequired().HasMaxLength(200);
            builder.Property(x => x.SignatureCity).IsRequired().HasMaxLength(100);
            builder.Property(x => x.CompanySignatureName).IsRequired().HasMaxLength(100);
        }
    }

    public class BookingContractConfig : IEntityTypeConfiguration<BookingContract>
    {
        public void Configure(EntityTypeBuilder<BookingContract> builder)
        {
            builder.Property(x => x.DraftJson).IsRequired();
            builder.Property(x => x.GeneratedById).HasMaxLength(450);
            builder.Property(x => x.GeneratedByName).HasMaxLength(200);

            // Ownership: the draft dies with its booking.
            builder.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.BookingId)
                .IsUnique()
                .HasDatabaseName("IX_BookingContracts_Booking");
        }
    }
}
