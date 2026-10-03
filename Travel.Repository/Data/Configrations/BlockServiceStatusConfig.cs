using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BlockServiceStatusConfig : IEntityTypeConfiguration<BlockServiceStatus>
    {
        public void Configure(EntityTypeBuilder<BlockServiceStatus> builder)
        {
            builder.Property(x => x.CheckInDate).IsRequired();

            // No HasDefaultValue on an enum property — the int/enum mismatch breaks at design time.
            // The default lives on the C# property instead.
            builder.Property(x => x.Status).IsRequired();

            builder.Property(x => x.RequestEmailLink).HasMaxLength(1000);
            builder.Property(x => x.ConfirmationEmailLink).HasMaxLength(1000);

            builder.Property(x => x.UpdatedAt).IsRequired();
            builder.Property(x => x.UpdatedByUserId).HasMaxLength(450);
            builder.Property(x => x.UpdatedByName).HasMaxLength(200);

            // One row per arrival group. The name must NOT contain "_Code": UnitOfWork translates a
            // unique-index violation on any index matching that substring into DuplicateCodeException.
            // Deliberately UNFILTERED although PrivateBookingId is nullable: SQL Server compares NULLs
            // as equal in a unique index, which is exactly the rule wanted — one shared row per
            // (destination, check-in, category), plus one row per private booking.
            builder.HasIndex(x => new { x.DestinationId, x.CheckInDate, x.Category, x.PrivateBookingId })
                .IsUnique()
                .HasFilter(null)
                .HasDatabaseName("IX_BlockServiceStatuses_Destination_CheckIn_Category_Private");

            // Reference FK — a status pins a destination; Restrict prevents deleting one that's in use.
            builder.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
