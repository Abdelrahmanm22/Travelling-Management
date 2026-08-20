using System;
using System.Collections.Generic;
using System.Linq;

namespace Travel.Domain.Authorization
{
    /// <summary>
    /// How a user's effective permissions travel inside the auth cookie.
    ///
    /// <para>
    /// One claim per permission is the natural shape, and it is what the <b>database</b> still stores
    /// (a row each in AspNetRoleClaims / AspNetUserClaims). But the cookie carries every claim on every
    /// request, and past ~60 permissions that gets uncomfortable — so the principal packs them into a
    /// single comma-joined claim instead. This class is the only owner of that format, so the packing
    /// side (<c>AppUserClaimsPrincipalFactory</c>) and the reading side
    /// (<c>PermissionAuthorizationHandler</c>) cannot drift — the same reason
    /// <c>BlockConfirmation</c> owns the room fingerprint alone.
    /// </para>
    ///
    /// <para>
    /// <see cref="Unpack"/> reads the un-packed shape too: a lone value contains no separator and
    /// splits to itself. That is what lets cookies issued before packing shipped keep working until
    /// the SecurityStampValidator tick re-issues them — nobody is signed out by the change.
    /// </para>
    /// </summary>
    public static class PermissionClaims
    {
        /// <summary>Safe as a separator because every permission value is shaped "Module.Action".</summary>
        private const char Separator = ',';

        public static string Pack(IEnumerable<string> permissions) =>
            string.Join(Separator, permissions.OrderBy(p => p, StringComparer.Ordinal));

        public static IEnumerable<string> Unpack(string? value) =>
            string.IsNullOrEmpty(value)
                ? Enumerable.Empty<string>()
                : value.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
    }
}
