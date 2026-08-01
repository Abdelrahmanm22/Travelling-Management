using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services;
using Travel.Domain.Services.Identity;

namespace Travel.Services.Identity
{
    public class UserAccountService : IUserAccountService
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IUnitOfWork _unitOfWork;

        public UserAccountService(
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IUnitOfWork unitOfWork)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<UserSummaryDto>> GetAllAsync()
        {
            var users = await _userManager.Users
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .ToListAsync();

            var summaries = new List<UserSummaryDto>(users.Count);
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                summaries.Add(new UserSummaryDto(
                    user.Id,
                    user.FullName,
                    user.UserName ?? string.Empty,
                    user.Email ?? string.Empty,
                    user.PhoneNumber,
                    user.ImageUrl,
                    user.Gender,
                    user.IsActive,
                    roles.OrderBy(r => r).ToList()));
            }

            return summaries;
        }

        public async Task<UserDetailsDto?> GetAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return null;

            var roles = (await _userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();
            var direct = await GetDirectPermissionsAsync(user);
            var inherited = await GetInheritedPermissionsAsync(user);

            return new UserDetailsDto(
                user.Id,
                user.FirstName,
                user.LastName,
                user.FullName,
                user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                user.PhoneNumber,
                user.Gender,
                user.IsActive,
                user.ImageUrl,
                roles,
                direct.ToList(),
                inherited);
        }

        public async Task<OperationResult<string>> CreateAsync(CreateUserRequest request)
        {
            var user = new AppUser
            {
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                UserName = request.UserName.Trim(),
                Email = request.Email.Trim(),
                PhoneNumber = request.PhoneNumber,
                Gender = request.Gender,
                IsActive = request.IsActive,
                ImageUrl = request.ImageUrl,
            };

            var roles = await SanitizeRolesAsync(request.Roles);

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var created = await _userManager.CreateAsync(user, request.Password);
                if (!created.Succeeded)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return OperationResult<string>.From(created);
                }

                if (roles.Count > 0)
                {
                    var assigned = await _userManager.AddToRolesAsync(user, roles);
                    if (!assigned.Succeeded)
                    {
                        await _unitOfWork.RollbackTransactionAsync();
                        return OperationResult<string>.From(assigned);
                    }
                }

                await _unitOfWork.CommitTransactionAsync();
                return OperationResult<string>.Ok(user.Id);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<OperationResult> UpdateAsync(UpdateUserRequest request, string currentUserId)
        {
            var user = await _userManager.FindByIdAsync(request.Id);
            if (user is null)
                return OperationResult.Fail("User not found.");

            var desiredRoles = await SanitizeRolesAsync(request.Roles);
            var currentRoles = await _userManager.GetRolesAsync(user);

            var losingSuperAdmin = currentRoles.Contains(AppRoles.SuperAdmin, StringComparer.OrdinalIgnoreCase)
                && !desiredRoles.Contains(AppRoles.SuperAdmin, StringComparer.OrdinalIgnoreCase);

            var guard = await GuardSuperAdminAsync(user, currentUserId, losingSuperAdmin, "remove SuperAdmin from");
            if (guard is not null)
                return guard;

            if (!request.IsActive && user.IsActive)
            {
                var deactivateGuard = await GuardDeactivationAsync(user, currentUserId, "deactivate");
                if (deactivateGuard is not null)
                    return deactivateGuard;
            }

            var rolesChanged = losingSuperAdmin
                || desiredRoles.Count != currentRoles.Count
                || desiredRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).Any();

            var deactivated = user.IsActive && !request.IsActive;

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                user.FirstName = request.FirstName.Trim();
                user.LastName = request.LastName.Trim();
                user.Gender = request.Gender;
                user.IsActive = request.IsActive;
                if (request.ImageUrl is not null)
                    user.ImageUrl = request.ImageUrl;
                user.PhoneNumber = request.PhoneNumber;

                var nameResult = await _userManager.SetUserNameAsync(user, request.UserName.Trim());
                if (!nameResult.Succeeded)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return OperationResult.From(nameResult);
                }

                var emailResult = await _userManager.SetEmailAsync(user, request.Email.Trim());
                if (!emailResult.Succeeded)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return OperationResult.From(emailResult);
                }

                var updated = await _userManager.UpdateAsync(user);
                if (!updated.Succeeded)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return OperationResult.From(updated);
                }

                if (rolesChanged)
                {
                    var removed = currentRoles.Except(desiredRoles, StringComparer.OrdinalIgnoreCase).ToList();
                    if (removed.Count > 0)
                    {
                        var removeResult = await _userManager.RemoveFromRolesAsync(user, removed);
                        if (!removeResult.Succeeded)
                        {
                            await _unitOfWork.RollbackTransactionAsync();
                            return OperationResult.From(removeResult);
                        }
                    }

                    var added = desiredRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
                    if (added.Count > 0)
                    {
                        var addResult = await _userManager.AddToRolesAsync(user, added);
                        if (!addResult.Succeeded)
                        {
                            await _unitOfWork.RollbackTransactionAsync();
                            return OperationResult.From(addResult);
                        }
                    }
                }

                // A role change or a deactivation is a de-escalation — end existing sessions rather
                // than waiting for the cookie to refresh.
                if (rolesChanged || deactivated)
                    await _userManager.UpdateSecurityStampAsync(user);

                await _unitOfWork.CommitTransactionAsync();
                return OperationResult.Ok();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<OperationResult> DeleteAsync(string userId, string currentUserId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return OperationResult.Fail("User not found.");

            if (user.Id == currentUserId)
                return OperationResult.Fail("You can't delete your own account.");

            var guard = await GuardSuperAdminAsync(user, currentUserId, losingSuperAdmin: true, "delete");
            if (guard is not null)
                return guard;

            return OperationResult.From(await _userManager.DeleteAsync(user));
        }

        public async Task<OperationResult> SetActiveAsync(string userId, bool isActive, string currentUserId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return OperationResult.Fail("User not found.");

            if (!isActive)
            {
                var guard = await GuardDeactivationAsync(user, currentUserId, "deactivate");
                if (guard is not null)
                    return guard;
            }

            user.IsActive = isActive;
            var updated = await _userManager.UpdateAsync(user);
            if (!updated.Succeeded)
                return OperationResult.From(updated);

            // IsActive is only checked at login, so end any live session explicitly.
            if (!isActive)
                await _userManager.UpdateSecurityStampAsync(user);

            return OperationResult.Ok();
        }

        public async Task<OperationResult> ResetPasswordAsync(string userId, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return OperationResult.Fail("User not found.");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            return OperationResult.From(await _userManager.ResetPasswordAsync(user, token, newPassword));
        }

        public async Task<OperationResult> ReplaceDirectPermissionsAsync(string userId, IEnumerable<string> permissions)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return OperationResult.Fail("User not found.");

            var inherited = (await GetInheritedPermissionsAsync(user)).Keys.ToHashSet(StringComparer.Ordinal);
            var existing = (await _userManager.GetClaimsAsync(user))
                .Where(c => c.Type == AppClaimTypes.Permission)
                .ToList();

            var desired = PermissionCatalog.Sanitize(permissions).ToHashSet(StringComparer.Ordinal);

            // Never store an inherited grant as a direct one — that would hide a later de-escalation.
            desired.ExceptWith(inherited);

            // Inherited rows render disabled and so never post; keep any that were already granted
            // directly, so an explicit grant survives a later change to the covering role.
            foreach (var value in existing.Select(c => c.Value).Where(inherited.Contains))
                desired.Add(value);

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                foreach (var stale in existing.Where(c => !desired.Contains(c.Value)))
                    await _userManager.RemoveClaimAsync(user, stale);

                var have = existing.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
                foreach (var added in desired.Where(p => !have.Contains(p)))
                    await _userManager.AddClaimAsync(user, new Claim(AppClaimTypes.Permission, added));

                await _unitOfWork.CommitTransactionAsync();
                return OperationResult.Ok();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<bool> IsUserNameAvailableAsync(string userName, string? excludeUserId)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return true;

            var user = await _userManager.FindByNameAsync(userName.Trim());
            return user is null || user.Id == excludeUserId;
        }

        public async Task<bool> IsEmailAvailableAsync(string email, string? excludeUserId)
        {
            if (string.IsNullOrWhiteSpace(email))
                return true;

            var user = await _userManager.FindByEmailAsync(email.Trim());
            return user is null || user.Id == excludeUserId;
        }

        private async Task<IReadOnlyList<string>> GetDirectPermissionsAsync(AppUser user) =>
            (await _userManager.GetClaimsAsync(user))
                .Where(c => c.Type == AppClaimTypes.Permission)
                .Select(c => c.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetInheritedPermissionsAsync(AppUser user)
        {
            var inherited = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var roleName in await _userManager.GetRolesAsync(user))
            {
                // SuperAdmin holds no claims but grants everything.
                if (AppRoles.IsProtected(roleName))
                {
                    foreach (var descriptor in PermissionCatalog.All)
                        AddSource(inherited, descriptor.Value, roleName);

                    continue;
                }

                var role = await _roleManager.FindByNameAsync(roleName);
                if (role is null)
                    continue;

                foreach (var claim in (await _roleManager.GetClaimsAsync(role))
                             .Where(c => c.Type == AppClaimTypes.Permission))
                {
                    AddSource(inherited, claim.Value, roleName);
                }
            }

            return inherited.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value,
                StringComparer.Ordinal);

            static void AddSource(Dictionary<string, List<string>> map, string permission, string roleName)
            {
                if (!map.TryGetValue(permission, out var sources))
                    map[permission] = sources = new List<string>();

                if (!sources.Contains(roleName, StringComparer.OrdinalIgnoreCase))
                    sources.Add(roleName);
            }
        }

        private async Task<List<string>> SanitizeRolesAsync(IEnumerable<string>? roles)
        {
            var requested = (roles ?? Enumerable.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var valid = new List<string>(requested.Count);
            foreach (var roleName in requested)
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                if (role?.Name is not null)
                    valid.Add(role.Name);
            }

            return valid;
        }

        /// <summary>
        /// Blocks any change that would strip the caller's own SuperAdmin access or leave the system
        /// with no active SuperAdmin at all.
        /// </summary>
        private async Task<OperationResult?> GuardSuperAdminAsync(
            AppUser target, string currentUserId, bool losingSuperAdmin, string verb)
        {
            if (!losingSuperAdmin)
                return null;

            if (!await _userManager.IsInRoleAsync(target, AppRoles.SuperAdmin))
                return null;

            if (target.Id == currentUserId)
                return OperationResult.Fail($"You can't {verb} your own SuperAdmin access.");

            var superAdmins = await _userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
            if (superAdmins.Count(u => u.IsActive) <= 1)
                return OperationResult.Fail(
                    $"This is the last active SuperAdmin — you can't {verb} it. " +
                    "Grant SuperAdmin to another user first.");

            return null;
        }

        private async Task<OperationResult?> GuardDeactivationAsync(AppUser target, string currentUserId, string verb)
        {
            if (target.Id == currentUserId)
                return OperationResult.Fail($"You can't {verb} your own account.");

            return await GuardSuperAdminAsync(target, currentUserId, losingSuperAdmin: true, verb);
        }
    }
}
