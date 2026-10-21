using Travel.Domain.Enums;

namespace Travel.Presentation.Helpers
{
    /// <summary>
    /// The single source of truth for how a theme is spelled on the page, mirroring
    /// <see cref="StatusBadges"/>: the <c>data-theme</c> value app-extras.css keys its tokens on, and
    /// the Skote stylesheet pair each theme loads. <c>wwwroot/js/theme-sync.js</c> repeats the
    /// file names (it swaps them client-side) — change both together.
    /// </summary>
    public static class ThemeAssets
    {
        public static string DataTheme(AppTheme theme) =>
            theme == AppTheme.Dark ? "dark" : "light";

        public static string BootstrapCss(AppTheme theme) =>
            theme == AppTheme.Dark ? "~/back/assets/css/bootstrap-dark.min.css" : "~/back/assets/css/bootstrap.min.css";

        public static string AppCss(AppTheme theme) =>
            theme == AppTheme.Dark ? "~/back/assets/css/app-dark.min.css" : "~/back/assets/css/app.min.css";

        /// <summary>The theme the topbar button switches to.</summary>
        public static AppTheme Opposite(AppTheme theme) =>
            theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;

        public static string ToggleLabel(AppTheme theme) =>
            theme == AppTheme.Dark ? "Switch to light mode" : "Switch to dark mode";
    }
}
