using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Authorization;
using Travel.Domain.Services.Identity;
using Travel.Presentation.Helpers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class RoleController : Controller
    {
        private readonly IRoleService _roleService;
        private readonly IAuthorizationService _authorization;

        public RoleController(IRoleService roleService, IAuthorizationService authorization)
        {
            _roleService = roleService;
            _authorization = authorization;
        }

        [Authorize(Policy = Permissions.Roles.View)]
        public async Task<IActionResult> Index()
        {
            var roles = await _roleService.GetAllAsync();
            return View(roles);
        }

        [Authorize(Policy = Permissions.Roles.View)]
        public async Task<IActionResult> Details(string id)
        {
            var role = await _roleService.GetAsync(id);
            if (role is null) return NotFound();

            return View(role);
        }

        [Authorize(Policy = Permissions.Roles.Create)]
        public IActionResult Create()
        {
            var model = new RoleViewModel();
            PopulatePermissionGroups(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Roles.Create)]
        public async Task<IActionResult> Create(RoleViewModel model)
        {
            if (!await CanManagePermissionsAsync(model))
            {
                TempData["Error"] = "You don't have permission to change role permissions.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                PopulatePermissionGroups(model);
                return View(model);
            }

            var result = await _roleService.CreateAsync(model.Name, model.SelectedPermissions);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error);

                PopulatePermissionGroups(model);
                return View(model);
            }

            TempData["Success"] = "Role created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Roles.Edit)]
        public async Task<IActionResult> Edit(string id)
        {
            var role = await _roleService.GetAsync(id);
            if (role is null) return NotFound();

            if (role.IsProtected)
            {
                TempData["Error"] = $"The {role.Name} role can't be modified.";
                return RedirectToAction(nameof(Index));
            }

            var model = new RoleViewModel
            {
                Id = role.Id,
                Name = role.Name,
                IsProtected = role.IsProtected,
                SelectedPermissions = role.Permissions.ToList(),
            };

            PopulatePermissionGroups(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Roles.Edit)]
        public async Task<IActionResult> Edit(RoleViewModel model)
        {
            if (string.IsNullOrEmpty(model.Id)) return NotFound();

            if (!await CanManagePermissionsAsync(model))
            {
                TempData["Error"] = "You don't have permission to change role permissions.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                PopulatePermissionGroups(model);
                return View(model);
            }

            var result = await _roleService.UpdateAsync(model.Id, model.Name, model.SelectedPermissions);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error);

                PopulatePermissionGroups(model);
                return View(model);
            }

            TempData["Success"] = "Role updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Roles.Delete)]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _roleService.DeleteAsync(id);
            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded ? "Role deleted successfully." : result.Error,
            });
        }

        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Roles.View)]
        public async Task<IActionResult> IsNameAvailable(string name, string? id)
        {
            return Json(await _roleService.IsNameAvailableAsync(name, id));
        }

        /// <summary>
        /// Permissions are edited inline on the role form, so the separate ManagePermissions gate is
        /// enforced here rather than on the whole action.
        /// </summary>
        private async Task<bool> CanManagePermissionsAsync(RoleViewModel model)
        {
            if (model.SelectedPermissions.Count == 0)
                return true;

            return (await _authorization.AuthorizeAsync(User, Permissions.Roles.ManagePermissions)).Succeeded;
        }

        private static void PopulatePermissionGroups(RoleViewModel model)
        {
            var selected = model.SelectedPermissions.ToHashSet(StringComparer.Ordinal);

            model.Groups = PermissionCatalog.Modules.Select(module => new PermissionGroupViewModel
            {
                Key = module.Key,
                DisplayName = module.DisplayName,
                Icon = PermissionIcons.For(module.Key),
                Items = module.Permissions.Select(permission => new PermissionItemViewModel
                {
                    Value = permission.Value,
                    DisplayName = permission.DisplayName,
                    Description = permission.Description,
                    IsSelected = selected.Contains(permission.Value),
                }).ToList(),
            }).ToList();
        }
    }
}
