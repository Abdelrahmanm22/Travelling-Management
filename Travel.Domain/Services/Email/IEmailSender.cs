namespace Travel.Domain.Services.Email
{
    /// <summary>
    /// Sends one email. Transport-agnostic so the Domain carries no SMTP library: the implementation
    /// lives in the web project, next to the other NuGet-backed services.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>
        /// Never throws for a delivery problem — a failure comes back as an
        /// <see cref="OperationResult"/> whose <see cref="OperationResult.Error"/> an operator can read.
        /// </summary>
        Task<OperationResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }

    public sealed record EmailMessage(
        IReadOnlyList<string> To,
        IReadOnlyList<string> Cc,
        string Subject,
        string HtmlBody,
        string TextBody)
    {
        public IReadOnlyList<EmailAttachment> Attachments { get; init; } = Array.Empty<EmailAttachment>();

        /// <summary>Images the HTML references as <c>cid:{ContentId}</c> — shown even where remote images are blocked.</summary>
        public IReadOnlyList<EmailInlineImage> InlineImages { get; init; } = Array.Empty<EmailInlineImage>();
    }

    public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

    public sealed record EmailInlineImage(string ContentId, string ContentType, byte[] Content);
}
