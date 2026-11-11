using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class DriverConfig : IEntityTypeConfiguration<Driver>
    {
        public void Configure(EntityTypeBuilder<Driver> builder)
        {
            builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
            builder.Property(x => x.IsActive).IsRequired();

            // No "_Code" in the name: UnitOfWork.IsCodeUniqueViolation string-matches that substring.
            builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("IX_Drivers_Name");
        }
    }
}
