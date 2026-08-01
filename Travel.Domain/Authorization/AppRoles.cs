using System;

namespace Travel.Domain.Authorization
{
    public static class AppRoles
    {
        /// <summary>
        /// Implicitly holds every permission — <c>PermissionAuthorizationHandler</c> short-circuits for it.
        /// Seeded on startup; never editable or deletable from the UI.
        /// </summary>
        public const string SuperAdmin = "SuperAdmin";

        /// <summary>Roles the UI must never allow renaming, re-permissioning or deleting.</summary>
        public static bool IsProtected(string? roleName) =>
            string.Equals(roleName, SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }
}
