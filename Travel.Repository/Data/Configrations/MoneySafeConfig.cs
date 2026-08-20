using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class MoneySafeConfig : IEntityTypeConfiguration<MoneySafe>
    {
        public void Configure(EntityTypeBuilder<MoneySafe> builder)
        {
            builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
            builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            builder.Property(x => x.Description).IsRequired(false).HasMaxLength(1000);
            builder.Property(x => x.IsActive).IsRequired();

            builder.Property(x => x.CreatedById).IsRequired(false).HasMaxLength(450);
            builder.Property(x => x.CreatedByName).IsRequired(false).HasMaxLength(200);

            // No "_Code" in the name — UnitOfWork translates a unique-index violation on any index
            // matching that substring into DuplicateCodeException, and a duplicate safe name must
            // surface as itself.
            builder.HasIndex(x => x.Name)
                .IsUnique()
                .HasDatabaseName("IX_MoneySafes_Name");
        }
    }

    public class SafeEntryConfig : IEntityTypeConfiguration<SafeEntry>
    {
        public void Configure(EntityTypeBuilder<SafeEntry> builder)
        {
            builder.Ignore(x => x.SignedAmount);

            builder.Property(x => x.Type).IsRequired();
            builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
            builder.Property(x => x.OccurredOn).IsRequired();
            builder.Property(x => x.Description).IsRequired(false).HasMaxLength(200);
            builder.Property(x => x.Reference).IsRequired(false).HasMaxLength(100);
            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(1000);

            builder.Property(x => x.CreatedById).IsRequired(false).HasMaxLength(450);
            builder.Property(x => x.CreatedByName).IsRequired(false).HasMaxLength(200);

            // The ledger filters and orders within one safe by date.
            builder.HasIndex(x => new { x.SafeId, x.OccurredOn })
                .HasDatabaseName("IX_SafeEntries_Safe_OccurredOn");

            // 🔴 The filter is load-bearing. SQL Server treats NULLs as EQUAL in a unique index, so
            // without it the second manual entry ever created (ExpenseId null on both) would fail on a
            // unique violation — and with no "_Code" in the name, not even with a friendly message.
            builder.HasIndex(x => x.ExpenseId)
                .IsUnique()
                .HasFilter("[ExpenseId] IS NOT NULL")
                .HasDatabaseName("IX_SafeEntries_Expense");

            // Ownership — the entry is part of that safe's ledger.
            builder.HasOne(x => x.Safe)
                .WithMany(s => s.Entries)
                .HasForeignKey(x => x.SafeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting the expense takes its withdrawal with it, so the money leaves the balance
            // atomically with the calculation behind it. Only one cascade path reaches this table from
            // MoneySafes, which is why BlockExpense carries no SafeId — see SafeEntry.ExpenseId.
            builder.HasOne(x => x.Expense)
                .WithOne(e => e.SafeEntry)
                .HasForeignKey<SafeEntry>(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class SafeEntryAttachmentConfig : IEntityTypeConfiguration<SafeEntryAttachment>
    {
        public void Configure(EntityTypeBuilder<SafeEntryAttachment> builder)
        {
            builder.Property(x => x.FileUrl).IsRequired().HasMaxLength(500);
            builder.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(260);
            builder.Property(x => x.UploadedByName).IsRequired(false).HasMaxLength(200);

            builder.HasOne(x => x.Entry)
                .WithMany(e => e.Attachments)
                .HasForeignKey(x => x.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
