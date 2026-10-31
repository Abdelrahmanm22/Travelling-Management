namespace Travel.Domain.Authorization
{
    /// <summary>
    /// Every permission in the system. Values must be compile-time constants so they can be used
    /// in <c>[Authorize(Policy = ...)]</c>. Add a matching entry to <see cref="PermissionCatalog"/>
    /// whenever a constant is added here.
    /// </summary>
    public static class Permissions
    {
        public static class Bookings
        {
            public const string View = "Bookings.View";
            public const string Create = "Bookings.Create";
            public const string Edit = "Bookings.Edit";
            public const string Delete = "Bookings.Delete";
            public const string Regenerate = "Bookings.Regenerate";
            public const string Export = "Bookings.Export";
            public const string ChangeStatus = "Bookings.ChangeStatus";
            public const string ViewPricing = "Bookings.ViewPricing";
            public const string EditPricing = "Bookings.EditPricing";
            public const string ViewAudit = "Bookings.ViewAudit";
            public const string Voucher = "Bookings.Voucher";
        }

        public static class Tours
        {
            public const string View = "Tours.View";
            public const string Create = "Tours.Create";
            public const string Edit = "Tours.Edit";
            public const string Delete = "Tours.Delete";
        }

        public static class Transactions
        {
            public const string View = "Transactions.View";
            public const string Create = "Transactions.Create";
            public const string Edit = "Transactions.Edit";
            public const string Delete = "Transactions.Delete";
            public const string Export = "Transactions.Export";
        }

        public static class BlockServices
        {
            public const string View = "BlockServices.View";
            public const string Export = "BlockServices.Export";
            public const string ChangeStatus = "BlockServices.ChangeStatus";
            public const string ManageDocuments = "BlockServices.ManageDocuments";
        }

        public static class Packages
        {
            public const string View = "Packages.View";
            public const string Create = "Packages.Create";
            public const string Edit = "Packages.Edit";
            public const string Delete = "Packages.Delete";
        }

        public static class Destinations
        {
            public const string View = "Destinations.View";
            public const string Create = "Destinations.Create";
            public const string Edit = "Destinations.Edit";
            public const string Delete = "Destinations.Delete";
        }

        public static class Nationalities
        {
            public const string View = "Nationalities.View";
            public const string Create = "Nationalities.Create";
            public const string Edit = "Nationalities.Edit";
            public const string Delete = "Nationalities.Delete";
        }

        /// <summary>One pair per report — a blanket Reports.View couldn't grant one without granting all.</summary>
        public static class Reports
        {
            public const string ViewFlights = "Reports.ViewFlights";
            public const string ExportFlights = "Reports.ExportFlights";
            public const string ViewRevenue = "Reports.ViewRevenue";
            public const string ExportRevenue = "Reports.ExportRevenue";
        }

        public static class Users
        {
            public const string View = "Users.View";
            public const string Create = "Users.Create";
            public const string Edit = "Users.Edit";
            public const string Delete = "Users.Delete";
            public const string ToggleActive = "Users.ToggleActive";
            public const string ResetPassword = "Users.ResetPassword";
            public const string AssignRoles = "Users.AssignRoles";
            public const string ManagePermissions = "Users.ManagePermissions";
        }

        public static class Roles
        {
            public const string View = "Roles.View";
            public const string Create = "Roles.Create";
            public const string Edit = "Roles.Edit";
            public const string Delete = "Roles.Delete";
            public const string ManagePermissions = "Roles.ManagePermissions";
        }

        /// <summary>
        /// A safe's own grants also cover the money moving through it: <see cref="Edit"/> records or
        /// corrects a deposit/withdrawal, <see cref="Delete"/> removes one. The catalog descriptions
        /// say so, so the mapping is visible in the picker rather than buried here.
        /// </summary>
        public static class MoneySafes
        {
            public const string View = "MoneySafes.View";
            public const string Create = "MoneySafes.Create";
            public const string Edit = "MoneySafes.Edit";
            public const string Delete = "MoneySafes.Delete";
            public const string Export = "MoneySafes.Export";
        }

        public static class ExpenseTypes
        {
            public const string View = "ExpenseTypes.View";
            public const string Create = "ExpenseTypes.Create";
            public const string Edit = "ExpenseTypes.Edit";
            public const string Delete = "ExpenseTypes.Delete";
        }

        public static class Expenses
        {
            public const string View = "Expenses.View";
            public const string Create = "Expenses.Create";
            public const string Edit = "Expenses.Edit";
            public const string Delete = "Expenses.Delete";
            public const string Export = "Expenses.Export";
        }
    }
}
