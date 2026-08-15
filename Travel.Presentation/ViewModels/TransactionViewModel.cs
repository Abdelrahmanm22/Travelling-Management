using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Presentation.ViewModels
{
    /// <summary>Add or edit one ledger entry. The same shape serves both, like the CRUD form partials.</summary>
    public class TransactionFormViewModel
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        /// <summary>Display only — the booking can't be changed once an entry belongs to it.</summary>
        public string BookingCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type is required.")]
        [Display(Name = "Type")]
        public TransactionType Type { get; set; } = TransactionType.Payment;

        [Required(ErrorMessage = "Method is required.")]
        [Display(Name = "Method")]
        public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

        // Always positive — Type carries the sign. A negative payment and a refund would be two ways
        // to say the same thing, and the ledger would stop adding up in an obvious way.
        [Required(ErrorMessage = "Amount is required.")]
        [Range(0.01, 9_999_999, ErrorMessage = "Amount must be greater than zero.")]
        [Display(Name = "Amount")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Payment date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Paid on")]
        public DateOnly PaidOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [StringLength(100)]
        [Display(Name = "Reference")]
        public string? Reference { get; set; }

        [StringLength(1000)]
        [Display(Name = "Notes")]
        public string? Notes { get; set; }

        public List<IFormFile> ReceiptFiles { get; set; } = new();

        /// <summary>Checkbox list of attachment ids to drop — never use asp-for on it (see the ViewModels note).</summary>
        public List<int> RemoveAttachmentIds { get; set; } = new();

        public IReadOnlyList<TransactionAttachment> ExistingAttachments { get; set; } = new List<TransactionAttachment>();
    }

    /// <summary>Filter state plus results for the global /Transaction list.</summary>
    public class TransactionListViewModel
    {
        [Display(Name = "From")]
        [DataType(DataType.Date)]
        public DateOnly? From { get; set; }

        [Display(Name = "To")]
        [DataType(DataType.Date)]
        public DateOnly? To { get; set; }

        [Display(Name = "Booking")]
        public int? BookingId { get; set; }

        [Display(Name = "Method")]
        public PaymentMethod? Method { get; set; }

        [Display(Name = "Type")]
        public TransactionType? Type { get; set; }

        /// <summary>
        /// The applied idiom, as in BlockServiceController/ReportController: From defaults to a month
        /// back only on a first visit, so a deliberately cleared box isn't silently refilled.
        /// </summary>
        public bool Applied { get; set; }

        public IEnumerable<SelectListItem> BookingOptions { get; set; } = Enumerable.Empty<SelectListItem>();

        public IReadOnlyList<BookingTransaction> Transactions { get; set; } = new List<BookingTransaction>();

        public decimal Received => Transactions.Where(t => t.Type == TransactionType.Payment).Sum(t => t.Amount);
        public decimal Refunded => Transactions.Where(t => t.Type == TransactionType.Refund).Sum(t => t.Amount);
        public decimal Net => Received - Refunded;
    }
}
