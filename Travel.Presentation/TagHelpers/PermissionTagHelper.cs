using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Travel.Presentation.TagHelpers
{
    /// <summary>
    /// Hides its content unless the current user holds the required permission(s):
    /// <c>&lt;permission required="@Permissions.Bookings.View"&gt;…&lt;/permission&gt;</c>.
    /// Evaluates through <see cref="IAuthorizationService"/> so it shares the exact same handler the
    /// server-side <c>[Authorize(Policy = …)]</c> gates use — including the SuperAdmin bypass.
    /// </summary>
    [HtmlTargetElement("permission", Attributes = RequiredAttributeName)]
    public class PermissionTagHelper : TagHelper
    {
        private const string RequiredAttributeName = "required";

        private readonly IAuthorizationService _authorization;

        public PermissionTagHelper(IAuthorizationService authorization) => _authorization = authorization;

        /// <summary>One permission, or several separated by commas.</summary>
        [HtmlAttributeName(RequiredAttributeName)]
        public string Required { get; set; } = string.Empty;

        /// <summary>"any" (default) or "all" when several permissions are listed.</summary>
        [HtmlAttributeName("mode")]
        public string Mode { get; set; } = "any";

        [HtmlAttributeNotBound, ViewContext]
        public ViewContext ViewContext { get; set; } = default!;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            // Erase the <permission> element itself so children stay direct children of their parent —
            // the sidebar puts <li> inside <ul class="metismenu">, which a wrapper would break.
            output.TagName = null;

            var required = Required.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (required.Length == 0)
            {
                output.SuppressOutput();
                return;
            }

            var user = ViewContext.HttpContext.User;
            var allowed = Mode.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? await HasAllAsync(user, required)
                : await HasAnyAsync(user, required);

            if (!allowed)
                output.SuppressOutput();
        }

        private async Task<bool> HasAnyAsync(ClaimsPrincipal user, string[] permissions)
        {
            foreach (var permission in permissions)
            {
                if ((await _authorization.AuthorizeAsync(user, permission)).Succeeded)
                    return true;
            }

            return false;
        }

        private async Task<bool> HasAllAsync(ClaimsPrincipal user, string[] permissions)
        {
            foreach (var permission in permissions)
            {
                if (!(await _authorization.AuthorizeAsync(user, permission)).Succeeded)
                    return false;
            }

            return true;
        }
    }
}
