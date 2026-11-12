using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Contracts
{
    /// <summary>
    /// One package with its itinerary (legs come from the base + segments) and its whole contract
    /// program. Three sibling collections grow with the program, so it splits. Filters by Id, so
    /// <c>GetByIdAsync</c> with a split query is safe.
    /// </summary>
    public class PackageContractProgramSpec : BaseSpecifications<Package>
    {
        public PackageContractProgramSpec(int packageId) : base(p => p.Id == packageId)
        {
            AddInclude("BaseDestination");
            AddInclude("Segments.Destination");
            AddInclude("ContractLegs");
            AddInclude("ContractDays");
            AddInclude("ContractListItems");
            UseSplitQuery();
        }
    }
}
