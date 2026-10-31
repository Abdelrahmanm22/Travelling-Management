namespace Travel.Presentation.Services.Vouchers
{
    /// <summary>
    /// Everything a voucher prints, already worded in its language. The single shape both the PDF and
    /// the email body render — built once by <see cref="VoucherModelFactory"/> so they cannot disagree.
    /// </summary>
    public sealed record VoucherModel(
        VoucherText Text,
        string BookingCode,
        string LeadGuestName,
        string IssuedOn,
        int Stars,
        string CategoryLabel,
        string TravellersLabel,
        string NightsLabel,
        string StaysSummary,
        string PaxSummary,
        bool IsPending,
        VoucherFlight Arrival,
        VoucherFlight Departure,
        IReadOnlyList<VoucherTimelineItem> Timeline,
        IReadOnlyList<VoucherGuest> Guests,
        string ContactEmail,
        IReadOnlyList<string> Instagram);

    /// <param name="Detail">"Flight MS 774 · lands 03:40", or the "details to follow" line.</param>
    public sealed record VoucherFlight(string Date, string Detail);

    public enum VoucherTimelineKind { Arrival, Stay, Departure }

    public sealed record VoucherTimelineItem(
        VoucherTimelineKind Kind,
        string Day,
        string Month,
        string Title,
        string Subtitle,
        string? Tag,
        string? NightsLabel,
        string? Range);

    public sealed record VoucherGuest(string Name, string Initials, string Type, bool IsLead);
}
