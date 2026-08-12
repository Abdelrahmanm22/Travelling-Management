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

            // Ownership FK — a group's documents belong to the group and go with it.
            builder.HasOne(x => x.BlockServiceStatus)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.BlockServiceStatusId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
