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
    public class RoleService : IRoleService
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly IUnitOfWork _unitOfWork;

        public RoleService(
            RoleManager<IdentityRole> roleManager,
            UserManager<AppUser> userManager,
            IUnitOfWork unitOfWork)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<RoleSummaryDto>> GetAllAsync()
        {
            var roles = await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync();
            var summaries = new List<RoleSummaryDto>(roles.Count);

            foreach (var role in roles)
            {
                var permissionCount = (await _roleManager.GetClaimsAsync(role))
                    .Count(c => c.Type == AppClaimTypes.Permission);

                var userCount = (await _userManager.GetUsersInRoleAsync(role.Name!)).Count;

                summaries.Add(new RoleSummaryDto(
                    role.Id,
                    role.Name ?? string.Empty,
                    userCount,
                    AppRoles.IsProtected(role.Name) ? PermissionCatalog.Count : permissionCount,
                    AppRoles.IsProtected(role.Name)));
            }

            return summaries;
        }

        public async Task<RoleDetailsDto?> GetAsync(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role is null)
                return null;

            var isProtected = AppRoles.IsProtected(role.Name);

            // SuperAdmin holds no claims — it bypasses the handler — but the UI should show it as full access.
            var permissions = isProtected
                ? PermissionCatalog.All.Select(p => p.Value).ToList()
                : (await _roleManager.GetClaimsAsync(role))
                    .Where(c => c.Type == AppClaimTypes.Permission)
                    .Select(c => c.Value)
                    .ToList();

            var members = (await _userManager.GetUsersInRoleAsync(role.Name!))
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .Select(u => new RoleMemberDto(u.Id, u.FullName, u.UserName ?? string.Empty, u.ImageUrl, u.Gender, u.IsActive))
                .ToList();

            return new RoleDetailsDto(role.Id, role.Name ?? string.Empty, isProtected, permissions, members);
        }

        public async Task<IReadOnlyList<string>> GetPermissionsAsync(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role is null)
                return Array.Empty<string>();

            if (AppRoles.IsProtected(role.Name))
                return PermissionCatalog.All.Select(p => p.Value).ToList();

            return (await _roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == AppClaimTypes.Permission)
                .Select(c => c.Value)
                .ToList();
        }

        public async Task<OperationResult<string>> CreateAsync(string name, IEnumerable<string> permissions)
        {
            if (AppRoles.IsProtected(name))
                return OperationResult<string>.Fail($"\"{name}\" is a reserved role name.");

            if (await _roleManager.FindByNameAsync(name) is not null)
                return OperationResult<string>.Fail("A role with this name already exists.");

            var role = new IdentityRole(name.Trim());
            var desired = PermissionCatalog.Sanitize(permissions);

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var created = await _roleManager.CreateAsync(role);
                if (!created.Succeeded)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return OperationResult<string>.From(created);
                }

                foreach (var permission in desired)
                    await _roleManager.AddClaimAsync(role, new Claim(AppClaimTypes.Permission, permission));

                await _unitOfWork.CommitTransactionAsync();
                return OperationResult<string>.Ok(role.Id);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<OperationResult> UpdateAsync(string roleId, string name, IEnumerable<string> permissions)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role is null)
                return OperationResult.Fail("Role not found.");

            if (AppRoles.IsProtected(role.Name))
                return OperationResult.Fail($"The {role.Name} role can't be modified.");

            if (AppRoles.IsProtected(name))
                return OperationResult.Fail($"\"{name}\" is a reserved role name.");

            var duplicate = await _roleManager.FindByNameAsync(name);
            if (duplicate is not null && duplicate.Id != role.Id)
                return OperationResult.Fail("A role with this name already exists.");

            var desired = PermissionCatalog.Sanitize(permissions).ToHashSet(StringComparer.Ordinal);
            var current = (await _roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == AppClaimTypes.Permission)
                .ToList();

            // The role store saves on every Add/RemoveClaimAsync, so wrap the whole diff in one
            // transaction. It shares the scoped TravelContext with UnitOfWork, so this is atomic.
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (!string.Equals(role.Name, name, StringComparison.Ordinal))
                {
                    var renamed = await _roleManager.SetRoleNameAsync(role, name.Trim());
                    if (!renamed.Succeeded)
                    {
                        await _unitOfWork.RollbackTransactionAsync();
                        return OperationResult.From(renamed);
                    }

                    var updated = await _roleManager.UpdateAsync(role);
                    if (!updated.Succeeded)
                    {
                        await _unitOfWork.RollbackTransactionAsync();
                        return OperationResult.From(updated);
                    }
                }

                foreach (var stale in current.Where(c => !desired.Contains(c.Value)))
                    await _roleManager.RemoveClaimAsync(role, stale);

                var existing = current.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
                foreach (var added in desired.Where(p => !existing.Contains(p)))
                    await _roleManager.AddClaimAsync(role, new Claim(AppClaimTypes.Permission, added));

                await _unitOfWork.CommitTransactionAsync();
                return OperationResult.Ok();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<OperationResult> DeleteAsync(string roleId)
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role is null)
                return OperationResult.Fail("Role not found.");

            if (AppRoles.IsProtected(role.Name))
                return OperationResult.Fail($"The {role.Name} role can't be deleted.");

            var members = await _userManager.GetUsersInRoleAsync(role.Name!);
            if (members.Count > 0)
                return OperationResult.Fail(
                    $"This role is assigned to {members.Count} user(s) and can't be deleted.");

            return OperationResult.From(await _roleManager.DeleteAsync(role));
        }

        public async Task<bool> IsNameAvailableAsync(string name, string? excludeRoleId)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            if (AppRoles.IsProtected(name))
                return false;

            var role = await _roleManager.FindByNameAsync(name.Trim());
            return role is null || role.Id == excludeRoleId;
        }
    }
}
