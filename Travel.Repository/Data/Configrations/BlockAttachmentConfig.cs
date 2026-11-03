using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BlockAttachmentConfig : IEntityTypeConfiguration<BlockAttachment>
    {
        public void Configure(EntityTypeBuilder<BlockAttachment> builder)
        {
            builder.Property(x => x.FileUrl).IsRequired().HasMaxLength(500);
            builder.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(260);
            builder.Property(x => x.UploadedAt).IsRequired();
            builder.Property(x => x.UploadedByName).HasMaxLength(200);

            // Ownership FK — a flight's files belong to the flight and go with it.
            builder.HasOne(x => x.BlockFlight)
                .WithMany(x => x.Files)
                .HasForeignKey(x => x.BlockFlightId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
