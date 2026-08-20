using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Presentation.ViewModels
{
    public class MoneySafeViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(150)]
        [Remote(action: "IsNameAvailable", controller: "MoneySafe", AdditionalFields = nameof(Id),
            ErrorMessage = "A safe with this name already exists.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Currency is required.")]
        [StringLength(3, MinimumLength = 3, ErrorMessage = "Use the three-letter code, e.g. EGP or USD.")]
        [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Use the three-letter code, e.g. EGP or USD.")]
        public string Currency { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Set on Edit. Once a safe holds any movement its currency is locked — changing it would
        /// silently re-denominate every historical figure in the ledger — so the field renders
        /// read-only and the controller reads the stored value back instead of the posted one.
        /// </summary>
        public bool HasEntries { get; set; }
    }

    /// <summary>One safe on the grid, with the figures derived from its ledger.</summary>
    public sealed record MoneySafeCardViewModel(
        MoneySafe Safe,
        decimal Balance,
        decimal TotalIn,
        decimal TotalOut,
        int EntryCount);

    public class MoneySafeListViewModel
    {
        public IReadOnlyList<MoneySafeCardViewModel> Safes { get; set; } = Array.Empty<MoneySafeCardViewModel>();
    }

    /// <summary>One safe's ledger — the filter state plus the movements it selected.</summary>
    public class SafeLedgerViewModel
    {
        public MoneySafe Safe { get; set; } = null!;

        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public SafeEntryType? Type { get; set; }

        /// <summary>
        /// Distinguishes "first visit" from "the operator deliberately cleared the From box", so the
        /// default date can't silently refill itself. Same idiom as the transactions and block lists.
        /// </summary>
        public bool Applied { get; set; }

        public IReadOnlyList<SafeEntry> Entries { get; set; } = Array.Empty<SafeEntry>();

        /// <summary>The safe's whole-life balance, not the filtered slice — a filter must not look like a payout.</summary>
        public decimal Balance { get; set; }

        public decimal FilteredIn => Entries.Where(e => e.Type == SafeEntryType.Deposit).Sum(e => e.Amount);
        public decimal FilteredOut => Entries.Where(e => e.Type != SafeEntryType.Deposit).Sum(e => e.Amount);
        public decimal FilteredNet => FilteredIn - FilteredOut;
    }

    public class SafeEntryFormViewModel
    {
        public int Id { get; set; }

        public int SafeId { get; set; }

        public string SafeName { get; set; } = string.Empty;

        public string Currency { get; set; } = string.Empty;

        [Display(Name = "Movement")]
        public SafeEntryType Type { get; set; } = SafeEntryType.Deposit;

        [Required(ErrorMessage = "Amount is required.")]
        [Range(0.01, 999_999_999, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Date")]
        public DateOnly OccurredOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

        [StringLength(200)]
        public string? Description { get; set; }

        [StringLength(100)]
        public string? Reference { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Display(Name = "Receipts")]
        public List<IFormFile> ReceiptFiles { get; set; } = new();

        /// <summary>Posted ids of existing receipts ticked for removal — a file input can't be repopulated from a response.</summary>
        public List<int> RemoveAttachmentIds { get; set; } = new();

        public IReadOnlyList<SafeEntryAttachment> ExistingAttachments { get; set; } = Array.Empty<SafeEntryAttachment>();
    }
}
