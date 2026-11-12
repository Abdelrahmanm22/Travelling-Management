using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Contracts
{
    /// <summary>A booking's contract draft row (unique per booking). No collections — a plain query.</summary>
    public class BookingContractSpec : BaseSpecifications<BookingContract>
    {
        public BookingContractSpec(int bookingId) : base(c => c.BookingId == bookingId)
        {
        }
    }
}
