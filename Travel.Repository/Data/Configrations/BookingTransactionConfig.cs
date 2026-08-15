using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingTransactionConfig : IEntityTypeConfiguration<BookingTransaction>
    {
        public void Configure(EntityTypeBuilder<BookingTransaction> builder)
        {
            builder.Ignore(x => x.SignedAmount);

            builder.Property(x => x.Type).IsRequired();
            builder.Property(x => x.Method).IsRequired();
            builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
            builder.Property(x => x.PaidOn).IsRequired();
            builder.Property(x => x.Reference).IsRequired(false).HasMaxLength(100);
            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(1000);

            // Snapshots, not a relationship — see the property docs on BookingTransaction.
            builder.Property(x => x.CreatedById).IsRequired(false).HasMaxLength(450);
            builder.Property(x => x.CreatedByName).IsRequired(false).HasMaxLength(200);

            // The global /Transaction list filters and orders by payment date.
            builder.HasIndex(x => x.PaidOn);

            builder.HasOne(x => x.Booking)
                .WithMany(b => b.Transactions)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class TransactionAttachmentConfig : IEntityTypeConfiguration<TransactionAttachment>
    {
        public void Configure(EntityTypeBuilder<TransactionAttachment> builder)
        {
            builder.Property(x => x.FileUrl).IsRequired().HasMaxLength(500);
            builder.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(260);

            builder.HasOne(x => x.Transaction)
                .WithMany(t => t.Attachments)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
