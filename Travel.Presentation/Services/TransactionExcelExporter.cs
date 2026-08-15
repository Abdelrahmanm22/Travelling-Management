using ClosedXML.Excel;
using Travel.Presentation.Helpers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// The global transactions list as a real .xlsx. Money goes in as numbers with a display format —
    /// never pre-formatted text — so whoever receives it can sum the column.
    /// </summary>
    public class TransactionExcelExporter
    {
        public const string ContentType = ExcelFormat.ContentType;

        private const int LastColumn = 7;

        public byte[] Build(TransactionListViewModel model)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Transactions");

            var title = sheet.Range(1, 1, 1, LastColumn).Merge().FirstCell();
            title.Value = "TRANSACTIONS";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var subtitle = sheet.Range(2, 1, 2, LastColumn).Merge().FirstCell();
            subtitle.Value = RangeText(model);
            subtitle.Style.Font.FontColor = XLColor.Gray;
            subtitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = 4;
            ExcelFormat.WriteHeaderRow(sheet, row++,
                "Date", "Booking", "Guest", "Type", "Method", "Reference", $"Amount ({Money.Code})");

            sheet.SheetView.FreezeRows(row - 1);

            foreach (var t in model.Transactions)
            {
                sheet.Cell(row, 1).Value = t.PaidOn.ToString("dd MMM yyyy");
                sheet.Cell(row, 2).Value = t.Booking?.Code ?? "—";
                sheet.Cell(row, 3).Value = t.Booking?.LeadGuestName ?? "—";
                sheet.Cell(row, 4).Value = ExcelFormat.TransactionTypeText(t.Type);
                sheet.Cell(row, 5).Value = ExcelFormat.PaymentMethodText(t.Method);
                sheet.Cell(row, 6).Value = t.Reference ?? "—";

                // Signed, so the column sums to the net figure in the footer.
                var amount = sheet.Cell(row, LastColumn);
                amount.Value = t.SignedAmount;
                amount.Style.NumberFormat.Format = Money.ExcelNumberFormat;
                row++;
            }

            sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "NET";
            var net = sheet.Cell(row, LastColumn);
            net.Value = model.Net;
            net.Style.NumberFormat.Format = Money.ExcelNumberFormat;
            sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;

            sheet.Columns(1, LastColumn).AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static string RangeText(TransactionListViewModel model)
        {
            var from = model.From?.ToString("dd MMM yyyy") ?? "the beginning";
            var to = model.To?.ToString("dd MMM yyyy") ?? "today";
            return $"{from} → {to} · {model.Transactions.Count} entr{(model.Transactions.Count == 1 ? "y" : "ies")}";
        }

        public static string FileName(DateOnly? from, DateOnly? to) =>
            $"Transactions_{from?.ToString("yyyy-MM-dd") ?? "all"}_{to?.ToString("yyyy-MM-dd") ?? "all"}.xlsx";
    }
}
