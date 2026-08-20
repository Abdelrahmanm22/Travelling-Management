using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Travel.Domain.Authorization;

namespace Travel.Presentation.Authorization
{
    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var user = context.User;

            if (user?.Identity?.IsAuthenticated != true)
                return Task.CompletedTask;

            // SuperAdmin implicitly holds every permission, so it carries none as claims.
            if (user.IsInRole(AppRoles.SuperAdmin))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Not HasClaim: the principal carries every permission packed into one comma-joined claim
            // to keep the cookie small. Unpack also reads the one-claim-per-permission shape, so a
            // cookie issued before packing shipped still authorises correctly.
            var granted = user.FindAll(AppClaimTypes.Permission)
                .SelectMany(claim => PermissionClaims.Unpack(claim.Value));

            if (granted.Contains(requirement.Permission, StringComparer.Ordinal))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}
