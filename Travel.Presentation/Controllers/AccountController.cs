using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    public class AccountController : Controller
    {
        private const string UsersFolder = "Users";
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IFileStorageService _fileStorage;

        public AccountController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IFileStorageService fileStorage)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _fileStorage = fileStorage;
        }
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            // If already authenticated, skip the login page
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            // Support login by email OR username in the same field
            var user = model.EmailOrUserName.Contains('@')
                ? await _userManager.FindByEmailAsync(model.EmailOrUserName)
                : await _userManager.FindByNameAsync(model.EmailOrUserName);

            if (user is null || !user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "These credentials do not match our records.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                // Redirect to originally requested page or dashboard
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "These credentials do not match our records.");
            return View(model);
        }

        [Authorize]
        public new async Task<IActionResult> SignOut()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ─── PROFILE ─────────────────────────────────────────────────────────

        //[Authorize]
        //public async Task<IActionResult> Profile()
        //{
        //    var user = await _userManager.Users
        //        .Include(u => u.Corporation)
        //        .Include(u => u.Section)
        //        .FirstOrDefaultAsync(u => u.Id == _userManager.GetUserId(User));

        //    if (user is null)
        //        return NotFound();

        //    var roles = await _userManager.GetRolesAsync(user);

        //    var vm = new ProfileViewModel
        //    {
        //        FullName = user.FullName,
        //        UserName = user.UserName ?? string.Empty,
        //        Email = user.Email ?? string.Empty,
        //        PhoneNumber = user.PhoneNumber,
        //        Gender = user.Gender,
        //        ImageUrl = user.ImageUrl,
        //        Role = roles.FirstOrDefault() ?? string.Empty,
        //        CorporationName = user.Corporation?.Name,
        //        SectionName = user.Section?.Name,
        //    };

        //    return View(vm);
        //}

        //[Authorize]
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> UpdateImage(ProfileViewModel model)
        //{
        //    var user = await _userManager.FindByIdAsync(_userManager.GetUserId(User)!);
        //    if (user is null)
        //        return NotFound();

        //    if (model.ProfileImage is null || model.ProfileImage.Length == 0)
        //    {
        //        TempData["Error"] = "Please select an image file.";
        //        return RedirectToAction(nameof(Profile));
        //    }

        //    if (!_fileStorage.IsImage(model.ProfileImage.FileName))
        //    {
        //        TempData["Error"] = "Only image files are allowed.";
        //        return RedirectToAction(nameof(Profile));
        //    }

        //    if (!string.IsNullOrEmpty(user.ImageUrl))
        //        _fileStorage.DeleteFile(Path.GetFileName(user.ImageUrl), UsersFolder);

        //    await using var stream = model.ProfileImage.OpenReadStream();
        //    var fileName = await _fileStorage.SaveFileAsync(stream, model.ProfileImage.FileName, UsersFolder);
        //    user.ImageUrl = $"/Files/{UsersFolder}/{fileName}";
        //    await _userManager.UpdateAsync(user);

        //    TempData["Success"] = "Profile picture updated successfully.";
        //    return RedirectToAction(nameof(Profile));
        //}
    }
}
