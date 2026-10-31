using System.Globalization;
using QuestPDF.Drawing;
using Travel.Domain.Enums;

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

        private readonly byte[] emailHeroEn;
        private readonly byte[] emailHeroPt;

        /// <summary>
        /// The email's hero: the same art with the logo, label and title baked in, one JPEG per language.
        /// Live text over a background image breaks in Outlook, and email clients strip SVG, so it is an
        /// image. Rendered from <c>hero-daylight.svg</c> + the logo at 1200×436 — regenerate both if either changes.
        /// </summary>
        public byte[] EmailHero(VoucherLanguage language) =>
            language == VoucherLanguage.Portuguese ? emailHeroPt : emailHeroEn;

        public VoucherAssets(IWebHostEnvironment env)
        {
            heroSvg = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Voucher", "hero-daylight.svg"));
            LogoPng = File.ReadAllBytes(Path.Combine(env.WebRootPath, "images", "voucher-logo.png"));
            emailHeroEn = File.ReadAllBytes(Path.Combine(env.WebRootPath, "images", "voucher-email-hero-en.jpg"));
            emailHeroPt = File.ReadAllBytes(Path.Combine(env.WebRootPath, "images", "voucher-email-hero-pt.jpg"));
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
