using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.Expenses;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Expenses;
using Travel.Presentation.Helpers;
using Travel.Presentation.Services;
using Travel.Presentation.ViewModels;
using Travel.Services.Expenses;

namespace Travel.Presentation.Controllers
{
    /// <summary>
    /// What an arrival group cost, and which safe paid for it.
    ///
    /// <para>
    /// Every amount is a <b>frozen snapshot</b> — the money genuinely left the safe at that figure. When
    /// the blocks later change, the expense says so and offers Recalculate; it never re-derives itself
    /// silently, which would move a balance nobody touched.
    /// </para>
    /// </summary>
    [Authorize]
    public class ExpenseController : Controller
    {
        private const string ReceiptsFolder = "SafeAttachments";
        private const long MaxAttachmentsRequestBytes = AttachmentRules.MaxRequestBytes;

        /// <summary>
        /// The group picker's default window. A hard default rather than the soft `applied` idiom the
        /// list pages use: this is a picker, and an unbounded one would load every stay ever recorded.
        /// </summary>
        private const int PickerMonthsBack = 1;
        private const int PickerMonthsForward = 3;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IFileStorageService _fileStorage;
        private readonly ICurrentUserService _currentUser;
        private readonly IBlockServiceQuery _blocks;
        private readonly IExpenseCalculator _calculator;
        private readonly IExpenseChangeDetector _changes;
        private readonly ExpenseExcelExporter _excel;

        public ExpenseController(
            IUnitOfWork unitOfWork,
            IFileStorageService fileStorage,
            ICurrentUserService currentUser,
            IBlockServiceQuery blocks,
            IExpenseCalculator calculator,
            IExpenseChangeDetector changes,
            ExpenseExcelExporter excel)
        {
            _unitOfWork = unitOfWork;
            _fileStorage = fileStorage;
            _currentUser = currentUser;
            _blocks = blocks;
            _calculator = calculator;
            _changes = changes;
            _excel = excel;
        }

        // ─── List ───────────────────────────────────────────────────────────

        [Authorize(Policy = Permissions.Expenses.View)]
        public async Task<IActionResult> Index(
            DateOnly? from, DateOnly? to, int? safeId, int? expenseTypeId, int? destinationId,
            bool changedOnly = false, bool applied = false)
        {
            var model = await BuildListAsync(from, to, safeId, expenseTypeId, destinationId, changedOnly, applied);
            return View(model);
        }

        [Authorize(Policy = Permissions.Expenses.Export)]
        public async Task<IActionResult> Export(
            DateOnly? from, DateOnly? to, int? safeId, int? expenseTypeId, int? destinationId,
            bool changedOnly = false, bool applied = false)
        {
            var model = await BuildListAsync(from, to, safeId, expenseTypeId, destinationId, changedOnly, applied);
            var bytes = _excel.Build(model);
            return File(bytes, ExcelFormat.ContentType, ExpenseExcelExporter.FileName(model.From, model.To));
        }

        [Authorize(Policy = Permissions.Expenses.View)]
        public async Task<IActionResult> Details(int id)
        {
            var expense = await _unitOfWork.Repository<BlockExpense>().GetByIdAsync(new BlockExpenseSpec(id));
            if (expense is null)
                return NotFound();

            var current = await _changes.CurrentFingerprintsAsync(GroupKeysOf(expense));

            return View(new ExpenseDetailsViewModel
            {
                Expense = expense,
                Currency = Money.Normalize(expense.SafeEntry?.Safe?.Currency),
                SafeName = expense.SafeEntry?.Safe?.Name ?? "—",
                SafeId = expense.SafeEntry?.SafeId,
                ChangedGroups = ChangedFlags(expense, current),
            });
        }

        // ─── Create ─────────────────────────────────────────────────────────

        [Authorize(Policy = Permissions.Expenses.Create)]
        public async Task<IActionResult> Create(int? destinationId, DateOnly? checkIn)
        {
            var model = new ExpenseFormViewModel();

            // Arriving from a block's Details page pre-selects that group and centres the picker on it.
            if (destinationId is { } d && checkIn is { } c)
            {
                model.SelectedGroupKeys.Add(new ExpenseGroupKey(d, c).Token);
                model.GroupsFrom = c.AddMonths(-PickerMonthsBack);
                model.GroupsTo = c.AddMonths(PickerMonthsForward);
            }

            await PopulateFormAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Expenses.Create)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> Create(ExpenseFormViewModel model)
        {
            await PopulateFormAsync(model);

            var safe = await LoadSafeAsync(model.SafeId);
            var type = await LoadTypeAsync(model.ExpenseTypeId);

            // Create-only, deliberately: an expense whose type or safe was retired afterwards must stay
            // editable and recalculable, or a retirement makes history permanently unfixable.
            if (safe is null || !safe.IsActive)
                ModelState.AddModelError(nameof(model.SafeId), "Choose an active money safe.");

            if (type is null || !type.IsActive)
                ModelState.AddModelError(nameof(model.ExpenseTypeId), "Choose an active expense type.");

            ValidateFiles(model.ReceiptFiles);
            ApplyExchangeRate(model, safe);

            if (!ModelState.IsValid || safe is null || type is null)
                return View(model);

            var calculation = await CalculateAsync(model, type.Basis, type.IncludeChildren, type.IncludeInfants);
            if (calculation is null)
                return View(model);

            var expense = new BlockExpense
            {
                ExpenseTypeId = type.Id,
                // Snapshots — a renamed or re-ruled type must never restate an expense already paid.
                ExpenseTypeName = type.Name,
                Basis = type.Basis,
                IncludeChildren = type.IncludeChildren,
                IncludeInfants = type.IncludeInfants,
                SpentOn = model.SpentOn,
                TotalAmount = calculation.Total,
                // Snapshot of what the safe's currency was worth on the day — see BlockExpense.UnitsPerUsd.
                UnitsPerUsd = model.UnitsPerUsd,
                Description = Trimmed(model.Description),
                Reference = Trimmed(model.Reference),
                Notes = Trimmed(model.Notes),
                CreatedAt = DateTime.UtcNow,
                CreatedById = _currentUser.UserId,
                CreatedByName = _currentUser.UserName,
                Rates = BuildRates(model, type.Basis),
                Groups = BuildGroups(calculation),
            };

            // The whole graph saves at once — the entry points at the expense by NAVIGATION, not id, so
            // no round-trip is needed to learn the id. One CompleteAsync, so no explicit transaction.
            var entry = new SafeEntry
            {
                SafeId = safe.Id,
                Type = SafeEntryType.Expense,
                Amount = calculation.Total,
                OccurredOn = model.SpentOn,
                Description = Trimmed(model.Description) ?? type.Name,
                Reference = Trimmed(model.Reference),
                Notes = Trimmed(model.Notes),
                CreatedAt = DateTime.UtcNow,
                CreatedById = _currentUser.UserId,
                CreatedByName = _currentUser.UserName,
                Expense = expense,
                Attachments = await SaveReceiptsAsync(model.ReceiptFiles),
            };

            await _unitOfWork.Repository<SafeEntry>().AddAsync(entry);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Expense recorded successfully.";
            return RedirectToAction(nameof(Details), new { id = expense.Id });
        }

        // ─── Edit ───────────────────────────────────────────────────────────

        [Authorize(Policy = Permissions.Expenses.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var expense = await _unitOfWork.Repository<BlockExpense>().GetByIdAsync(new BlockExpenseSpec(id));
            if (expense is null)
                return NotFound();

            var model = new ExpenseFormViewModel
            {
                Id = expense.Id,
                SafeId = expense.SafeEntry?.SafeId,
                ExpenseTypeId = expense.ExpenseTypeId,
                SpentOn = expense.SpentOn,
                Description = expense.Description,
                Reference = expense.Reference,
                Notes = expense.Notes,
                LumpSumTotal = expense.Basis == ExpenseBasis.PerPax ? expense.TotalAmount : null,
                UnitsPerUsd = expense.UnitsPerUsd,
                SelectedGroupKeys = expense.Groups
                    .Select(g => new ExpenseGroupKey(g.DestinationId, g.CheckInDate).Token)
                    .ToList(),
                ExistingAttachments = expense.SafeEntry?.Attachments.ToList() ?? new List<SafeEntryAttachment>(),
                IsEdit = true,
            };

            await PopulateFormAsync(model, expense);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Expenses.Edit)]
        [RequestSizeLimit(MaxAttachmentsRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]
        public async Task<IActionResult> Edit(ExpenseFormViewModel model)
        {
            var expense = await _unitOfWork.Repository<BlockExpense>().GetByIdAsync(new BlockExpenseSpec(model.Id));
            if (expense is null)
                return NotFound();

            model.IsEdit = true;

            // The safe and the group selection are frozen on Edit — moving money between safes, or
            // re-scoping a record of money already paid, is delete-and-re-add. Read both back from the
            // stored rows rather than the post, so a tampered form can't do either.
            model.SafeId = expense.SafeEntry?.SafeId;
            model.ExpenseTypeId = expense.ExpenseTypeId;
            model.SelectedGroupKeys = expense.Groups
                .Select(g => new ExpenseGroupKey(g.DestinationId, g.CheckInDate).Token)
                .ToList();

            await PopulateFormAsync(model, expense);
            ValidateFiles(model.ReceiptFiles);
            ApplyExchangeRate(model, expense.SafeEntry?.Safe);

            if (!ModelState.IsValid)
            {
                model.ExistingAttachments = expense.SafeEntry?.Attachments.ToList() ?? new List<SafeEntryAttachment>();
                return View(model);
            }

            // Uses the SNAPSHOTTED rule, not the type's current one — recalculating must re-derive under
            // the rules the operator actually applied.
            var calculation = await CalculateAsync(
                model, expense.Basis, expense.IncludeChildren, expense.IncludeInfants);

            if (calculation is null)
            {
                model.ExistingAttachments = expense.SafeEntry?.Attachments.ToList() ?? new List<SafeEntryAttachment>();
                return View(model);
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                expense.SpentOn = model.SpentOn;
                expense.UnitsPerUsd = model.UnitsPerUsd;
                expense.Description = Trimmed(model.Description);
                expense.Reference = Trimmed(model.Reference);
                expense.Notes = Trimmed(model.Notes);
                expense.TotalAmount = calculation.Total;

                ReplaceRates(expense, BuildRates(model, expense.Basis));
                ReplaceGroups(expense, calculation);

                if (expense.SafeEntry is { } entry)
                {
                    entry.Amount = calculation.Total;
                    entry.OccurredOn = model.SpentOn;
                    entry.Description = Trimmed(model.Description) ?? expense.ExpenseTypeName;
                    entry.Reference = Trimmed(model.Reference);

                    await ApplyReceiptChangesAsync(entry, model);
                }

                _unitOfWork.Repository<BlockExpense>().Update(expense);
                await _unitOfWork.CompleteAsync();
                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }

            TempData["Success"] = "Expense updated successfully.";
            return RedirectToAction(nameof(Details), new { id = expense.Id });
        }

        // ─── Recalculate ────────────────────────────────────────────────────

        /// <summary>
        /// Feeds the confirm dialog. Per-room re-derives a new total from the stored rates, so the
        /// balance moves; per-pax is a lump sum that already left the safe, so the operator is asked
        /// what the real invoiced figure is — a negotiated total is never derived.
        /// </summary>
        [Authorize(Policy = Permissions.Expenses.View)]
        public async Task<IActionResult> RecalculatePreview(int id)
        {
            var expense = await _unitOfWork.Repository<BlockExpense>().GetByIdAsync(new BlockExpenseSpec(id));
            if (expense is null)
                return Json(new { success = false, message = "Expense not found." });

            var result = await RecalculateAsync(expense, expense.TotalAmount);
            if (!result.Succeeded || result.Calculation is not { } calculation)
                return Json(new { success = false, message = FirstError(result) });

            return Json(new
            {
                success = true,
                basis = (int)expense.Basis,
                currency = Money.Normalize(expense.SafeEntry?.Safe?.Currency),
                safeName = expense.SafeEntry?.Safe?.Name ?? "—",
                currentTotal = expense.TotalAmount,
                newTotal = calculation.Total,
                bookingCount = calculation.Groups.Sum(g => g.BookingCount),
                eligiblePax = calculation.Groups.Sum(g => g.EligiblePax),
                appliedBookingCount = expense.Groups.Sum(g => g.BookingCount),
                appliedEligiblePax = expense.Groups.Sum(g => g.EligiblePax),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Expenses.Edit)]
        public async Task<IActionResult> Recalculate(int id, decimal? total)
        {
            var expense = await _unitOfWork.Repository<BlockExpense>().GetByIdAsync(new BlockExpenseSpec(id));
            if (expense is null)
                return Json(new { success = false, message = "Expense not found." });

            // Per-pax re-splits whatever total the operator confirms (pre-filled with the current one).
            // Per-room ignores it and re-derives from the stored rates.
            var lumpSum = expense.Basis == ExpenseBasis.PerPax
                ? total ?? expense.TotalAmount
                : expense.TotalAmount;

            if (expense.Basis == ExpenseBasis.PerPax && lumpSum <= 0m)
                return Json(new { success = false, message = "Enter the total amount for this expense." });

            var result = await RecalculateAsync(expense, lumpSum);
            if (!result.Succeeded || result.Calculation is not { } calculation)
                return Json(new { success = false, message = FirstError(result) });

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                expense.TotalAmount = calculation.Total;
                expense.RecalculatedAt = DateTime.UtcNow;
                expense.RecalculatedByName = _currentUser.UserName;

                ReplaceGroups(expense, calculation);

                if (expense.SafeEntry is { } entry)
                    entry.Amount = calculation.Total;

                _unitOfWork.Repository<BlockExpense>().Update(expense);
                await _unitOfWork.CompleteAsync();
                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }

            return Json(new { success = true, message = "Expense recalculated successfully." });
        }

        // ─── Delete ─────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Expenses.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var expense = await _unitOfWork.Repository<BlockExpense>().GetByIdAsync(new BlockExpenseSpec(id));
            if (expense is null)
                return Json(new { success = false, message = "Expense not found." });

            // The cascade removes the entry and its attachment rows, but never the files on disk.
            foreach (var attachment in expense.SafeEntry?.Attachments ?? Enumerable.Empty<SafeEntryAttachment>())
                _fileStorage.DeleteFile(Path.GetFileName(attachment.FileUrl), ReceiptsFolder);

            _unitOfWork.Repository<BlockExpense>().Delete(expense);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Expense deleted, and its withdrawal removed from the safe." });
        }

        // ─── AJAX: group picker and live preview ────────────────────────────

        // View, not Create — the Edit form calls these too.
        [Authorize(Policy = Permissions.Expenses.View)]
        public async Task<IActionResult> Groups(DateOnly? from, DateOnly? to, int? destinationId)
        {
            var groups = await LoadGroupsAsync(from, to, destinationId);

            return Json(new
            {
                success = true,
                groups = groups.Select(g => new
                {
                    key = new ExpenseGroupKey(g.DestinationId, g.CheckInDate).Token,
                    destinationId = g.DestinationId,
                    destinationName = g.DestinationName,
                    checkIn = g.CheckInDate.ToString("yyyy-MM-dd"),
                    checkInText = g.CheckInDate.ToString("dd MMM yyyy"),
                    bookingCount = g.BookingCount,
                    pax = g.TotalPax,
                    adults = g.Adults,
                    children = g.Children,
                    infants = g.Infants,
                    rooms = ExcelFormat.FormatRooms(g.Rooms),
                    minNights = g.MinNights,
                    maxNights = g.MaxNights,
                }),
            });
        }

        /// <summary>
        /// Advisory only. The POST recomputes server-side through the same calculator and stores that,
        /// so a tampered form can never write an arbitrary amount into a safe.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Expenses.View)]
        public async Task<IActionResult> Preview(ExpenseFormViewModel model)
        {
            var type = await LoadTypeAsync(model.ExpenseTypeId);
            if (type is null)
                return Json(new { success = false, errors = new[] { new { key = nameof(model.ExpenseTypeId), message = "Choose an expense type." } } });

            var request = BuildRequest(model, type.Basis, type.IncludeChildren, type.IncludeInfants);
            var result = await _calculator.CalculateAsync(request);

            if (!result.Succeeded || result.Calculation is null)
                return Json(new
                {
                    success = false,
                    errors = result.Errors.Select(e => new { key = MapErrorKey(e.Key, model), message = e.Message }),
                });

            var safe = await LoadSafeAsync(model.SafeId);
            var balance = safe is null ? (decimal?)null : await BalanceOfAsync(safe.Id);

            return Json(new
            {
                success = true,
                basis = (int)type.Basis,
                currency = Money.Normalize(safe?.Currency),
                total = result.Calculation.Total,
                balanceAfter = balance is { } b ? b - result.Calculation.Total : (decimal?)null,
                perPerson = PerPerson(result.Calculation, type.Basis),
                groups = result.Calculation.Groups.Select(g => new
                {
                    destinationName = g.DestinationName,
                    checkInText = g.CheckInDate.ToString("dd MMM yyyy"),
                    bookingCount = g.BookingCount,
                    eligiblePax = g.EligiblePax,
                    rooms = ExcelFormat.FormatRooms(g.Rooms),
                    amount = g.Amount,
                    bookings = g.Bookings.Select(b => new
                    {
                        code = b.Code,
                        leadGuestName = b.LeadGuestName,
                        nights = b.Nights,
                        eligiblePax = b.EligiblePax,
                        rooms = ExcelFormat.FormatRooms(b.Rooms),
                        amount = b.Amount,
                    }),
                }),
            });
        }

        // ─── Building and persisting ────────────────────────────────────────

        /// <summary>
        /// Runs the calculation and pushes any failure into ModelState under the right field. Returns
        /// null when it failed, so the caller re-renders instead of saving.
        /// </summary>
        private async Task<ExpenseCalculation?> CalculateAsync(
            ExpenseFormViewModel model, ExpenseBasis basis, bool includeChildren, bool includeInfants)
        {
            var result = await _calculator.CalculateAsync(
                BuildRequest(model, basis, includeChildren, includeInfants));

            if (result.Succeeded && result.Calculation is not null)
                return result.Calculation;

            foreach (var error in result.Errors)
                ModelState.AddModelError(MapErrorKey(error.Key, model), error.Message);

            return null;
        }

        private static ExpenseCalculationRequest BuildRequest(
            ExpenseFormViewModel model, ExpenseBasis basis, bool includeChildren, bool includeInfants) =>
            new(
                Basis: basis,
                IncludeChildren: includeChildren,
                IncludeInfants: includeInfants,
                Groups: model.SelectedGroupKeys
                    .Select(ExpenseGroupKey.Parse)
                    .Where(k => k is not null)
                    .Select(k => k!)
                    .ToList(),
                Rates: model.Rates.ToDictionary(r => r.RoomType, r => r.Rate),
                LumpSumTotal: model.LumpSumTotal);

        /// <summary>
        /// Turns the calculator's room-type-keyed error into the posted row index, so the message lands
        /// under the input it is about. A collection-indexed ModelState key with no matching
        /// &lt;span asp-validation-for&gt; in the row markup is an invisible rule — the single most
        /// repeated bug in this codebase.
        /// </summary>
        private static string MapErrorKey(string key, ExpenseFormViewModel model)
        {
            if (key == nameof(ExpenseCalculationRequest.Groups))
                return nameof(ExpenseFormViewModel.SelectedGroupKeys);

            if (key == nameof(ExpenseCalculationRequest.LumpSumTotal))
                return nameof(ExpenseFormViewModel.LumpSumTotal);

            if (!key.StartsWith("Rate.", StringComparison.Ordinal))
                return key;

            var raw = key["Rate.".Length..];
            if (!int.TryParse(raw, out var typeValue))
                return key;

            var index = model.Rates.FindIndex(r => (int)r.RoomType == typeValue);
            return index >= 0
                ? $"{nameof(ExpenseFormViewModel.Rates)}[{index}].{nameof(ExpenseRateInputViewModel.Rate)}"
                : nameof(ExpenseFormViewModel.Rates);
        }

        private static List<BlockExpenseRate> BuildRates(ExpenseFormViewModel model, ExpenseBasis basis) =>
            basis != ExpenseBasis.PerRoom
                ? new List<BlockExpenseRate>()
                : model.Rates
                    .Where(r => r.Rate.HasValue)
                    .Select(r => new BlockExpenseRate { RoomType = r.RoomType, Rate = r.Rate!.Value })
                    .ToList();

        private static List<BlockExpenseGroup> BuildGroups(ExpenseCalculation calculation) =>
            calculation.Groups.Select(g => new BlockExpenseGroup
            {
                DestinationId = g.DestinationId,
                DestinationName = g.DestinationName,
                CheckInDate = g.CheckInDate,
                Amount = g.Amount,
                BookingCount = g.BookingCount,
                Adults = g.Adults,
                Children = g.Children,
                Infants = g.Infants,
                EligiblePax = g.EligiblePax,
                RoomsFingerprint = g.RoomsFingerprint,
                AppliedFingerprint = g.AppliedFingerprint,
                Bookings = g.Bookings.Select(b => new BlockExpenseBooking
                {
                    BookingId = b.BookingId,
                    BookingCode = b.Code,
                    LeadGuestName = b.LeadGuestName,
                    Amount = b.Amount,
                    Nights = b.Nights,
                    EligiblePax = b.EligiblePax,
                    Adults = b.Adults,
                    Children = b.Children,
                    Infants = b.Infants,
                    RoomsFingerprint = b.RoomsFingerprint,
                }).ToList(),
            }).ToList();

        private void ReplaceRates(BlockExpense expense, List<BlockExpenseRate> rates)
        {
            foreach (var rate in expense.Rates.ToList())
                _unitOfWork.Repository<BlockExpenseRate>().Delete(rate);

            expense.Rates.Clear();
            foreach (var rate in rates)
                expense.Rates.Add(rate);
        }

        private void ReplaceGroups(BlockExpense expense, ExpenseCalculation calculation)
        {
            foreach (var group in expense.Groups.ToList())
            {
                foreach (var booking in group.Bookings.ToList())
                    _unitOfWork.Repository<BlockExpenseBooking>().Delete(booking);

                _unitOfWork.Repository<BlockExpenseGroup>().Delete(group);
            }

            expense.Groups.Clear();
            foreach (var group in BuildGroups(calculation))
                expense.Groups.Add(group);
        }

        /// <summary>Re-derives an existing expense over its own groups, using its snapshotted rule and rates.</summary>
        private Task<ExpenseCalculationResult> RecalculateAsync(BlockExpense expense, decimal lumpSum) =>
            _calculator.CalculateAsync(new ExpenseCalculationRequest(
                Basis: expense.Basis,
                IncludeChildren: expense.IncludeChildren,
                IncludeInfants: expense.IncludeInfants,
                Groups: expense.Groups
                    .Select(g => new ExpenseGroupKey(g.DestinationId, g.CheckInDate))
                    .ToList(),
                Rates: expense.Rates.ToDictionary(r => r.RoomType, r => (decimal?)r.Rate),
                LumpSumTotal: lumpSum));

        private static string FirstError(ExpenseCalculationResult result) =>
            result.Errors.FirstOrDefault()?.Message ?? "This expense can no longer be recalculated.";

        // ─── Loading ────────────────────────────────────────────────────────

        private async Task<ExpenseListViewModel> BuildListAsync(
            DateOnly? from, DateOnly? to, int? safeId, int? expenseTypeId, int? destinationId,
            bool changedOnly, bool applied)
        {
            // Defaulted only on a first visit, so clearing the From box actually clears it.
            if (!applied && from is null)
                from = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

            var expenses = (await _unitOfWork.Repository<BlockExpense>()
                .GetAllAsync(new BlockExpenseSpec(from, to, safeId, expenseTypeId, destinationId)))
                .ToList();

            // One detector call for every group on the page, regardless of how many expenses.
            var current = await _changes.CurrentFingerprintsAsync(expenses.SelectMany(GroupKeysOf));

            var cards = expenses
                .Select(e => new ExpenseCardViewModel(
                    Expense: e,
                    Currency: Money.Normalize(e.SafeEntry?.Safe?.Currency),
                    SafeName: e.SafeEntry?.Safe?.Name ?? "—",
                    SafeId: e.SafeEntry?.SafeId,
                    HasChanged: ChangedFlags(e, current).Values.Any(changed => changed)))
                .Where(card => !changedOnly || card.HasChanged)
                .ToList();

            return new ExpenseListViewModel
            {
                From = from,
                To = to,
                SafeId = safeId,
                ExpenseTypeId = expenseTypeId,
                DestinationId = destinationId,
                ChangedOnly = changedOnly,
                Applied = applied,
                SafeOptions = await LoadSafeOptionsAsync(includeInactive: true),
                TypeOptions = await LoadTypeOptionsAsync(includeInactive: true),
                DestinationOptions = await LoadDestinationsAsync(),
                Expenses = cards,
            };
        }

        /// <summary>
        /// Every render path goes through this — both GETs and both POSTs' re-render — so the dropdowns
        /// and the picker can never be missed on one branch.
        /// </summary>
        private async Task PopulateFormAsync(ExpenseFormViewModel model, BlockExpense? existing = null)
        {
            // Retired options still have to appear on Edit, or an expense paid from a since-retired safe
            // would render with an empty dropdown and fail to save.
            model.SafeOptions = await LoadSafeOptionsAsync(includeInactive: existing is not null);
            model.TypeOptions = await LoadTypeOptionsAsync(includeInactive: existing is not null);
            model.DestinationOptions = await LoadDestinationsAsync();

            EnsureRateRows(model, existing);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            model.GroupsFrom ??= today.AddMonths(-PickerMonthsBack);
            model.GroupsTo ??= today.AddMonths(PickerMonthsForward);

            model.AvailableGroups = await LoadGroupsAsync(
                model.GroupsFrom, model.GroupsTo, model.GroupsDestinationId, model.SelectedGroupKeys);
        }

        /// <summary>
        /// One row per room type in enum order, so the posted indices are stable across a re-render and
        /// the validation spans keep pointing at the right input.
        /// </summary>
        private static void EnsureRateRows(ExpenseFormViewModel model, BlockExpense? existing)
        {
            var posted = model.Rates.ToDictionary(r => r.RoomType, r => r.Rate);
            var stored = existing?.Rates.ToDictionary(r => r.RoomType, r => (decimal?)r.Rate)
                         ?? new Dictionary<RoomType, decimal?>();

            model.Rates = Enum.GetValues<RoomType>()
                .OrderBy(t => t)
                .Select(t => new ExpenseRateInputViewModel
                {
                    RoomType = t,
                    Rate = posted.TryGetValue(t, out var p) ? p : stored.GetValueOrDefault(t),
                })
                .ToList();
        }

        /// <summary>
        /// The picker's groups. Any group already selected is force-included even when it falls outside
        /// the current filter, so narrowing the dates can't silently drop a selection.
        /// </summary>
        private async Task<IReadOnlyList<BlockSummary>> LoadGroupsAsync(
            DateOnly? from, DateOnly? to, int? destinationId, IEnumerable<string>? selectedKeys = null)
        {
            var arrivals = await _blocks.GetArrivalsAsync(from, to, destinationId, null);
            var groups = arrivals.Blocks.Concat(arrivals.Singles).ToList();

            var selected = (selectedKeys ?? Enumerable.Empty<string>())
                .Select(ExpenseGroupKey.Parse)
                .Where(k => k is not null)
                .Select(k => (k!.DestinationId, k.CheckInDate))
                .ToHashSet();

            var present = groups.Select(g => (g.DestinationId, g.CheckInDate)).ToHashSet();
            var missing = selected.Where(k => !present.Contains(k)).ToList();

            foreach (var key in missing)
            {
                var file = await _blocks.GetBlockAsync(key.DestinationId, key.CheckInDate);
                if (file is not null)
                    groups.Add(file.Summary);
            }

            return groups
                .OrderBy(g => g.CheckInDate)
                .ThenBy(g => g.DestinationName)
                .ToList();
        }

        private async Task<IReadOnlyList<SafeOptionViewModel>> LoadSafeOptionsAsync(bool includeInactive)
        {
            var spec = new BaseSpecifications<MoneySafe>();
            spec.SetOrderBy(s => s.Name);
            var safes = await _unitOfWork.Repository<MoneySafe>().GetAllAsync(spec);

            return safes
                .Where(s => includeInactive || s.IsActive)
                .Select(s => new SafeOptionViewModel(s.Id, s.Name, Money.Normalize(s.Currency)))
                .ToList();
        }

        private async Task<IReadOnlyList<ExpenseTypeOptionViewModel>> LoadTypeOptionsAsync(bool includeInactive)
        {
            var spec = new BaseSpecifications<ExpenseType>();
            spec.SetOrderBy(t => t.Name);
            var types = await _unitOfWork.Repository<ExpenseType>().GetAllAsync(spec);

            return types
                .Where(t => includeInactive || t.IsActive)
                .Select(t => new ExpenseTypeOptionViewModel(
                    t.Id, t.Name, t.Basis, t.IncludeChildren, t.IncludeInfants))
                .ToList();
        }

        private async Task<IReadOnlyList<Destination>> LoadDestinationsAsync()
        {
            var spec = new BaseSpecifications<Destination>();
            spec.SetOrderBy(d => d.Name);
            return (await _unitOfWork.Repository<Destination>().GetAllAsync(spec)).ToList();
        }

        private async Task<MoneySafe?> LoadSafeAsync(int? id) =>
            id is null
                ? null
                : await _unitOfWork.Repository<MoneySafe>()
                    .GetByIdAsync(new BaseSpecifications<MoneySafe>(s => s.Id == id));

        private async Task<ExpenseType?> LoadTypeAsync(int? id) =>
            id is null
                ? null
                : await _unitOfWork.Repository<ExpenseType>()
                    .GetByIdAsync(new BaseSpecifications<ExpenseType>(t => t.Id == id));

        private async Task<decimal> BalanceOfAsync(int safeId) =>
            (await _unitOfWork.Repository<SafeEntry>().GetAllAsync(new SafeEntriesOfSafeSpec(safeId)))
                .Sum(e => e.SignedAmount);

        private static IEnumerable<(int DestinationId, DateOnly CheckInDate)> GroupKeysOf(BlockExpense expense) =>
            expense.Groups.Select(g => (g.DestinationId, g.CheckInDate));

        private static Dictionary<int, bool> ChangedFlags(
            BlockExpense expense, IReadOnlyDictionary<(int, DateOnly), string> current) =>
            expense.Groups.ToDictionary(
                g => g.Id,
                // A group that has emptied entirely has no entry, so its live fingerprint is "" — which
                // never matches, and it correctly reads "changed" rather than silently passing.
                g => !ExpenseFingerprint.Matches(
                    g.AppliedFingerprint,
                    current.GetValueOrDefault((g.DestinationId, g.CheckInDate), string.Empty)));

        private static decimal? PerPerson(ExpenseCalculation calculation, ExpenseBasis basis)
        {
            if (basis != ExpenseBasis.PerPax)
                return null;

            var pax = calculation.Groups.Sum(g => g.EligiblePax);
            return pax == 0 ? null : Math.Round(calculation.Total / pax, 2, MidpointRounding.AwayFromZero);
        }

        // ─── Receipts ───────────────────────────────────────────────────────

        private async Task ApplyReceiptChangesAsync(SafeEntry entry, ExpenseFormViewModel model)
        {
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
        }

        private void ValidateFiles(IEnumerable<IFormFile> files)
        {
            foreach (var file in files.Where(f => f.Length > 0))
            {
                if (AttachmentRules.Reject(file.FileName, file.Length) is { } reason)
                    ModelState.AddModelError(
                        nameof(ExpenseFormViewModel.ReceiptFiles), $"{file.FileName}: {reason}");
            }
        }

        /// <summary>
        /// A safe already in USD needs no rate, so one is **forced** to 1 rather than trusted from the
        /// post — the same call <c>ApplyBasisRules</c> makes clearing the include flags on a per-room
        /// type. Any other currency must carry one, because the Revenue report cannot convert without
        /// it and a missing rate would quietly understate what a trip cost.
        ///
        /// <para>
        /// Cross-field, so it lives here rather than in a <c>[Required]</c> attribute — the rule
        /// depends on which safe was chosen. The client-side half in <c>expense-form.js</c> only hides
        /// the input; it is not trusted.
        /// </para>
        /// </summary>
        private void ApplyExchangeRate(ExpenseFormViewModel model, MoneySafe? safe)
        {
            if (safe is null)
                return;

            if (IsBaseCurrency(safe.Currency))
            {
                model.UnitsPerUsd = 1m;
                ModelState.Remove(nameof(ExpenseFormViewModel.UnitsPerUsd));
                return;
            }

            if (model.UnitsPerUsd is not { } rate || rate <= 0m)
                ModelState.AddModelError(
                    nameof(ExpenseFormViewModel.UnitsPerUsd),
                    $"Enter how many {Money.Normalize(safe.Currency)} equal one {Money.Code} — this expense can't be "
                    + "counted against revenue without it.");
        }

        /// <summary>Whether a safe is already in the currency bookings are priced in.</summary>
        private static bool IsBaseCurrency(string? currency) =>
            string.Equals(Money.Normalize(currency), Money.Code, StringComparison.Ordinal);

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
