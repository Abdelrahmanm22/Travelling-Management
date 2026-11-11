using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class MovementTypeConfig : IEntityTypeConfiguration<MovementType>
    {
        public void Configure(EntityTypeBuilder<MovementType> builder)
        {
            builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
            builder.Property(x => x.FlightLeg).IsRequired(false);
            builder.Property(x => x.IsActive).IsRequired();

            // No "_Code" in the name: UnitOfWork.IsCodeUniqueViolation string-matches that substring.
            builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("IX_MovementTypes_Name");
        }
    }
}
