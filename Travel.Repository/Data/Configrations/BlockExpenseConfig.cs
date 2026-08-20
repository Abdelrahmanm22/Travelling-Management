using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class ExpenseTypeConfig : IEntityTypeConfiguration<ExpenseType>
    {
        public void Configure(EntityTypeBuilder<ExpenseType> builder)
        {
            builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
            builder.Property(x => x.Description).IsRequired(false).HasMaxLength(1000);
            builder.Property(x => x.Basis).IsRequired();
            builder.Property(x => x.IncludeChildren).IsRequired();
            builder.Property(x => x.IncludeInfants).IsRequired();
            builder.Property(x => x.IsActive).IsRequired();

            // No "_Code" in the name — see MoneySafeConfig.
            builder.HasIndex(x => x.Name)
                .IsUnique()
                .HasDatabaseName("IX_ExpenseTypes_Name");
        }
    }

    public class BlockExpenseConfig : IEntityTypeConfiguration<BlockExpense>
    {
        public void Configure(EntityTypeBuilder<BlockExpense> builder)
        {
            builder.Property(x => x.ExpenseTypeName).IsRequired().HasMaxLength(150);
            builder.Property(x => x.Basis).IsRequired();
            builder.Property(x => x.IncludeChildren).IsRequired();
            builder.Property(x => x.IncludeInfants).IsRequired();
            builder.Property(x => x.SpentOn).IsRequired();
            builder.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)").IsRequired();

            // Six decimals: a rate is not money, and 2dp would round 48.5075 to 48.51 and skew every
            // conversion on the expense. Nullable on purpose — see the property docs.
            builder.Property(x => x.UnitsPerUsd).HasColumnType("decimal(18,6)").IsRequired(false);
            builder.Property(x => x.Description).IsRequired(false).HasMaxLength(200);
            builder.Property(x => x.Reference).IsRequired(false).HasMaxLength(100);
            builder.Property(x => x.Notes).IsRequired(false).HasMaxLength(1000);

            builder.Property(x => x.CreatedById).IsRequired(false).HasMaxLength(450);
            builder.Property(x => x.CreatedByName).IsRequired(false).HasMaxLength(200);
            builder.Property(x => x.RecalculatedByName).IsRequired(false).HasMaxLength(200);

            builder.HasIndex(x => x.SpentOn);

            // Reference FK, not ownership — retiring a type must never delete the expenses that used
            // it. ExpenseTypeController refuses to delete an in-use type first; this is the backstop.
            builder.HasOne(x => x.ExpenseType)
                .WithMany()
                .HasForeignKey(x => x.ExpenseTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class BlockExpenseRateConfig : IEntityTypeConfiguration<BlockExpenseRate>
    {
        public void Configure(EntityTypeBuilder<BlockExpenseRate> builder)
        {
            builder.Property(x => x.RoomType).IsRequired();
            builder.Property(x => x.Rate).HasColumnType("decimal(18,2)").IsRequired();

            builder.HasIndex(x => new { x.ExpenseId, x.RoomType })
                .IsUnique()
                .HasDatabaseName("IX_BlockExpenseRates_Expense_RoomType");

            builder.HasOne(x => x.Expense)
                .WithMany(e => e.Rates)
                .HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class BlockExpenseGroupConfig : IEntityTypeConfiguration<BlockExpenseGroup>
    {
        public void Configure(EntityTypeBuilder<BlockExpenseGroup> builder)
        {
            builder.Property(x => x.DestinationName).IsRequired().HasMaxLength(150);
            builder.Property(x => x.CheckInDate).IsRequired();
            builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
            builder.Property(x => x.RoomsFingerprint).IsRequired().HasMaxLength(200);

            // Generous on purpose: a truncated fingerprint silently never matches, and every expense
            // would read "changed" forever with nothing to explain it.
            builder.Property(x => x.AppliedFingerprint).IsRequired().HasMaxLength(4000);

            builder.HasIndex(x => new { x.ExpenseId, x.DestinationId, x.CheckInDate })
                .IsUnique()
                .HasDatabaseName("IX_BlockExpenseGroups_Expense_Destination_CheckIn");

            builder.HasOne(x => x.Expense)
                .WithMany(e => e.Groups)
                .HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Reference FK, matching BlockServiceStatus.Destination.
            builder.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class BlockExpenseBookingConfig : IEntityTypeConfiguration<BlockExpenseBooking>
    {
        public void Configure(EntityTypeBuilder<BlockExpenseBooking> builder)
        {
            builder.Property(x => x.BookingCode).IsRequired().HasMaxLength(50);
            builder.Property(x => x.LeadGuestName).IsRequired().HasMaxLength(200);
            builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
            builder.Property(x => x.RoomsFingerprint).IsRequired().HasMaxLength(200);

            // Filtered for the same reason as IX_SafeEntries_Expense: NULLs compare equal in a SQL
            // Server unique index, and a group can hold several lines whose booking has been deleted.
            builder.HasIndex(x => new { x.ExpenseGroupId, x.BookingId })
                .IsUnique()
                .HasFilter("[BookingId] IS NOT NULL")
                .HasDatabaseName("IX_BlockExpenseBookings_Group_Booking");

            builder.HasOne(x => x.ExpenseGroup)
                .WithMany(g => g.Bookings)
                .HasForeignKey(x => x.ExpenseGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // NoAction breaks the second cascade path into this table (SQL Server error 1785), same as
            // BlockRecordedBooking. BookingController.Delete NULLs these rows by hand — it must not
            // delete them: the line records money that already left a safe.
            builder.HasOne(x => x.Booking)
                .WithMany()
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
