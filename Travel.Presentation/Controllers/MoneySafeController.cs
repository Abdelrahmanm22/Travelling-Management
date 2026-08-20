using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Expenses;
using Travel.Presentation.Helpers;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// The company's cash boxes and everything that moves through them. A safe's ledger holds manual
    /// deposits and withdrawals plus the withdrawal behind each block expense — one table, so the
    /// balance is one formula.
    ///
    /// <para>
    /// Deliberately separate from the booking payment ledger (<c>TransactionController</c>): what a
    /// client pays and what the company spends are different books, and nothing here touches
    /// <c>BookingTransaction</c>.
    /// </para>
    /// </summary>
    [Authorize]
    public class MoneySafeController : Controller
    {
        private const string ReceiptsFolder = "SafeAttachments";
        private const long MaxAttachmentsRequestBytes = AttachmentRules.MaxRequestBytes;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IFileStorageService _fileStorage;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly SafeLedgerExcelExporter _excel;

        public MoneySafeController(
            IUnitOfWork unitOfWork,
            IFileStorageService fileStorage,
            ICurrentUserService currentUser,
            IMapper mapper,
            SafeLedgerExcelExporter excel)
        {
            _unitOfWork = unitOfWork;
            _fileStorage = fileStorage;
            _currentUser = currentUser;
            _mapper = mapper;
            _excel = excel;
        }

        // ─── Safes ──────────────────────────────────────────────────────────

        [Authorize(Policy = Permissions.MoneySafes.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<MoneySafe>();
            spec.SetOrderBy(s => s.Name);
            var safes = await _unitOfWork.Repository<MoneySafe>().GetAllAsync(spec);

            // One query for every entry, grouped in memory — not one balance query per card.
            var entries = await _unitOfWork.Repository<SafeEntry>().GetAllAsync(new SafeBalanceSpec());
            var bySafe = entries.GroupBy(e => e.SafeId).ToDictionary(g => g.Key, g => g.ToList());

            var cards = safes.Select(safe =>
            {
                var rows = bySafe.GetValueOrDefault(safe.Id) ?? new List<SafeEntry>();

                return new MoneySafeCardViewModel(
                    Safe: safe,
                    Balance: rows.Sum(e => e.SignedAmount),
                    TotalIn: rows.Where(e => e.Type == SafeEntryType.Deposit).Sum(e => e.Amount),
                    TotalOut: rows.Where(e => e.Type != SafeEntryType.Deposit).Sum(e => e.Amount),
                    EntryCount: rows.Count);
            }).ToList();

            return View(new MoneySafeListViewModel { Safes = cards });
        }

        [Authorize(Policy = Permissions.MoneySafes.Create)]
        public IActionResult Create() => View(new MoneySafeViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MoneySafes.Create)]
        public async Task<IActionResult> Create(MoneySafeViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var safe = _mapper.Map<MoneySafe>(model);
            safe.Currency = Money.Normalize(model.Currency);
            safe.CreatedAt = DateTime.UtcNow;
            safe.CreatedById = _currentUser.UserId;
            safe.CreatedByName = _currentUser.UserName;

            await _unitOfWork.Repository<MoneySafe>().AddAsync(safe);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Money safe created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.MoneySafes.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var safe = await LoadSafeAsync(id);
            if (safe is null)
                return NotFound();

            var model = _mapper.Map<MoneySafeViewModel>(safe);
            model.HasEntries = await HasEntriesAsync(id);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MoneySafes.Edit)]
        public async Task<IActionResult> Edit(MoneySafeViewModel model)
        {
            var safe = await LoadSafeAsync(model.Id);
            if (safe is null)
                return NotFound();

            var hasEntries = await HasEntriesAsync(safe.Id);
            model.HasEntries = hasEntries;

            // A locked currency isn't posted at all (the input is read-only and disabled), so validate
            // against the stored value rather than failing the save on a field the user can't fill.
            if (hasEntries)
            {
                model.Currency = safe.Currency;
                ModelState.Remove(nameof(MoneySafeViewModel.Currency));
            }

            if (!ModelState.IsValid)
                return View(model);

            _mapper.Map(model, safe);

            // Currency is Ignore()d in the profile — it is set here only while the safe is still empty.
            // Once it holds a movement, changing it would re-denominate every historical figure.
            if (!hasEntries)
                safe.Currency = Money.Normalize(model.Currency);

            _unitOfWork.Repository<MoneySafe>().Update(safe);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Money safe updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MoneySafes.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var safe = await LoadSafeAsync(id);
            if (safe is null)
                return Json(new { success = false, message = "Money safe not found." });

            // The FK cascades, so this guard is the real protection: deleting a safe with history would
            // take its ledger — and the withdrawals behind real expenses — with it. Retiring is
            // IsActive = false.
            if (await HasEntriesAsync(id))
                return Json(new
                {
                    success = false,
                    message = "This safe has movements recorded against it and can't be deleted. Mark it inactive instead."
                });

            _unitOfWork.Repository<MoneySafe>().Delete(safe);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Money safe deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.MoneySafes.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<MoneySafe>()
                .GetAllAsync(new BaseSpecifications<MoneySafe>(s => s.Name == name && s.Id != id));

            return Json(!matches.Any());
        }

        // ─── Ledger ─────────────────────────────────────────────────────────

        [Authorize(Policy = Permissions.MoneySafes.View)]
        public async Task<IActionResult> Details(
            int id, DateOnly? from, DateOnly? to, SafeEntryType? type, bool applied = false)
        {
            var model = await BuildLedgerAsync(id, from, to, type, applied);
            return model is null ? NotFound() : View(model);
        }

        [Authorize(Policy = Permissions.MoneySafes.Export)]
        public async Task<IActionResult> Export(
            int id, DateOnly? from, DateOnly? to, SafeEntryType? type, bool applied = false)
        {
            var model = await BuildLedgerAsync(id, from, to, type, applied);
            if (model is null)
                return NotFound();

            var bytes = _excel.Build(model);
            return File(bytes, ExcelFormat.ContentType, SafeLedgerExcelExporter.FileName(model));
        }

        // ─── Manual movements ───────────────────────────────────────────────

        [Authorize(Policy = Permissions.MoneySafes.Edit)]
        public async Task<IActionResult> CreateEntry(int safeId)
        {
            var safe = await LoadSafeAsync(safeId);
            if (safe is null)
                return NotFound();

            return View(new SafeEntryFormViewModel
            {
                SafeId = safe.Id,
                SafeName = safe.Name,
                Currency = safe.Currency,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MoneySafes.Edit)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> CreateEntry(SafeEntryFormViewModel model)
        {
            var safe = await LoadSafeAsync(model.SafeId);
            if (safe is null)
                return NotFound();

            model.SafeName = safe.Name;
            model.Currency = safe.Currency;

            ValidateEntryType(model.Type);
            ValidateFiles(model.ReceiptFiles);

            if (!ModelState.IsValid)
                return View(model);

            var entry = new SafeEntry
            {
                SafeId = safe.Id,
                Type = model.Type,
                Amount = model.Amount,
                OccurredOn = model.OccurredOn,
                Description = Trimmed(model.Description),
                Reference = Trimmed(model.Reference),
                Notes = Trimmed(model.Notes),
                CreatedAt = DateTime.UtcNow,
                CreatedById = _currentUser.UserId,
                CreatedByName = _currentUser.UserName,
                Attachments = await SaveReceiptsAsync(model.ReceiptFiles),
            };

            await _unitOfWork.Repository<SafeEntry>().AddAsync(entry);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Movement recorded successfully.";
            return RedirectToAction(nameof(Details), new { id = safe.Id });
        }

        [Authorize(Policy = Permissions.MoneySafes.Edit)]
        public async Task<IActionResult> EditEntry(int id)
        {
            var entry = await _unitOfWork.Repository<SafeEntry>().GetByIdAsync(new SafeEntrySpec(id));
            if (entry is null)
                return NotFound();

            if (entry.ExpenseId is { } expenseId)
                return RejectExpenseEntry(expenseId);

            return View(new SafeEntryFormViewModel
            {
                Id = entry.Id,
                SafeId = entry.SafeId,
                SafeName = entry.Safe?.Name ?? string.Empty,
                Currency = entry.Safe?.Currency ?? string.Empty,
                Type = entry.Type,
                Amount = entry.Amount,
                OccurredOn = entry.OccurredOn,
                Description = entry.Description,
                Reference = entry.Reference,
                Notes = entry.Notes,
                ExistingAttachments = entry.Attachments.ToList(),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MoneySafes.Edit)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> EditEntry(SafeEntryFormViewModel model)
        {
            var entry = await _unitOfWork.Repository<SafeEntry>().GetByIdAsync(new SafeEntrySpec(model.Id));
            if (entry is null)
                return NotFound();

            if (entry.ExpenseId is { } expenseId)
                return RejectExpenseEntry(expenseId);

            model.SafeName = entry.Safe?.Name ?? string.Empty;
            model.Currency = entry.Safe?.Currency ?? string.Empty;

            ValidateEntryType(model.Type);
            ValidateFiles(model.ReceiptFiles);

            if (!ModelState.IsValid)
            {
                model.ExistingAttachments = entry.Attachments.ToList();
                return View(model);
            }

            // SafeId is deliberately not read from the post: a movement never moves between safes, and
            // honouring a tampered value would take money off one balance and put it on another.
            entry.Type = model.Type;
            entry.Amount = model.Amount;
            entry.OccurredOn = model.OccurredOn;
            entry.Description = Trimmed(model.Description);
            entry.Reference = Trimmed(model.Reference);
            entry.Notes = Trimmed(model.Notes);

            foreach (var attachmentId in model.RemoveAttachmentIds)
            {
                var attachment = entry.Attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment is null)
                    continue;

                _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl), ReceiptsFolder);
                _unitOfWork.Repository<SafeEntryAttachment>().Delete(attachment);
            }

            foreach (var added in await SaveReceiptsAsync(model.ReceiptFiles))
                entry.Attachments.Add(added);

            _unitOfWork.Repository<SafeEntry>().Update(entry);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Movement updated successfully.";
            return RedirectToAction(nameof(Details), new { id = entry.SafeId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.MoneySafes.Delete)]
        public async Task<IActionResult> DeleteEntry(int id)
        {
            var entry = await _unitOfWork.Repository<SafeEntry>().GetByIdAsync(new SafeEntrySpec(id));
            if (entry is null)
                return Json(new { success = false, message = "Movement not found." });

            if (entry.ExpenseId is not null)
                return Json(new
                {
                    success = false,
                    message = "This movement belongs to a block expense. Delete the expense instead — it removes this withdrawal with it."
                });

            // The cascade removes the attachment rows but never the files on disk.
            foreach (var attachment in entry.Attachments)
                _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl), ReceiptsFolder);

            _unitOfWork.Repository<SafeEntry>().Delete(entry);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Movement deleted successfully." });
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private async Task<SafeLedgerViewModel?> BuildLedgerAsync(
            int id, DateOnly? from, DateOnly? to, SafeEntryType? type, bool applied)
        {
            var safe = await LoadSafeAsync(id);
            if (safe is null)
                return null;

            // Defaulted only on a first visit, so clearing the From box actually clears it. As
            // elsewhere it's a lower bound, so the UtcNow/local-date skew can only widen the range.
            if (!applied && from is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

            var entries = await _unitOfWork.Repository<SafeEntry>()
                .GetAllAsync(new SafeEntrySpec(id, from, to, type));

            // The headline balance is the safe's whole life, not the filtered slice — a date filter
            // must never look like money leaving the safe.
            var all = await _unitOfWork.Repository<SafeEntry>().GetAllAsync(new SafeEntriesOfSafeSpec(id));

            return new SafeLedgerViewModel
            {
                Safe = safe,
                From = from,
                To = to,
                Type = type,
                Applied = applied,
                Entries = entries.ToList(),
                Balance = all.Sum(e => e.SignedAmount),
            };
        }

        private async Task<MoneySafe?> LoadSafeAsync(int id) =>
            await _unitOfWork.Repository<MoneySafe>()
                .GetByIdAsync(new BaseSpecifications<MoneySafe>(s => s.Id == id));

        private async Task<bool> HasEntriesAsync(int safeId) =>
            (await _unitOfWork.Repository<SafeEntry>().GetAllAsync(new SafeEntriesOfSafeSpec(safeId))).Any();

        /// <summary>
        /// An expense's withdrawal is owned by the expense — editing it here would leave the two
        /// disagreeing about what was paid. Enum.IsDefined alone would let it through, which is why
        /// this is its own explicit check.
        /// </summary>
        private IActionResult RejectExpenseEntry(int expenseId)
        {
            TempData["Error"] = "That movement belongs to a block expense. Edit the expense instead.";
            return RedirectToAction("Details", "Expense", new { id = expenseId });
        }

        private void ValidateEntryType(SafeEntryType type)
        {
            if (type is SafeEntryType.Deposit or SafeEntryType.Withdrawal)
                return;

            ModelState.AddModelError(nameof(SafeEntryFormViewModel.Type),
                "Choose deposit or withdrawal. Expenses are recorded from the Block Expenses screen.");
        }

        private void ValidateFiles(IEnumerable<IFormFile> files)
        {
            foreach (var file in files.Where(f => f.Length > 0))
            {
                if (AttachmentRules.Reject(file.FileName, file.Length) is { } reason)
                    ModelState.AddModelError(
                        nameof(SafeEntryFormViewModel.ReceiptFiles), $"{file.FileName}: {reason}");
            }
        }

        private async Task<List<SafeEntryAttachment>> SaveReceiptsAsync(IEnumerable<IFormFile> files)
        {
            var saved = new List<SafeEntryAttachment>();

            foreach (var file in files.Where(f => f.Length > 0))
            {
                await using var stream = file.OpenReadStream();
                var storedName = await _fileStorage.SaveFileAsync(stream, file.FileName, ReceiptsFolder);

                saved.Add(new SafeEntryAttachment
                {
                    FileUrl = $"/Files/{ReceiptsFolder}/{storedName}",
                    OriginalFileName = file.FileName,
                    UploadedAt = DateTime.UtcNow,
                    UploadedByName = _currentUser.UserName,
                });
            }

            return saved;
        }

        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
