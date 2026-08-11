namespace Travel.Presentation.Helpers
{
    /// <summary>Boxicons class per permission module — UI concern, so it stays out of the Domain catalog.</summary>
    public static class PermissionIcons
    {
        private const string Fallback = "bx-shield";

        private static readonly Dictionary<string, string> Icons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bookings"] = "bx-calendar-check",
            ["BlockServices"] = "bx-layer",
            ["Packages"] = "bx-package",
            ["Destinations"] = "bx-map-pin",
            ["Nationalities"] = "bx-flag",
            ["Users"] = "bx-group",
            ["Roles"] = "bx-shield-quarter",
        };

        public static string For(string moduleKey) =>
            Icons.TryGetValue(moduleKey, out var icon) ? icon : Fallback;
    }
}
