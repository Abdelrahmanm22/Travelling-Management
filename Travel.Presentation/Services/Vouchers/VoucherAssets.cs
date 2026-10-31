using System.Globalization;
using QuestPDF.Drawing;

namespace Travel.Presentation.Services.Vouchers
{
    /// <summary>
    /// The voucher's static files, read once. Fonts and hero art ship in the build output (Assets/),
    /// the logo in wwwroot so the email can reuse it.
    /// </summary>
    public sealed class VoucherAssets
    {
        private readonly string heroSvg;
        public byte[] LogoPng { get; }

        /// <summary>
        /// The hero art sized to an exact box, filled edge to edge and anchored on the dunes
        /// (<c>xMidYMax slice</c>), so the horizon never floats when the box ratio differs slightly.
        /// </summary>
        public string HeroSvgSized(float width, float height) =>
            heroSvg.Replace("<svg ", string.Create(CultureInfo.InvariantCulture,
                $"<svg width=\"{width:0.##}\" height=\"{height:0.##}\" preserveAspectRatio=\"xMidYMax slice\" "));

        public VoucherAssets(IWebHostEnvironment env)
        {
            heroSvg = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Voucher", "hero-daylight.svg"));
            LogoPng = File.ReadAllBytes(Path.Combine(env.WebRootPath, "images", "voucher-logo.png"));
        }

        /// <summary>
        /// The container has no Poppins, and QuestPDF would silently fall back to another face.
        /// Called once at startup, before the first document is generated.
        /// </summary>
        public static void RegisterFonts()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            foreach (var file in Directory.EnumerateFiles(dir, "*.ttf"))
                FontManager.RegisterFontFromFile(file);
        }
    }
}
