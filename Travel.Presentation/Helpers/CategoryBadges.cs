using Travel.Domain.Enums;

namespace Travel.Presentation.Helpers
{
    /// <summary>
    /// The single source of truth for how a <see cref="PackageCategory"/> reads on screen — the same
    /// role <see cref="StatusBadges"/> plays for statuses. The category is part of an arrival group's
    /// identity (a 4★ and a 5★ booking on the same date are two blocks), so it is printed beside every
    /// group label and the spelling must not drift between pages. Excel text lives in
    /// <c>ExcelFormat.CategoryText</c>.
    /// </summary>
    public static class CategoryBadges
    {
        /// <summary>Compact form for badges and group labels: "4★" / "5★".</summary>
        public static string Short(PackageCategory category) =>
            category == PackageCategory.FiveStars ? "5★" : "4★";

        /// <summary>Long form for filter options and page titles: "4 Stars" / "5 Stars".</summary>
        public static string Label(PackageCategory category) =>
            category == PackageCategory.FiveStars ? "5 Stars" : "4 Stars";

        /// <summary>
        /// Badge classes — a neutral tag (app-extras.css "Category & Private tags"), never a status hue:
        /// 5★ used to borrow In progress's amber and 4★ Not booked's grey. 5★ is a solid ink pill and
        /// 4★ an outline, so the two halves of a date still tell apart at a glance.
        /// </summary>
        public static string Css(PackageCategory category) =>
            category == PackageCategory.FiveStars
                ? "badge badge-tag badge-tag-5"
                : "badge badge-tag badge-tag-4";

        /// <summary>
        /// "Cairo · 5★ · 01 Aug 2026" — how an arrival group is named in a list or a chip. A private
        /// booking's own file names the booking — "Cairo · 5★ · Private MUSB-000003 · 01 Aug 2026" — so
        /// it can't be mistaken for the shared block of the same destination, date and category.
        /// </summary>
        public static string GroupLabel(
            string destinationName, PackageCategory category, DateOnly checkIn, string? privateBookingCode = null) =>
            privateBookingCode is null
                ? $"{destinationName} · {Short(category)} · {checkIn:dd MMM yyyy}"
                : $"{destinationName} · {Short(category)} · Private {privateBookingCode} · {checkIn:dd MMM yyyy}";

        /// <summary>
        /// The badge a private booking's file (and the booking itself) carries. The spelling is kept
        /// because expense-form.js builds the same badge client-side; app-extras.css styles
        /// <c>.badge.bg-dark-subtle</c> as the Private tag, so it must not be reused for anything else.
        /// </summary>
        public const string PrivateCss = "badge bg-dark-subtle text-dark";
    }
}
