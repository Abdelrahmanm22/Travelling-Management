using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Bookings;
using Travel.Domain.Specifications.Transactions;
using Travel.Presentation.Helpers;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// The payment ledger — one entry per installment, refund or part-payment. Entries are editable
    /// and deletable by permission; every change lands in the booking's audit trail via the
    /// SaveChanges interceptor, so a corrected figure is never a silent one.
    /// </summary>
    [Authorize]
    public class TransactionController : Controller
    {
        private const string ReceiptsFolder = "TransactionAttachments";
        private const long MaxAttachmentsRequestBytes = AttachmentRules.MaxRequestBytes;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IFileStorageService _fileStorage;
        private readonly ICurrentUserService _currentUser;
        private readonly TransactionExcelExporter _excel;

        public TransactionController(
            IUnitOfWork unitOfWork,
            IFileStorageService fileStorage,
            ICurrentUserService currentUser,
            TransactionExcelExporter excel)
        {
            _unitOfWork = unitOfWork;
            _fileStorage = fileStorage;
            _currentUser = currentUser;
            _excel = excel;
        }

        [Authorize(Policy = Permissions.Transactions.View)]
        public async Task<IActionResult> Index(
            DateOnly? from, DateOnly? to, int? bookingId, PaymentMethod? method, TransactionType? type, bool applied = false)
        {
            var model = await BuildListAsync(from, to, bookingId, method, type, applied);
            return View(model);
        }

        [Authorize(Policy = Permissions.Transactions.Export)]
        public async Task<IActionResult> Export(
            DateOnly? from, DateOnly? to, int? bookingId, PaymentMethod? method, TransactionType? type, bool applied = false)
        {
            var model = await BuildListAsync(from, to, bookingId, method, type, applied);
            var bytes = _excel.Build(model);
            return File(bytes, TransactionExcelExporter.ContentType,
                TransactionExcelExporter.FileName(model.From, model.To));
        }

        [Authorize(Policy = Permissions.Transactions.Create)]
        public async Task<IActionResult> Create(int bookingId)
        {
            var booking = await LoadBookingAsync(bookingId);
            if (booking is null)
                return NotFound();

            return View(new TransactionFormViewModel { BookingId = bookingId, BookingCode = booking.Code });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Transactions.Create)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> Create(TransactionFormViewModel model)
        {
            var booking = await LoadBookingAsync(model.BookingId);
            if (booking is null)
                return NotFound();

            model.BookingCode = booking.Code;
            // Nothing held yet on a new entry, so the cap is simply "at most one uploaded".
            ValidateFiles(model.ReceiptFiles);

            if (!ModelState.IsValid)
                return View(model);

            var transaction = new BookingTransaction
            {
                BookingId = model.BookingId,
                Type = model.Type,
                Method = model.Method,
                Amount = model.Amount,
                PaidOn = model.PaidOn,
                Reference = Trimmed(model.Reference),
                Notes = Trimmed(model.Notes),
                CreatedAt = DateTime.UtcNow,
                // Snapshot, not a lookup — the ledger must still name who logged it after that user is gone.
                CreatedById = _currentUser.UserId,
                CreatedByName = _currentUser.UserName,
                Attachments = await SaveReceiptsAsync(model.ReceiptFiles),
            };

            await _unitOfWork.Repository<BookingTransaction>().AddAsync(transaction);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Payment recorded successfully.";
            return RedirectToAction("Details", "Booking", new { id = model.BookingId });
        }

        [Authorize(Policy = Permissions.Transactions.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var transaction = await _unitOfWork.Repository<BookingTransaction>()
                .GetByIdAsync(new TransactionSpec(id));

            if (transaction is null)
                return NotFound();

            return View(new TransactionFormViewModel
            {
                Id = transaction.Id,
                BookingId = transaction.BookingId,
                BookingCode = transaction.Booking?.Code ?? string.Empty,
                Type = transaction.Type,
                Method = transaction.Method,
                Amount = transaction.Amount,
                PaidOn = transaction.PaidOn,
                Reference = transaction.Reference,
                Notes = transaction.Notes,
                ExistingAttachments = transaction.Attachments.ToList(),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Transactions.Edit)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> Edit(TransactionFormViewModel model)
        {
            var transaction = await _unitOfWork.Repository<BookingTransaction>()
                .GetByIdAsync(new TransactionSpec(model.Id));

            if (transaction is null)
                return NotFound();

            model.BookingCode = transaction.Booking?.Code ?? string.Empty;
            // Count what survives this save: a ticked-for-removal receipt frees the slot, so replacing
            // one is a single round-trip rather than remove-save-edit-upload-save.
            var kept = transaction.Attachments.Count(a => !model.RemoveAttachmentIds.Contains(a.Id));
            ValidateFiles(model.ReceiptFiles, kept);

            if (!ModelState.IsValid)
            {
                model.ExistingAttachments = transaction.Attachments.ToList();
                return View(model);
            }

            // BookingId is deliberately not read from the post: an entry never moves between bookings,
            // and honouring a tampered value would move money off one balance and onto another.
            transaction.Type = model.Type;
            transaction.Method = model.Method;
            transaction.Amount = model.Amount;
            transaction.PaidOn = model.PaidOn;
            transaction.Reference = Trimmed(model.Reference);
            transaction.Notes = Trimmed(model.Notes);

            // Additive, like booking attachments — a file input can't be repopulated from a response,
            // so removals are an explicit id list and new uploads are appended.
            foreach (var attachmentId in model.RemoveAttachmentIds)
            {
                var attachment = transaction.Attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment is null)
                    continue;

                _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl), ReceiptsFolder);
                _unitOfWork.Repository<TransactionAttachment>().Delete(attachment);
            }

            foreach (var added in await SaveReceiptsAsync(model.ReceiptFiles))
                transaction.Attachments.Add(added);

            _unitOfWork.Repository<BookingTransaction>().Update(transaction);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Payment updated successfully.";
            return RedirectToAction("Details", "Booking", new { id = transaction.BookingId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Transactions.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var transaction = await _unitOfWork.Repository<BookingTransaction>()
                .GetByIdAsync(new TransactionSpec(id));

            if (transaction is null)
                return Json(new { success = false, message = "Transaction not found." });

            // The cascade removes the attachment rows but never the files on disk — same rule as
            // BookingController.Delete and PackageController.Delete.
            foreach (var attachment in transaction.Attachments)
                _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl), ReceiptsFolder);

            _unitOfWork.Repository<BookingTransaction>().Delete(transaction);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Transaction deleted successfully." });
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private async Task<TransactionListViewModel> BuildListAsync(
            DateOnly? from, DateOnly? to, int? bookingId, PaymentMethod? method, TransactionType? type, bool applied)
        {
            // Defaulted only on a first visit, so clearing the From box actually clears it. Same idiom
            // as BlockServiceController — and, as there, it's a lower bound, so the UtcNow/local-date
            // skew can only widen the range.
            if (!applied && from is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

            var transactions = await _unitOfWork.Repository<BookingTransaction>()
                .GetAllAsync(new TransactionSpec(from, to, bookingId, method, type));

            var bookingSpec = new BookingWithDetailsSpec();
            var bookings = await _unitOfWork.Repository<Booking>().GetAllAsync(bookingSpec);

            return new TransactionListViewModel
            {
                From = from,
                To = to,
                BookingId = bookingId,
                Method = method,
                Type = type,
                Applied = applied,
                BookingOptions = bookings
                    .Select(b => new SelectListItem($"{b.Code} — {b.LeadGuestName}", b.Id.ToString()))
                    .ToList(),
                Transactions = transactions.ToList(),
            };
        }

        private async Task<Booking?> LoadBookingAsync(int id) =>
            await _unitOfWork.Repository<Booking>()
                .GetByIdAsync(new BaseSpecifications<Booking>(b => b.Id == id));

        /// <param name="alreadyHeld">
        /// Receipts the transaction will still have after this save — existing rows minus the ones
        /// ticked for removal. The cap is checked against the RESULTING count, not the uploaded one,
        /// which is what lets an operator swap a receipt in a single save: tick the old, add the new.
        /// </param>
        private void ValidateFiles(IEnumerable<IFormFile> files, int alreadyHeld = 0)
        {
            var uploaded = files.Where(f => f.Length > 0).ToList();

            foreach (var file in uploaded)
            {
                if (AttachmentRules.Reject(file.FileName, file.Length) is { } reason)
                    ModelState.AddModelError(nameof(TransactionFormViewModel.ReceiptFiles), $"{file.FileName}: {reason}");
            }

            if (alreadyHeld + uploaded.Count > AttachmentRules.MaxReceiptFiles)
                ModelState.AddModelError(nameof(TransactionFormViewModel.ReceiptFiles), AttachmentRules.TooManyReceiptsMessage);
        }

        private async Task<List<TransactionAttachment>> SaveReceiptsAsync(IEnumerable<IFormFile> files)
        {
            var saved = new List<TransactionAttachment>();

            foreach (var file in files.Where(f => f.Length > 0))
            {
                await using var stream = file.OpenReadStream();
                var storedName = await _fileStorage.SaveFileAsync(stream, file.FileName, ReceiptsFolder);

                saved.Add(new TransactionAttachment
                {
                    FileUrl = $"/Files/{ReceiptsFolder}/{storedName}",
                    OriginalFileName = file.FileName,
                    UploadedAt = DateTime.UtcNow,
                });
            }

            return saved;
        }

        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
