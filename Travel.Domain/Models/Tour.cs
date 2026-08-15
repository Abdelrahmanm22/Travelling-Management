namespace Travel.Domain.Models
{
    /// <summary>
    /// An optional excursion a booking can add on top of its package — the catalogue entry, not the
    /// sale. <see cref="PricePerPerson"/> is the standard rate; what a booking was actually sold at
    /// is snapshotted on <see cref="BookingTour.UnitPrice"/>, so re-pricing the catalogue never
    /// rewrites a past booking.
    /// </summary>
    public class Tour : BaseModel
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal PricePerPerson { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
