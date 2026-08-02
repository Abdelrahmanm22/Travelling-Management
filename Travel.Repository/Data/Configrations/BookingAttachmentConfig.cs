using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingAttachmentConfig : IEntityTypeConfiguration<BookingAttachment>
    {
        public void Configure(EntityTypeBuilder<BookingAttachment> builder)
        {
            builder.Property(x => x.Category).IsRequired();
            builder.Property(x => x.FileUrl).HasMaxLength(500);
            builder.Property(x => x.OriginalFileName).HasMaxLength(260);
            builder.Property(x => x.LinkUrl).HasMaxLength(500);
            builder.Property(x => x.UploadedAt).IsRequired();

            // Ownership FK — attachments belong to the booking and die with it.
            builder.HasOne(x => x.Booking)
                .WithMany(b => b.Attachments)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
