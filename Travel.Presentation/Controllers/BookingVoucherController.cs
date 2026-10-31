using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Services.Email;
using Travel.Domain.Specifications.Bookings;
using Travel.Presentation.Logging;
using Travel.Presentation.Services.Vouchers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// A booking's travel voucher — the PDF, and emailing it to the client. Every action is gated on
    /// <see cref="Permissions.Bookings.Voucher"/>; a cancelled booking never gets one. Each send, and each
    /// failed send, is written to the booking's audit trail.
    /// </summary>
    [Authorize(Policy = Permissions.Bookings.Voucher)]
    public class BookingVoucherController : Controller
    {
        public const int MaxCc = 5;
        public const int MaxMessageLength = 1000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly VoucherModelFactory _vouchers;
        private readonly VoucherAssets _assets;
        private readonly IEmailSender _email;
        private readonly ICurrentUserService _currentUser;
        private readonly ICompositeViewEngine _viewEngine;
        private readonly ILogger<BookingVoucherController> _logger;

        public BookingVoucherController(
            IUnitOfWork unitOfWork,
            VoucherModelFactory vouchers,
            VoucherAssets assets,
            IEmailSender email,
            ICurrentUserService currentUser,
            ICompositeViewEngine viewEngine,
            ILogger<BookingVoucherController> logger)
        {
            _unitOfWork = unitOfWork;
            _vouchers = vouchers;
            _assets = assets;
            _email = email;
            _currentUser = currentUser;
            _viewEngine = viewEngine;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Download(int id, VoucherLanguage language = VoucherLanguage.English)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return NotFound();

            if (booking.Status == BookingStatus.Cancelled)
            {
                _logger.LogWarning("Voucher for booking {BookingCode} refused: booking is cancelled", booking.Code);
                TempData["Error"] = "A cancelled booking has no voucher.";
                return RedirectToAction("Details", "Booking", new { id });
            }

            if (!Enum.IsDefined(language))
                language = VoucherLanguage.English;

            var model = _vouchers.Build(booking, language, Today());
            var bytes = BookingVoucherPdf.Render(model, _assets);

            _logger.LogInformation("Voucher for booking {BookingCode} downloaded ({Language})", booking.Code, language);
            return File(bytes, BookingVoucherPdf.ContentType, BookingVoucherPdf.FileName(model));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(int id, string? to, string? cc, VoucherLanguage language, string? message)
        {
            var booking = await _unitOfWork.Repository<Booking>().GetByIdAsync(new BookingWithDetailsSpec(id));
            if (booking is null)
                return Json(new { success = false, message = "Booking not found." });

            if (booking.Status == BookingStatus.Cancelled)
                return Refuse(booking, "A cancelled booking has no voucher.");

            if (!Enum.IsDefined(language))
                return Refuse(booking, "Choose English or Portuguese.");

            var recipient = to?.Trim() ?? string.Empty;
            if (!IsEmail(recipient))
                return Refuse(booking, "Enter a valid email address for the client.");

            var copies = (cc ?? string.Empty)
                .Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(a => !a.Equals(recipient, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var badCopy = copies.FirstOrDefault(a => !IsEmail(a));
            if (badCopy is not null)
                return Refuse(booking, $"\"{badCopy}\" isn't a valid email address.", logReason: "invalid CC address");
            if (copies.Count > MaxCc)
                return Refuse(booking, $"Add at most {MaxCc} CC addresses.");

            var note = message?.Trim();
            if (note?.Length > MaxMessageLength)
                return Refuse(booking, $"Keep the message under {MaxMessageLength} characters.");

            var voucher = _vouchers.Build(booking, language, Today());
            var pdf = BookingVoucherPdf.Render(voucher, _assets);
            var email = new EmailMessage(
                To: new[] { recipient },
                Cc: copies,
                Subject: string.Format(voucher.Text.EmailSubject, voucher.BookingCode),
                HtmlBody: await RenderViewAsync("_VoucherEmail", new VoucherEmailViewModel { Voucher = voucher, Message = note }),
                TextBody: PlainText(voucher, note))
            {
                Attachments = new[] { new EmailAttachment(BookingVoucherPdf.FileName(voucher), BookingVoucherPdf.ContentType, pdf) },
                InlineImages = new[] { new EmailInlineImage(VoucherEmailViewModel.HeroContentId, "image/jpeg", _assets.EmailHero(language)) },
            };

            var result = await _email.SendAsync(email, HttpContext.RequestAborted);
            await RecordAsync(booking, language, recipient, copies, result.Succeeded ? null : result.Error);

            if (!result.Succeeded)
            {
                // No address here — guest data stays out of the log files; it is in the trail.
                _logger.LogWarning("Voucher for booking {BookingCode} not sent ({Language}): {Reason}",
                    booking.Code, language, LogRedaction.Emails(result.Error));
                return Json(new { success = false, message = $"The voucher was not sent. {result.Error}" });
            }

            _logger.LogInformation("Voucher for booking {BookingCode} sent ({Language}, {Recipients} recipient(s))",
                booking.Code, language, 1 + copies.Count);
            return Json(new { success = true, message = $"Voucher sent to {recipient}." });
        }

        /// <param name="logReason">What the log says instead, when <paramref name="reason"/> echoes guest input.</param>
        private JsonResult Refuse(Booking booking, string reason, string? logReason = null)
        {
            _logger.LogWarning("Voucher for booking {BookingCode} refused: {Reason}", booking.Code, logReason ?? reason);
            return Json(new { success = false, message = reason });
        }

        /// <summary>
        /// One audit event per attempt — sent or failed (the user's call: a colleague must see an attempt
        /// that didn't arrive, and why). The details ride in change rows, which the trail renders as plain
        /// "Field: value" lines for these two actions.
        /// </summary>
        private async Task RecordAsync(Booking booking, VoucherLanguage language, string to, IReadOnlyList<string> cc, string? error)
        {
            var languageName = language == VoucherLanguage.Portuguese ? "Portuguese" : "English";
            var ev = new BookingAuditEvent
            {
                BookingId = booking.Id,
                OccurredAt = DateTime.UtcNow,
                UserId = _currentUser.UserId,
                UserName = _currentUser.UserName,
                Action = error is null ? AuditAction.VoucherSent : AuditAction.VoucherSendFailed,
                Summary = error is null ? $"Voucher sent ({languageName})" : $"Voucher send failed ({languageName})",
            };
            ev.Changes.Add(Detail("To", to));
            if (cc.Count > 0)
                ev.Changes.Add(Detail("CC", string.Join(", ", cc)));
            if (error is not null)
                ev.Changes.Add(Detail("Error", error));

            try
            {
                await _unitOfWork.Repository<BookingAuditEvent>().AddAsync(ev);
                await _unitOfWork.CompleteAsync();
            }
            catch (Exception ex)
            {
                // The email outcome is what the operator asked about; a trail write failing must not
                // turn a delivered voucher into an error toast.
                _logger.LogError(ex, "Voucher audit event for booking {BookingCode} not saved", booking.Code);
            }
        }

        private static BookingAuditChange Detail(string field, string value) => new()
        {
            EntityType = "Voucher",
            Field = field,
            NewValue = value.Length > 500 ? value[..500] : value,
            Kind = AuditChangeKind.Added,
        };

        private static bool IsEmail(string value) =>
            value.Length <= 254 && MailAddress.TryCreate(value, out var address) && address.Address == value
            && value.IndexOf('@') is > 0 and var at && value.IndexOf('.', at) > at + 1;

        private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

        private static string PlainText(VoucherModel v, string? note)
        {
            var t = v.Text;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(t.EmailGreeting, v.LeadGuestName)).AppendLine();
            sb.AppendLine(t.EmailIntro).AppendLine();
            if (!string.IsNullOrWhiteSpace(note))
                sb.AppendLine(note).AppendLine();
            sb.AppendLine($"{t.BookingNo} {v.BookingCode}");
            sb.AppendLine($"{t.Arrival}: {v.Arrival.Date} · {v.Arrival.Detail}");
            sb.AppendLine($"{t.Departure}: {v.Departure.Date} · {v.Departure.Detail}").AppendLine();
            sb.AppendLine($"{t.Itinerary} ({v.StaysSummary})");
            foreach (var item in v.Timeline)
                sb.AppendLine(item.Kind == VoucherTimelineKind.Stay
                    ? $"  {item.Range}  {item.Title} — {item.Subtitle} ({item.NightsLabel})"
                    : $"  {item.Day} {item.Month}  {item.Title} — {item.Subtitle}");
            sb.AppendLine().AppendLine($"{t.Travellers} ({v.PaxSummary})");
            foreach (var guest in v.Guests)
                sb.AppendLine($"  {guest.Name} — {guest.Type}{(guest.IsLead ? $" ({t.LeadGuest})" : "")}");
            sb.AppendLine().AppendLine(t.EmailAttached);
            if (!string.IsNullOrWhiteSpace(v.ContactEmail))
                sb.AppendLine($"{t.EmailQuestions} {v.ContactEmail}.");
            sb.AppendLine().AppendLine(t.EmailSignOff).AppendLine(t.EmailTeam);
            return sb.ToString();
        }

        private async Task<string> RenderViewAsync(string viewName, object model)
        {
            var view = _viewEngine.FindView(ControllerContext, viewName, isMainPage: false).View
                ?? throw new InvalidOperationException($"View {viewName} not found.");
            var viewData = new ViewDataDictionary(ViewData) { Model = model };
            await using var writer = new StringWriter();
            await view.RenderAsync(new ViewContext(ControllerContext, view, viewData, TempData, writer, new HtmlHelperOptions()));
            return writer.ToString();
        }
    }
}
