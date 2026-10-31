using Travel.Presentation.Services.Vouchers;

namespace Travel.Presentation.ViewModels
{
    /// <summary>The voucher email body — the same <see cref="VoucherModel"/> the PDF renders, plus the operator's note.</summary>
    public sealed class VoucherEmailViewModel
    {
        /// <summary>Content-ID the HTML uses for the inline hero image (<c>cid:…</c>).</summary>
        public const string HeroContentId = "voucher-hero";

        public required VoucherModel Voucher { get; init; }

        /// <summary>Optional free text from the operator. Rendered encoded, line breaks kept.</summary>
        public string? Message { get; init; }
    }
}
