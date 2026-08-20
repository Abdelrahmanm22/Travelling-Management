using System.ComponentModel.DataAnnotations;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;

namespace Travel.Presentation.ViewModels
{
    /// <summary>A safe the expense can be paid from. Carries the currency so the form can restamp its prefixes live.</summary>
    public sealed record SafeOptionViewModel(int Id, string Name, string Currency);

    /// <summary>
    /// An expense type the form can pick. Carries the whole rule, not just a label — the rates card
    /// swaps between per-room and per-pax purely from these attributes.
    /// </summary>
    public sealed record ExpenseTypeOptionViewModel(
        int Id, string Name, ExpenseBasis Basis, bool IncludeChildren, bool IncludeInfants);

    /// <summary>
    /// One room type's rate row. <see cref="Rate"/> is nullable because <b>0 is a legal rate</b>
    /// (deliberately free) and only an <i>absent</i> one is an error — a non-nullable decimal cannot
    /// tell those apart.
    /// </summary>
    public class ExpenseRateInputViewModel
    {
        public RoomType RoomType { get; set; }

        [Range(0, 9_999_999, ErrorMessage = "Rate must be zero or more.")]
        public decimal? Rate { get; set; }
    }

    public class ExpenseFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Choose the safe this is paid from.")]
        [Display(Name = "Paid from")]
        public int? SafeId { get; set; }

        [Required(ErrorMessage = "Choose an expense type.")]
        [Display(Name = "Expense type")]
        public int? ExpenseTypeId { get; set; }

        [Required(ErrorMessage = "Date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Spent on")]
        public DateOnly SpentOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

        [StringLength(200)]
        public string? Description { get; set; }

        [StringLength(100)]
        public string? Reference { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        /// <summary>
        /// Arrival-group keys as "{destinationId}|{yyyy-MM-dd}". Bound from repeated checkbox inputs, so
        /// it must stay non-nullable and initialised: when nothing is ticked nothing posts, and the
        /// empty list correctly means "none selected". Never use asp-for on it — that emits a companion
        /// hidden "false" input which corrupts the list.
        /// </summary>
        [Display(Name = "Arrival groups")]
        public List<string> SelectedGroupKeys { get; set; } = new();

        /// <summary>Per-room only. One row per RoomType, in enum order, so the posted indices are stable.</summary>
        public List<ExpenseRateInputViewModel> Rates { get; set; } = new();

        /// <summary>Per-pax only: one total for the WHOLE expense, split across every selected group.</summary>
        [Display(Name = "Total amount")]
        public decimal? LumpSumTotal { get; set; }

        /// <summary>
        /// Units of the safe's currency per US dollar. Required only when the safe isn't already in
        /// USD — the rule is cross-field, so it lives in the controller rather than in an attribute,
        /// the same call <c>ValidatePricing</c> makes on the booking form.
        /// </summary>
        [Display(Name = "Exchange rate")]
        [Range(0.000001, 9_999_999, ErrorMessage = "The rate must be greater than zero.")]
        public decimal? UnitsPerUsd { get; set; }

        [Display(Name = "Receipts")]
        public List<IFormFile> ReceiptFiles { get; set; } = new();

        public List<int> RemoveAttachmentIds { get; set; } = new();

        public IReadOnlyList<SafeEntryAttachment> ExistingAttachments { get; set; } = Array.Empty<SafeEntryAttachment>();

        // ─── Form context, repopulated on every render path ─────────────────

        public IReadOnlyList<SafeOptionViewModel> SafeOptions { get; set; } = Array.Empty<SafeOptionViewModel>();
        public IReadOnlyList<ExpenseTypeOptionViewModel> TypeOptions { get; set; } = Array.Empty<ExpenseTypeOptionViewModel>();
        public IReadOnlyList<Destination> DestinationOptions { get; set; } = Array.Empty<Destination>();

        /// <summary>The picker's own filter. Bounded by default — an unbounded picker loads every stay ever recorded.</summary>
        public DateOnly? GroupsFrom { get; set; }
        public DateOnly? GroupsTo { get; set; }
        public int? GroupsDestinationId { get; set; }

        public IReadOnlyList<BlockSummary> AvailableGroups { get; set; } = Array.Empty<BlockSummary>();

        /// <summary>
        /// True on Edit: the safe and the group selection are frozen. Moving money between safes, or
        /// re-scoping a record of money already paid, is delete-and-re-add, not an edit.
        /// </summary>
        public bool IsEdit { get; set; }

        public string CurrencyOf(int? safeId) =>
            SafeOptions.FirstOrDefault(s => s.Id == safeId)?.Currency ?? string.Empty;
    }

    /// <summary>One expense on the list, with the "blocks changed" verdict already resolved.</summary>
    public sealed record ExpenseCardViewModel(
        BlockExpense Expense,
        string Currency,
        string SafeName,
        int? SafeId,
        bool HasChanged);

    public class ExpenseListViewModel
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public int? SafeId { get; set; }
        public int? ExpenseTypeId { get; set; }
        public int? DestinationId { get; set; }
        public bool ChangedOnly { get; set; }
        public bool Applied { get; set; }

        public IReadOnlyList<SafeOptionViewModel> SafeOptions { get; set; } = Array.Empty<SafeOptionViewModel>();
        public IReadOnlyList<ExpenseTypeOptionViewModel> TypeOptions { get; set; } = Array.Empty<ExpenseTypeOptionViewModel>();
        public IReadOnlyList<Destination> DestinationOptions { get; set; } = Array.Empty<Destination>();

        public IReadOnlyList<ExpenseCardViewModel> Expenses { get; set; } = Array.Empty<ExpenseCardViewModel>();

        public int ChangedCount => Expenses.Count(e => e.HasChanged);

        /// <summary>
        /// Totals are per currency and never summed across them — the safes hold different money, and a
        /// combined figure would be meaningless.
        /// </summary>
        public IReadOnlyList<(string Currency, decimal Total)> TotalsByCurrency =>
            Expenses
                .GroupBy(e => e.Currency)
                .Select(g => (Currency: g.Key, Total: g.Sum(e => e.Expense.TotalAmount)))
                .OrderBy(t => t.Currency)
                .ToList();

        public int GroupsTouched => Expenses.SelectMany(e => e.Expense.Groups)
            .Select(g => (g.DestinationId, g.CheckInDate))
            .Distinct()
            .Count();
    }

    public class ExpenseDetailsViewModel
    {
        public BlockExpense Expense { get; set; } = null!;
        public string Currency { get; set; } = string.Empty;
        public string SafeName { get; set; } = string.Empty;
        public int? SafeId { get; set; }

        /// <summary>Group id → whether its arrival group still matches what was paid for.</summary>
        public IReadOnlyDictionary<int, bool> ChangedGroups { get; set; } = new Dictionary<int, bool>();

        public bool HasChanged => ChangedGroups.Values.Any(changed => changed);
    }

    /// <summary>
    /// What <c>BlockService/Details</c> binds to now that it carries an expenses card as well as the
    /// block file itself.
    /// </summary>
    public class BlockDetailsViewModel
    {
        public BlockFile File { get; set; } = null!;

        public IReadOnlyList<BlockExpenseLine> Expenses { get; set; } = Array.Empty<BlockExpenseLine>();
    }

    /// <summary>
    /// One line of the read-only expenses card on a block's Details page. Screen-only — the block file
    /// itself goes to the hotel supplier, and what we paid is ours.
    /// </summary>
    public sealed record BlockExpenseLine(
        int ExpenseId,
        string TypeName,
        ExpenseBasis Basis,
        DateOnly SpentOn,
        string Currency,
        string SafeName,
        decimal GroupAmount,
        bool HasChanged);
}
