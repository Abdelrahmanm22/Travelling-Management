using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Travel.Domain.Authorization;
using Travel.Domain.Models.Identity;

namespace Travel.Presentation.Authorization
{
    /// <summary>
    /// The base factory already unions AspNetRoleClaims and AspNetUserClaims into the principal, so
    /// this override exists to tidy the result: de-duplicate permission claims, drop claims for
    /// permissions no longer in the catalog, and emit none at all for SuperAdmin (which bypasses the
    /// handler anyway) to keep the auth cookie small.
    /// </summary>
    public sealed class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, IdentityRole>
    {
        public AppUserClaimsPrincipalFactory(
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<IdentityOptions> options)
            : base(userManager, roleManager, options)
        {
        }

        public override async Task<ClaimsPrincipal> CreateAsync(AppUser user)
        {
            var principal = await base.CreateAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;

            var permissionClaims = identity.FindAll(AppClaimTypes.Permission).ToList();
            foreach (var claim in permissionClaims)
                identity.RemoveClaim(claim);

            if (!identity.HasClaim(identity.RoleClaimType, AppRoles.SuperAdmin))
            {
                var effective = permissionClaims
                    .Select(c => c.Value)
                    .Where(PermissionCatalog.IsValid)
                    .Distinct(StringComparer.Ordinal);

                identity.AddClaims(effective.Select(p => new Claim(AppClaimTypes.Permission, p)));
            }

            return principal;
        }
    }
}
