using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Travel.Domain.Services;
using Travel.Domain.Services.Email;
using Travel.Presentation.Logging;

namespace Travel.Presentation.Services.Email
{
    /// <summary>
    /// appsettings "Email". Host and credentials are never committed: on the VPS they come from
    /// <c>/srv/travel/{prod,dev}/app.env</c> (<c>Email__Host</c>, …), locally from <c>dotnet user-secrets</c>.
    /// </summary>
    public sealed class EmailOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;

        /// <summary>Where a client's reply lands — the From address is a no-reply.</summary>
        public string ReplyTo { get; set; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
    }

    /// <summary>
    /// SMTP through MailKit (Mailtrap: Email API/SMTP on prod, Sandbox on dev and local). Every failure
    /// comes back as an operator-readable <see cref="OperationResult"/>; nothing is thrown to the caller.
    /// </summary>
    public sealed class MailKitEmailSender : IEmailSender
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private readonly EmailOptions _options;
        private readonly ILogger<MailKitEmailSender> _logger;

        public MailKitEmailSender(IOptions<EmailOptions> options, ILogger<MailKitEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<OperationResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (!_options.IsConfigured)
                return OperationResult.Fail("Email isn't configured on this server yet.");

            using var mime = Build(message);
            using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };

            try
            {
                // Auto: STARTTLS on 587/2525/25, TLS on connect on 465 — Mailtrap offers both.
                await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.Auto, cancellationToken);
                if (!string.IsNullOrEmpty(_options.UserName))
                    await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
                await client.SendAsync(mime, cancellationToken);
                await client.DisconnectAsync(quit: true, cancellationToken);
                return OperationResult.Ok();
            }
            catch (AuthenticationException ex)
            {
                _logger.LogWarning(ex, "SMTP authentication failed");
                return OperationResult.Fail("The mail server rejected the email service credentials.");
            }
            catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
            {
                // The rejected address is guest data — it goes to the trail, not the log.
                _logger.LogWarning("SMTP recipient rejected ({StatusCode})", (int)ex.StatusCode);
                return OperationResult.Fail($"The mail server rejected the address {ex.Mailbox?.Address}.");
            }
            catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.SenderNotAccepted)
            {
                _logger.LogWarning("SMTP sender rejected ({StatusCode})", (int)ex.StatusCode);
                return OperationResult.Fail(
                    $"The mail server won't send from {_options.FromAddress} — the sending domain may not be verified yet.");
            }
            catch (SmtpCommandException ex)
            {
                _logger.LogWarning("SMTP command failed ({StatusCode}): {Reason}", (int)ex.StatusCode, LogRedaction.Emails(ex.Message));
                return OperationResult.Fail($"The mail server refused the message: {ex.Message}");
            }
            catch (Exception ex) when (ex is SocketException or SslHandshakeException or TimeoutException
                                           or OperationCanceledException or ServiceNotConnectedException or IOException)
            {
                _logger.LogWarning(ex, "SMTP server {Host}:{Port} unreachable", _options.Host, _options.Port);
                return OperationResult.Fail("Couldn't reach the mail server. Please try again in a moment.");
            }
            catch (ProtocolException ex)
            {
                _logger.LogWarning(ex, "SMTP protocol error");
                return OperationResult.Fail("The mail server returned an unexpected response.");
            }
        }

        private MimeMessage Build(EmailMessage message)
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
                mime.ReplyTo.Add(MailboxAddress.Parse(_options.ReplyTo));
            mime.To.AddRange(message.To.Select(MailboxAddress.Parse));
            mime.Cc.AddRange(message.Cc.Select(MailboxAddress.Parse));
            mime.Subject = message.Subject;

            var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
            foreach (var image in message.InlineImages)
            {
                var resource = body.LinkedResources.Add(image.ContentId, image.Content, ContentType.Parse(image.ContentType));
                resource.ContentId = image.ContentId;
            }
            foreach (var file in message.Attachments)
                body.Attachments.Add(file.FileName, file.Content, ContentType.Parse(file.ContentType));

            mime.Body = body.ToMessageBody();
            return mime;
        }
    }
}
