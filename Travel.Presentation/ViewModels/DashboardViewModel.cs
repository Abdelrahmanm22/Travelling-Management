using System.Globalization;
using Travel.Domain.Services.Dashboard;

namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// The Home page. Each section is null unless the user holds its <c>Dashboard.*</c> permission —
    /// the controller never even queries a hidden section, so its figures can't reach the HTML.
    /// </summary>
    public class DashboardViewModel
    {
        public string FirstName { get; init; } = string.Empty;
        public DateOnly Today { get; init; }

        public DashboardPeriod Period { get; init; } = null!;

        public OperationsStats? Operations { get; init; }
        public SalesStats? Sales { get; init; }
        public PaymentsStats? Payments { get; init; }
        public SafesStats? Safes { get; init; }

        public bool HasAnySection => Operations is not null || Sales is not null || Payments is not null || Safes is not null;

        // Whether a figure may link to the page it summarises. A user can hold a Dashboard permission
        // without the target page's — then the figure renders as plain text, never a link to AccessDenied.
        public bool CanOpenBookings { get; init; }
        public bool CanOpenBlocks { get; init; }
        public bool CanOpenRevenue { get; init; }
        public bool CanOpenTransactions { get; init; }
        public bool CanOpenSafes { get; init; }
        public bool CanOpenExpenses { get; init; }
    }

    /// <summary>
    /// One stat tile, rendered by <c>Views/Home/_DashboardStat.cshtml</c>. <see cref="Currency"/> set
    /// means <see cref="Value"/> is an amount and gets the small currency prefix. <see cref="Href"/>
    /// null renders a plain tile — never a link the viewer can't open.
    /// </summary>
    public sealed record DashboardTile(
        string Label,
        string Icon,
        string Value,
        string? Sub = null,
        string? Currency = null,
        string? Href = null,
        string? ValueCss = null,
        string? SubCss = null);

    /// <summary>
    /// The picked period. "month" (the default), "lastMonth", "year" or "custom" — resolved once, so
    /// every section reads the same From/To.
    /// </summary>
    public sealed record DashboardPeriod(string Key, DateOnly From, DateOnly To, string Label)
    {
        public const string ThisMonth = "month";
        public const string LastMonth = "lastMonth";
        public const string ThisYear = "year";
        public const string Custom = "custom";

        public bool IsCustom => Key == Custom;

        /// <summary>"01 Nov 2026 – 30 Nov 2026".</summary>
        public string RangeText => $"{Format(From)} – {Format(To)}";

        public static DashboardPeriod Resolve(string? key, DateOnly? from, DateOnly? to, DateOnly today)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            switch (key)
            {
                case LastMonth:
                    var last = monthStart.AddMonths(-1);
                    return new(LastMonth, last, monthStart.AddDays(-1), MonthLabel(last));

                case ThisYear:
                    return new(ThisYear, new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31),
                               today.Year.ToString(CultureInfo.InvariantCulture));

                case Custom when from is not null || to is not null:
                    // One box left empty means a one-day range on the other; reversed boxes are swapped
                    // rather than producing an empty, silent dashboard.
                    var a = from ?? to!.Value;
                    var b = to ?? from!.Value;
                    if (a > b) (a, b) = (b, a);
                    return new(Custom, a, b, $"{Format(a)} – {Format(b)}");

                default:
                    return new(ThisMonth, monthStart, monthStart.AddMonths(1).AddDays(-1), MonthLabel(monthStart));
            }
        }

        private static string MonthLabel(DateOnly month) => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        private static string Format(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }
}
