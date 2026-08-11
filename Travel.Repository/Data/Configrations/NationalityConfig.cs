using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Models;

namespace Travel.Repository.Data.Configrations
{
    public class NationalityConfig : IEntityTypeConfiguration<Nationality>
    {
        public void Configure(EntityTypeBuilder<Nationality> builder)
        {
            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            // Named explicitly, and deliberately without "_Code" in it — UnitOfWork.IsCodeUniqueViolation
            // string-matches that substring and would report a name clash as a DuplicateCodeException.
            builder.HasIndex(x => x.Name)
                .IsUnique()
                .HasDatabaseName("IX_Nationalities_Name");
        }
    }
}
