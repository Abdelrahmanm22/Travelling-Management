using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class DestinationConfig : IEntityTypeConfiguration<Destination>
    {
        public void Configure(EntityTypeBuilder<Destination> builder)
        {
            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.IsCruise)
                .IsRequired();

            builder.Property(x => x.HasInternalMovements)
                .IsRequired();

            builder.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            // Stored as its underlying int; null when the destination isn't a cruise.
            builder.Property(x => x.DepartureDays)
                .IsRequired(false);

            builder.Property(x => x.ContractAccommodationText).HasMaxLength(200);
            builder.Property(x => x.ContractFreeDayTitle).HasMaxLength(150);
            builder.Property(x => x.ContractFreeDayText).HasMaxLength(2000);

            builder.HasIndex(x => x.Name).IsUnique();
        }
    }
}
