using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Enums;
using Travel.Domain.Models.Identity;
using Travel.Domain.Services;
using Travel.Domain.Services.Identity;
using Travel.Presentation.Helpers;
using Travel.Presentation.Logging;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    public class AccountController : Controller
    {
        private const string UsersFolder = "Users";
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IFileStorageService _fileStorage;
        private readonly IUserAccountService _userAccounts;
        private readonly ILogger<AccountController> _logger;

        public AccountController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IFileStorageService fileStorage, IUserAccountService userAccounts, ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _fileStorage = fileStorage;
            _userAccounts = userAccounts;
            _logger = logger;
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
        [SkipModelStateLog]
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
                // The page shows one message for all three causes; the log tells them apart.
                _logger.LogWarning("Login failed for {Login}: {Reason}",
                    model.EmailOrUserName, user is null ? "no such user" : "account inactive");
                ModelState.AddModelError(string.Empty, "These credentials do not match our records.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                _logger.LogInformation("Login succeeded for {Login} (remember me: {RememberMe})",
                    user.UserName, model.RememberMe);

                // Redirect to originally requested page or dashboard
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }

            _logger.LogWarning("Login failed for {Login}: {Reason}", user.UserName,
                result.IsLockedOut ? "locked out" : result.IsNotAllowed ? "sign-in not allowed" : "wrong password");
            ModelState.AddModelError(string.Empty, "These credentials do not match our records.");
            return View(model);
        }

        [Authorize]
        public new async Task<IActionResult> SignOut()
        {
            _logger.LogInformation("Signed out");
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            // The one line per real denial — the framework's own "Authorization failed" is muted, as
            // it also fires for every hidden sidebar item.
            _logger.LogWarning("Access denied to {Path}", returnUrl ?? "(unknown page)");
            return View();
        }

        // A personal preference every signed-in user owns — no permission gate. The topbar button
        // posts it with fetch (JSON back, no reload); with scripting off the same form posts
        // normally and lands back on the page it came from.
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetTheme(AppTheme theme, string? returnUrl = null)
        {
            var result = await _userAccounts.SetThemeAsync(_userManager.GetUserId(User)!, theme);
            var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";

            if (isAjax)
                return Json(new { success = result.Succeeded, message = result.Error, theme = ThemeAssets.DataTheme(theme) });

            if (!result.Succeeded)
                TempData["Error"] = result.Error;

            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Index", "Home");
        }

        // ─── PROFILE ─────────────────────────────────────────────────────────
        // The signed-in user's own account. No permission gate (like SetTheme): everyone owns theirs.
        // Username, email, active flag and roles are deliberately not editable here — see CLAUDE.md.

        // Above the 2 MB photo cap, so an oversized pick reaches validation and gets a readable
        // message instead of Kestrel's bare 400 (the 2026-08-02 gotcha).
        private const long ProfileRequestBytes = 10 * 1024 * 1024;

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var model = await BuildProfileModelAsync();
            return model is null ? NotFound() : View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ProfileRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = ProfileRequestBytes)]
        public async Task<IActionResult> UpdateProfile([Bind(Prefix = nameof(ProfileViewModel.Info))] ProfileInfoInput info)
        {
            var userId = _userManager.GetUserId(User)!;
            var current = await _userAccounts.GetAsync(userId);
            if (current is null) return NotFound();

            var image = info.ProfileImage is { Length: > 0 } file ? file : null;
            if (image is not null)
            {
                if (!_fileStorage.IsImage(image.FileName))
                    ModelState.AddModelError($"{nameof(ProfileViewModel.Info)}.{nameof(info.ProfileImage)}",
                        "Only image files (JPG, PNG, GIF, WEBP) are allowed.");
                else if (image.Length > ProfileInfoInput.MaxImageBytes)
                    ModelState.AddModelError($"{nameof(ProfileViewModel.Info)}.{nameof(info.ProfileImage)}",
                        "The photo must be 2 MB or smaller.");
            }

            if (!ModelState.IsValid)
                return View(nameof(Profile), await BuildProfileModelAsync(info: info));

            string? newImageUrl = null;
            if (image is not null)
            {
                await using var stream = image.OpenReadStream();
                var fileName = await _fileStorage.SaveFileAsync(stream, image.FileName, UsersFolder);
                newImageUrl = $"/Files/{UsersFolder}/{fileName}";
            }

            var result = await _userAccounts.UpdateOwnProfileAsync(new UpdateOwnProfileRequest(
                userId, info.FirstName, info.LastName, info.PhoneNumber, info.Gender,
                newImageUrl, info.RemoveImage));

            if (!result.Succeeded)
            {
                if (newImageUrl is not null)
                    _fileStorage.DeleteFile(Path.GetFileName(newImageUrl), UsersFolder);

                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error);

                return View(nameof(Profile), await BuildProfileModelAsync(info: info));
            }

            // The old file goes only after the row stopped pointing at it.
            var photoReplacedOrRemoved = newImageUrl is not null || info.RemoveImage;
            if (photoReplacedOrRemoved && !string.IsNullOrEmpty(current.ImageUrl))
                _fileStorage.DeleteFile(Path.GetFileName(current.ImageUrl), UsersFolder);

            TempData["Success"] = "Your profile was updated.";
            return RedirectToAction(nameof(Profile));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = nameof(ProfileViewModel.Password))] ChangePasswordInput input)
        {
            const string prefix = nameof(ProfileViewModel.Password);

            if (!ModelState.IsValid)
                return View(nameof(Profile), await BuildProfileModelAsync());

            var userId = _userManager.GetUserId(User)!;
            var result = await _userAccounts.ChangePasswordAsync(userId, input.CurrentPassword, input.NewPassword);
            if (!result.Succeeded)
            {
                // Each message lands beside the field it is about — a wrong current password under
                // Current, a policy failure under New — so no rule is ever invisible.
                // A span shows only its first error, so the policy failures are joined into one line
                // rather than revealed one round trip at a time.
                var mismatch = _userManager.ErrorDescriber.PasswordMismatch().Description;
                if (result.Errors.Contains(mismatch))
                    ModelState.AddModelError($"{prefix}.{nameof(input.CurrentPassword)}", "Your current password is incorrect.");

                var policy = result.Errors.Where(e => e != mismatch).ToList();
                if (policy.Count > 0)
                    ModelState.AddModelError($"{prefix}.{nameof(input.NewPassword)}", string.Join(" ", policy));

                return View(nameof(Profile), await BuildProfileModelAsync());
            }

            // Identity rotated the security stamp. Re-issue this tab's cookie with the new stamp so it
            // stays signed in; every other session fails its next stamp check and is signed out.
            var me = await _userManager.FindByIdAsync(userId);
            if (me is not null)
                await _signInManager.RefreshSignInAsync(me);

            TempData["Success"] = "Password changed. Your other devices have been signed out.";
            return RedirectToAction(nameof(Profile));
        }

        /// <summary>
        /// The page model, from the stored account. A re-render passes the posted <paramref name="info"/>
        /// so the operator's typing survives; the password boxes always start empty.
        /// </summary>
        private async Task<ProfileViewModel?> BuildProfileModelAsync(ProfileInfoInput? info = null)
        {
            var user = await _userAccounts.GetAsync(_userManager.GetUserId(User)!);
            if (user is null)
                return null;

            return new ProfileViewModel
            {
                FullName = user.FullName,
                UserName = user.UserName,
                Email = user.Email,
                ImageUrl = user.ImageUrl,
                Roles = user.Roles,
                Info = info ?? new ProfileInfoInput
                {
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    PhoneNumber = user.PhoneNumber,
                    Gender = user.Gender,
                },
            };
        }
    }
}
