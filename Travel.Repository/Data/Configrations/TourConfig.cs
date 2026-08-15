using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class TourConfig : IEntityTypeConfiguration<Tour>
    {
        public void Configure(EntityTypeBuilder<Tour> builder)
        {
            builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
            builder.Property(x => x.Description).IsRequired(false).HasMaxLength(1000);
            builder.Property(x => x.PricePerPerson).HasColumnType("decimal(18,2)").IsRequired();
            builder.Property(x => x.IsActive).IsRequired();

            // Named explicitly and WITHOUT "_Code" in it: UnitOfWork.IsCodeUniqueViolation string-matches
            // that substring, and a duplicate tour name must surface as itself, not DuplicateCodeException.
            builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("IX_Tours_Name");
        }
    }
}
