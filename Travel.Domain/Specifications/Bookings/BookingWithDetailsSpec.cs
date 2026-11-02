using Travel.Domain.Models;

namespace Travel.Domain.Specifications.Bookings
{
    /// <summary>
    /// One booking with everything Details, Edit, the Excel export and the voucher read.
    /// Lists don't use it — <see cref="BookingListSpec"/> loads only what a row renders.
    /// </summary>
    public class BookingWithDetailsSpec : BaseSpecifications<Booking>
    {
        public BookingWithDetailsSpec(int id) : base(b => b.Id == id)
        {
            AddIncludes();
        }

        // Booking.GrandTotal / PaidTotal are computed over Stays, Tours and Transactions, so a missing
        // include here doesn't error — it silently makes the total too small everywhere this spec is
        // used, which is Details, Edit and the Excel export. Same trap as Nationality rendering "—".
        private void AddIncludes()
        {
            AddInclude("Package");
            AddInclude("Nationality");
            AddInclude("Stays.Destination");
            // "Stays.Rates", not just "Stays": the extra-night charge lives one level down, and
            // without it ExtraNightsTotal reads 0 AND HasUnpricedExtraNights reads true for every
            // booking — so every total renders "—" with nothing to say why.
            AddInclude("Stays.Rates");
            AddInclude("Guests");
            AddInclude("Rooms");
            AddInclude("Attachments");
            AddInclude("Tours.Tour");
            // Who is on each tour. Edit maps it back to ticks, Details and the export list the names,
            // and the audit diff compares it — without it every tour reads "names not recorded".
            AddInclude("Tours.Guests.Guest");
            // "Transactions.Attachments", not just "Transactions": without the second level the
            // receipts navigation comes back empty and Booking/Details renders "—" for a payment that
            // does have a receipt — no error, and the global /Transaction list still shows it correctly
            // (TransactionSpec includes them), which makes the two pages disagree.
            AddInclude("Transactions.Attachments");

            // Six sibling collections: as one JOIN they multiply
            // (stays x rates x guests x rooms x attachments x tour guests x receipts) into thousands of rows.
            UseSplitQuery();
        }
    }
}
