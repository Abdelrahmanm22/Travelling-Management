using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class PackageConfig : IEntityTypeConfiguration<Package>
    {
        public void Configure(EntityTypeBuilder<Package> builder)
        {
            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(x => x.Description)
                .IsRequired(false)
                .HasMaxLength(1000);

            builder.Property(x => x.ImageUrl)
                .IsRequired(false)
                .HasMaxLength(500);

            builder.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(x => x.TotalNights)
                .IsRequired(false);

            builder.Property(x => x.BaseReturnNights)
                .IsRequired();

            builder.HasIndex(x => x.Name).IsUnique();

            // Reference FK — a destination in use as a base can't be deleted; Restrict also breaks the
            // cascade cycle to Destination (SQL Server error 1785).
            builder.HasOne(x => x.BaseDestination)
                .WithMany()
                .HasForeignKey(x => x.BaseDestinationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
