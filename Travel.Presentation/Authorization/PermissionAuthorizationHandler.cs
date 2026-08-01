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

            if (user.HasClaim(AppClaimTypes.Permission, requirement.Permission))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}
