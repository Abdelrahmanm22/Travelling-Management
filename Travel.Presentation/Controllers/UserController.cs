using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Authorization;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services;
using Travel.Domain.Services.Identity;
using Travel.Presentation.Helpers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class UserController : Controller
    {
        private const string UsersFolder = "Users";

        private readonly IUserAccountService _userAccounts;
        private readonly IRoleService _roleService;
        private readonly IFileStorageService _fileStorage;
        private readonly IAuthorizationService _authorization;
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;

        public UserController(
            IUserAccountService userAccounts,
            IRoleService roleService,
            IFileStorageService fileStorage,
            IAuthorizationService authorization,
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager)
        {
            _userAccounts = userAccounts;
            _roleService = roleService;
            _fileStorage = fileStorage;
            _authorization = authorization;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> Index()
        {
            var users = await _userAccounts.GetAllAsync();
            return View(users);
        }

        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> Details(string id)
        {
            var user = await _userAccounts.GetAsync(id);
            if (user is null) return NotFound();

            return View(user);
        }

        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<IActionResult> Create()
        {
            var model = new UserFormViewModel();
            await PopulateRolesAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Users.Create)]
        public async Task<IActionResult> Create(UserFormViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError(nameof(model.Password), "Password is required.");

            if (!await CanAssignRolesAsync(model.SelectedRoles))
            {
                TempData["Error"] = "You don't have permission to assign roles.";
                return RedirectToAction(nameof(Index));
            }

            ValidateProfileImage(model);

            if (!ModelState.IsValid)
            {
                await PopulateRolesAsync(model);
                return View(model);
            }

            var request = new CreateUserRequest(
                model.FirstName,
                model.LastName,
                model.UserName,
                model.Email,
                model.PhoneNumber,
                model.Gender,
                model.IsActive,
                model.Password!,
                await SaveProfileImageAsync(model.ProfileImage),
                model.SelectedRoles);

            var result = await _userAccounts.CreateAsync(request);
            if (!result.Succeeded)
            {
                AddErrors(result);
                await PopulateRolesAsync(model);
                return View(model);
            }

            TempData["Success"] = "User created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> Edit(string id)
        {
            var user = await _userAccounts.GetAsync(id);
            if (user is null) return NotFound();

            var model = new UserFormViewModel
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                UserName = user.UserName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Gender = user.Gender,
                IsActive = user.IsActive,
                ExistingImageUrl = user.ImageUrl,
                SelectedRoles = user.Roles.ToList(),
            };

            await PopulateRolesAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Users.Edit)]
        public async Task<IActionResult> Edit(UserFormViewModel model)
        {
            if (string.IsNullOrEmpty(model.Id)) return NotFound();

            var current = await _userAccounts.GetAsync(model.Id);
            if (current is null) return NotFound();

            // Role changes are gated separately; keep the existing roles if the caller can't assign.
            if (!(await _authorization.AuthorizeAsync(User, Permissions.Users.AssignRoles)).Succeeded)
                model.SelectedRoles = current.Roles.ToList();

            ValidateProfileImage(model);

            if (!ModelState.IsValid)
            {
                await PopulateRolesAsync(model);
                return View(model);
            }

            var imageUrl = await SaveProfileImageAsync(model.ProfileImage);
            if (imageUrl is not null && !string.IsNullOrEmpty(current.ImageUrl))
                _fileStorage.DeleteFile(Path.GetFileName(current.ImageUrl), UsersFolder);

            var request = new UpdateUserRequest(
                model.Id,
                model.FirstName,
                model.LastName,
                model.UserName,
                model.Email,
                model.PhoneNumber,
                model.Gender,
                model.IsActive,
                imageUrl,
                model.SelectedRoles);

            var result = await _userAccounts.UpdateAsync(request, _userManager.GetUserId(User)!);
            if (!result.Succeeded)
            {
                AddErrors(result);
                await PopulateRolesAsync(model);
                return View(model);
            }

            await RefreshCurrentUserAsync(model.Id);

            TempData["Success"] = "User updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Users.Delete)]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userAccounts.GetAsync(id);
            var result = await _userAccounts.DeleteAsync(id, _userManager.GetUserId(User)!);

            if (result.Succeeded && !string.IsNullOrEmpty(user?.ImageUrl))
                _fileStorage.DeleteFile(Path.GetFileName(user.ImageUrl), UsersFolder);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded ? "User deleted successfully." : result.Error,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Users.ToggleActive)]
        public async Task<IActionResult> ToggleActive(string id, bool isActive)
        {
            var result = await _userAccounts.SetActiveAsync(id, isActive, _userManager.GetUserId(User)!);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded
                    ? (isActive ? "User activated." : "User deactivated.")
                    : result.Error,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Users.ResetPassword)]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var message = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Please check the password and try again.";

                return Json(new { success = false, message });
            }

            var result = await _userAccounts.ResetPasswordAsync(model.UserId, model.NewPassword);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded ? "Password reset successfully." : result.Error,
            });
        }

        // Named DirectPermissions in C# (the `Permissions` static class would be shadowed by a
        // member of the same name) but routed as /User/Permissions via [ActionName].
        [HttpGet]
        [ActionName("Permissions")]
        [Authorize(Policy = Permissions.Users.ManagePermissions)]
        public async Task<IActionResult> DirectPermissions(string id)
        {
            var user = await _userAccounts.GetAsync(id);
            if (user is null) return NotFound();

            var model = new UserPermissionsViewModel
            {
                UserId = user.Id,
                FullName = user.FullName,
                Roles = user.Roles.ToList(),
                SelectedPermissions = user.DirectPermissions.ToList(),
            };

            BuildPermissionGroups(model, user);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Permissions")]
        [Authorize(Policy = Permissions.Users.ManagePermissions)]
        public async Task<IActionResult> DirectPermissionsPost(UserPermissionsViewModel model)
        {
            var result = await _userAccounts.ReplaceDirectPermissionsAsync(model.UserId, model.SelectedPermissions);
            if (!result.Succeeded)
            {
                TempData["Error"] = result.Error;
                return RedirectToAction("Permissions", new { id = model.UserId });
            }

            await RefreshCurrentUserAsync(model.UserId);

            TempData["Success"] = "Permissions updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> IsUserNameAvailable(string userName, string? id) =>
            Json(await _userAccounts.IsUserNameAvailableAsync(userName, id));

        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Users.View)]
        public async Task<IActionResult> IsEmailAvailable(string email, string? id) =>
            Json(await _userAccounts.IsEmailAvailableAsync(email, id));

        private async Task<bool> CanAssignRolesAsync(IReadOnlyCollection<string> roles)
        {
            if (roles.Count == 0)
                return true;

            return (await _authorization.AuthorizeAsync(User, Permissions.Users.AssignRoles)).Succeeded;
        }

        /// <summary>Re-issues the cookie right away when an admin changes their own access.</summary>
        private async Task RefreshCurrentUserAsync(string userId)
        {
            if (userId != _userManager.GetUserId(User))
                return;

            var me = await _userManager.FindByIdAsync(userId);
            if (me is not null)
                await _signInManager.RefreshSignInAsync(me);
        }

        private void ValidateProfileImage(UserFormViewModel model)
        {
            if (model.ProfileImage is not null && model.ProfileImage.Length > 0 &&
                !_fileStorage.IsImage(model.ProfileImage.FileName))
            {
                ModelState.AddModelError(nameof(model.ProfileImage), "Only image files are allowed.");
            }
        }

        private async Task<string?> SaveProfileImageAsync(IFormFile? image)
        {
            if (image is null || image.Length == 0)
                return null;

            await using var stream = image.OpenReadStream();
            var fileName = await _fileStorage.SaveFileAsync(stream, image.FileName, UsersFolder);
            return $"/Files/{UsersFolder}/{fileName}";
        }

        private void AddErrors(OperationResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
        }

        private async Task PopulateRolesAsync(UserFormViewModel model)
        {
            var selected = model.SelectedRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);

            model.AvailableRoles = (await _roleService.GetAllAsync())
                .Select(role => new RoleOptionViewModel
                {
                    Name = role.Name,
                    Summary = role.IsProtected
                        ? "Full access to everything"
                        : $"{role.PermissionCount} permission(s)",
                    IsSelected = selected.Contains(role.Name),
                    IsProtected = role.IsProtected,
                })
                .ToList();
        }

        private static void BuildPermissionGroups(UserPermissionsViewModel model, UserDetailsDto user)
        {
            var direct = user.DirectPermissions.ToHashSet(StringComparer.Ordinal);
            var inherited = user.InheritedPermissions;

            model.InheritedCount = inherited.Count;
            model.DirectCount = direct.Count(p => !inherited.ContainsKey(p));

            model.Groups = PermissionCatalog.Modules.Select(module => new PermissionGroupViewModel
            {
                Key = module.Key,
                DisplayName = module.DisplayName,
                Icon = PermissionIcons.For(module.Key),
                Items = module.Permissions.Select(permission =>
                {
                    var isInherited = inherited.TryGetValue(permission.Value, out var sources);

                    return new PermissionItemViewModel
                    {
                        Value = permission.Value,
                        DisplayName = permission.DisplayName,
                        Description = permission.Description,
                        IsLocked = isInherited,
                        IsSelected = isInherited || direct.Contains(permission.Value),
                        IsAlsoDirect = isInherited && direct.Contains(permission.Value),
                        LockedReason = isInherited
                            ? "Granted by role: " + string.Join(", ", sources!)
                            : null,
                    };
                }).ToList(),
            }).ToList();
        }
    }
}
