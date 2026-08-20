using ClosedXML.Excel;
using Travel.Presentation.Helpers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// One safe's movements as a real .xlsx, modelled on <see cref="TransactionExcelExporter"/>.
    ///
    /// <para>
    /// Amounts are <b>signed numbers</b> with a display format, never pre-formatted text, so the column
    /// genuinely sums to the closing balance in the footer. The currency lives in the header rather
    /// than in each cell for the same reason.
    /// </para>
    /// </summary>
    public class SafeLedgerExcelExporter
    {
        private const int LastColumn = 7;

        public byte[] Build(SafeLedgerViewModel model)
        {
            var currency = Money.Normalize(model.Safe.Currency);

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(ExcelFormat.SheetName(model.Safe.Name, "Safe"));

            var title = sheet.Range(1, 1, 1, LastColumn).Merge().FirstCell();
            title.Value = $"{model.Safe.Name.Trim().ToUpperInvariant()} · {currency}";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var subtitle = sheet.Range(2, 1, 2, LastColumn).Merge().FirstCell();
            subtitle.Value = RangeText(model);
            subtitle.Style.Font.FontColor = XLColor.Gray;
            subtitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = 4;
            ExcelFormat.WriteHeaderRow(sheet, row++,
                "Date", "Movement", "Description", "Expense", "Reference", "Logged by", $"Amount ({currency})");

            sheet.SheetView.FreezeRows(row - 1);

            foreach (var entry in model.Entries.OrderBy(e => e.OccurredOn).ThenBy(e => e.Id))
            {
                sheet.Cell(row, 1).Value = entry.OccurredOn.ToString("dd MMM yyyy");
                sheet.Cell(row, 2).Value = ExcelFormat.SafeEntryTypeText(entry.Type);
                sheet.Cell(row, 3).Value = string.IsNullOrWhiteSpace(entry.Description) ? "—" : entry.Description;
                sheet.Cell(row, 4).Value = entry.Expense?.ExpenseTypeName ?? "—";
                sheet.Cell(row, 5).Value = string.IsNullOrWhiteSpace(entry.Reference) ? "—" : entry.Reference;
                sheet.Cell(row, 6).Value = entry.CreatedByName ?? "—";

                var amount = sheet.Cell(row, LastColumn);
                amount.Value = entry.SignedAmount;
                amount.Style.NumberFormat.Format = Money.ExcelNumberFormat;
                row++;
            }

            // The filtered net, then the safe's actual balance — a date filter must never read as if
            // money had left the safe, so both figures are written and labelled.
            sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "NET (filtered)";
            var net = sheet.Cell(row, LastColumn);
            net.Value = model.FilteredNet;
            net.Style.NumberFormat.Format = Money.ExcelNumberFormat;
            sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;
            row++;

            sheet.Cell(row, 1).Value = "BALANCE (all time)";
            var balance = sheet.Cell(row, LastColumn);
            balance.Value = model.Balance;
            balance.Style.NumberFormat.Format = Money.ExcelNumberFormat;
            sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;

            sheet.Columns(1, LastColumn).AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static string RangeText(SafeLedgerViewModel model)
        {
            var from = model.From?.ToString("dd MMM yyyy") ?? "the beginning";
            var to = model.To?.ToString("dd MMM yyyy") ?? "today";
            var count = model.Entries.Count;
            return $"{from} → {to} · {count} movement{(count == 1 ? "" : "s")}";
        }

        public static string FileName(SafeLedgerViewModel model) =>
            $"Safe_{ExcelFormat.SanitizeFileName(model.Safe.Name, "safe")}"
            + $"_{model.From?.ToString("yyyy-MM-dd") ?? "all"}"
            + $"_{model.To?.ToString("yyyy-MM-dd") ?? "all"}.xlsx";
    }
}
