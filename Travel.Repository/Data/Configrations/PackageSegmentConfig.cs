using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class PackageSegmentConfig : IEntityTypeConfiguration<PackageSegment>
    {
        public void Configure(EntityTypeBuilder<PackageSegment> builder)
        {
            builder.Property(x => x.Nights)
                .IsRequired();

            builder.Property(x => x.SortOrder)
                .IsRequired();

            // Ownership FK — deleting a package removes its segments.
            builder.HasOne(x => x.Package)
                .WithMany(p => p.Segments)
                .HasForeignKey(x => x.PackageId)
                .OnDelete(DeleteBehavior.Cascade);

            // Reference FK — NoAction breaks the multiple-cascade-path cycle to Destination.
            builder.HasOne(x => x.Destination)
                .WithMany(d => d.Segments)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
