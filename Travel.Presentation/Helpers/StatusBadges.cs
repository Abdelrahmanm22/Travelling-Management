using Travel.Domain.Enums;

namespace Travel.Presentation.Helpers
{
    /// <summary>How a status looks and reads. Label, badge classes and icon in one place.</summary>
    public sealed record StatusBadge(string Label, string CssClass, string Icon);

    /// <summary>
    /// The single source of truth for status presentation, mirroring <see cref="PermissionIcons"/>.
    /// Enum display strings are otherwise hand-written per view in this project, which is how the same
    /// value ends up spelled three different ways.
    ///
    /// <para>
    /// The <c>bg-*-subtle</c> classes come from <c>wwwroot/css/app-extras.css</c> — the Skote template
    /// is Bootstrap 5.0.0-beta2 and ships none of them. Every icon here is verified present in
    /// <c>icons.min.css</c>; a missing Boxicon renders as blank space with no error.
    /// </para>
    /// </summary>
    public static class StatusBadges
    {
        public static StatusBadge ForBlock(BlockStatus status) => status switch
        {
            BlockStatus.InProgress => new("In progress", "bg-warning-subtle text-warning", "bx-time-five"),
            BlockStatus.Confirmed => new("Confirmed", "bg-success-subtle text-success", "bx-check-circle"),
            _ => new("Not booked", "bg-secondary-subtle text-secondary", "bx-block"),
        };

        public static StatusBadge ForBooking(BookingStatus status) => status switch
        {
            BookingStatus.Confirmed => new("Confirmed", "bg-success-subtle text-success", "bx-check-circle"),
            BookingStatus.Completed => new("Completed", "bg-info-subtle text-info", "bx-check-double"),
            BookingStatus.Cancelled => new("Cancelled", "bg-danger-subtle text-danger", "bx-x-circle"),
            _ => new("In progress", "bg-warning-subtle text-warning", "bx-time-five"),
        };
    }
}
