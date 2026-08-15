namespace Travel.Presentation.Helpers
{
    /// <summary>
    /// What counts as a valid uploaded document, in one place, so the two upload paths — a booking's
    /// flight/passport files and an arrival group's domestic flight documents — can't drift on it.
    /// The Dropzone options in <c>attachment-dropzones.js</c> mirror these but are not trusted.
    /// </summary>
    public static class AttachmentRules
    {
        public static readonly string[] AllowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png" };

        public const long MaxFileBytes = 10 * 1024 * 1024;

        /// <summary>
        /// Every POST that accepts these files needs <c>[RequestSizeLimit]</c> +
        /// <c>[RequestFormLimits(MultipartBodyLengthLimit = ...)]</c> set to this. Kestrel's own default
        /// (30,000,000 bytes) is smaller than a handful of real phone photos, and it kills the request
        /// <i>before</i> model binding — so no validation runs and no ModelState error ever reaches the
        /// view. Sized well above <see cref="MaxFileBytes"/> × a realistic file count.
        /// </summary>
        public const long MaxRequestBytes = 100 * 1024 * 1024;

        /// <summary>
        /// A payment carries at most one receipt. Enforced on both transaction upload paths — the
        /// TransactionController form and the first payment taken on Booking/Create — and mirrored by
        /// <c>data-max-files</c> on their dropzones, which is not trusted.
        /// </summary>
        public const int MaxReceiptFiles = 1;

        public static readonly string InvalidTypeMessage = "only PDF, JPG and PNG files are allowed.";

        public static readonly string TooLargeMessage = "files must be 10MB or smaller.";

        public static readonly string TooManyReceiptsMessage =
            "a payment can have only one receipt. Remove the existing one to replace it.";

        /// <summary>The rejection reason for a file, or null when it's acceptable.</summary>
        public static string? Reject(string fileName, long length)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (!AllowedExtensions.Contains(extension)) return InvalidTypeMessage;
            if (length > MaxFileBytes) return TooLargeMessage;

            return null;
        }
    }
}
