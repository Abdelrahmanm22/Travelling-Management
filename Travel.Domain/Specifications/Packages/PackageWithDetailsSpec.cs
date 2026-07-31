using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Packages
{
    public class PackageWithDetailsSpec : BaseSpecifications<Package>
    {
        // All packages, ordered by name.
        public PackageWithDetailsSpec()
        {
            AddIncludes();
            SetOrderBy(p => p.Name);
        }

        // A single package by id.
        public PackageWithDetailsSpec(int id) : base(p => p.Id == id)
        {
            AddIncludes();
        }

        private void AddIncludes()
        {
            AddInclude("BaseDestination");
            AddInclude("Segments.Destination");
        }
    }
}
