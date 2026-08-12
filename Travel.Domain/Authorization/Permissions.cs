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

        public static class Reports
        {
            public const string ViewFlights = "Reports.ViewFlights";
            public const string ExportFlights = "Reports.ExportFlights";
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
    }
}
