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
            BlockStatus.NeedsAmendment => new("Needs amendment", "bg-danger-subtle text-danger", "bx-error-circle"),
            _ => new("Not booked", "bg-secondary-subtle text-secondary", "bx-block"),
        };

        public static StatusBadge ForBooking(BookingStatus status) => status switch
        {
            BookingStatus.Confirmed => new("Confirmed", "bg-success-subtle text-success", "bx-check-circle"),
            BookingStatus.Completed => new("Completed", "bg-info-subtle text-info", "bx-check-double"),
            BookingStatus.Cancelled => new("Cancelled", "bg-danger-subtle text-danger", "bx-x-circle"),
            _ => new("In progress", "bg-warning-subtle text-warning", "bx-time-five"),
        };

        public static StatusBadge ForTransaction(TransactionType type) => type switch
        {
            TransactionType.Refund => new("Refund", "bg-danger-subtle text-danger", "bx-undo"),
            _ => new("Payment", "bg-success-subtle text-success", "bx-check-circle"),
        };

        public static StatusBadge ForMethod(PaymentMethod method) => method switch
        {
            PaymentMethod.BankTransfer => new("Bank transfer", "bg-info-subtle text-info", "bx-transfer"),
            PaymentMethod.CreditCard => new("Credit card", "bg-primary-subtle text-primary", "bx-credit-card"),
            _ => new("Cash", "bg-secondary-subtle text-secondary", "bx-money"),
        };

        public static StatusBadge ForSafeEntry(SafeEntryType type) => type switch
        {
            SafeEntryType.Withdrawal => new("Withdrawal", "bg-warning-subtle text-warning", "bx-minus-circle"),
            SafeEntryType.Expense => new("Expense", "bg-danger-subtle text-danger", "bx-receipt"),
            _ => new("Deposit", "bg-success-subtle text-success", "bx-plus-circle"),
        };

        public static StatusBadge ForExpenseBasis(ExpenseBasis basis) => basis switch
        {
            ExpenseBasis.PerPax => new("Per pax", "bg-primary-subtle text-primary", "bx-group"),
            _ => new("Per room", "bg-info-subtle text-info", "bx-bed"),
        };

        public static StatusBadge ForAudit(AuditChangeKind kind) => kind switch
        {
            AuditChangeKind.Added => new("Added", "bg-success-subtle text-success", "bx-plus-circle"),
            AuditChangeKind.Removed => new("Removed", "bg-danger-subtle text-danger", "bx-minus-circle"),
            _ => new("Changed", "bg-info-subtle text-info", "bx-edit-alt"),
        };
    }
}
