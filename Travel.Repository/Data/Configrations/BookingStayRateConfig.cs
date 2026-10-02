using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class BookingStayRateConfig : IEntityTypeConfiguration<BookingStayRate>
    {
        public void Configure(EntityTypeBuilder<BookingStayRate> builder)
        {
            builder.Property(x => x.RoomType).IsRequired();
            builder.Property(x => x.Quantity).IsRequired();
            builder.Property(x => x.Rate).HasColumnType("decimal(18,2)").IsRequired();

            // Named without "_Code": UnitOfWork.IsCodeUniqueViolation string-matches that, and a
            // duplicate room type on one stay must surface as itself, not DuplicateCodeException.
            builder.HasIndex(x => new { x.StayId, x.RoomType })
                .IsUnique()
                .HasDatabaseName("IX_BookingStayRates_Stay_RoomType");

            // Ownership FK. There is deliberately no BookingId here — it would be a second cascade
            // path into Bookings (SQL Server 1785), and the audit interceptor reaches the booking
            // through the Stay navigation instead.
            builder.HasOne(x => x.Stay)
                .WithMany(s => s.Rates)
                .HasForeignKey(x => x.StayId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
