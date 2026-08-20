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
            ["Reports"] = "bx-bar-chart-alt-2",
            ["Tours"] = "bx-compass",
            ["Transactions"] = "bx-wallet",
            ["Users"] = "bx-group",
            ["Roles"] = "bx-shield-quarter",
            ["MoneySafes"] = "bx-wallet-alt",
            ["ExpenseTypes"] = "bx-purchase-tag",
            ["Expenses"] = "bx-receipt",
        };

        public static string For(string moduleKey) =>
            Icons.TryGetValue(moduleKey, out var icon) ? icon : Fallback;
    }
}
