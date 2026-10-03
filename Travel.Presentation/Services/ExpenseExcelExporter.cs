using ClosedXML.Excel;
using Travel.Presentation.Helpers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// The block expenses list as a real .xlsx — one row per expense, with its arrival groups indented
    /// beneath it (the guest-sub-row idiom <see cref="BlockExcelExporter"/> already uses).
    ///
    /// <para>
    /// There is no grand total row. Expenses can be paid from safes in different currencies, and a
    /// single summed figure across them would be meaningless — the totals are written per currency
    /// instead.
    /// </para>
    /// </summary>
    public class ExpenseExcelExporter
    {
        private const int LastColumn = 7;

        public byte[] Build(ExpenseListViewModel model)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Expenses");

            var title = sheet.Range(1, 1, 1, LastColumn).Merge().FirstCell();
            title.Value = "BLOCK EXPENSES";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var subtitle = sheet.Range(2, 1, 2, LastColumn).Merge().FirstCell();
            subtitle.Value = RangeText(model);
            subtitle.Style.Font.FontColor = XLColor.Gray;
            subtitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = 4;
            ExcelFormat.WriteHeaderRow(sheet, row++,
                "Date", "Expense type", "Basis", "Safe", "Currency", "Arrival group", "Amount");

            sheet.SheetView.FreezeRows(row - 1);

            foreach (var card in model.Expenses)
            {
                var expense = card.Expense;

                sheet.Cell(row, 1).Value = expense.SpentOn.ToString("dd MMM yyyy");
                sheet.Cell(row, 2).Value = expense.ExpenseTypeName;
                sheet.Cell(row, 3).Value = ExcelFormat.ExpenseBasisText(expense.Basis);
                sheet.Cell(row, 4).Value = card.SafeName;
                sheet.Cell(row, 5).Value = card.Currency;
                sheet.Cell(row, 6).Value = $"{expense.Groups.Count} group{(expense.Groups.Count == 1 ? "" : "s")}";
                sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;

                var total = sheet.Cell(row, LastColumn);
                total.Value = expense.TotalAmount;
                total.Style.NumberFormat.Format = Money.ExcelNumberFormat;
                row++;

                foreach (var group in expense.Groups.OrderBy(g => g.CheckInDate).ThenBy(g => g.DestinationName).ThenBy(g => g.Category))
                {
                    var label = sheet.Cell(row, 6);
                    var privateText = group.PrivateBookingId.HasValue
                        ? $" · Private {group.Bookings.FirstOrDefault()?.BookingCode}"
                        : "";
                    label.Value = $"{group.DestinationName} · {ExcelFormat.CategoryText(group.Category)}{privateText} · {group.CheckInDate:dd MMM yyyy}"
                                + $" · {group.BookingCount} booking{(group.BookingCount == 1 ? "" : "s")}";
                    label.Style.Alignment.Indent = 1;
                    label.Style.Font.FontColor = ExcelFormat.MutedText;

                    var amount = sheet.Cell(row, LastColumn);
                    amount.Value = group.Amount;
                    amount.Style.NumberFormat.Format = Money.ExcelNumberFormat;
                    amount.Style.Font.FontColor = ExcelFormat.MutedText;
                    row++;
                }
            }

            row++;
            foreach (var (currency, total) in model.TotalsByCurrency)
            {
                sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                sheet.Cell(row, 1).Value = $"TOTAL ({currency})";
                var cell = sheet.Cell(row, LastColumn);
                cell.Value = total;
                cell.Style.NumberFormat.Format = Money.ExcelNumberFormat;
                sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;
                row++;
            }

            sheet.Columns(1, LastColumn).AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static string RangeText(ExpenseListViewModel model)
        {
            var from = model.From?.ToString("dd MMM yyyy") ?? "the beginning";
            var to = model.To?.ToString("dd MMM yyyy") ?? "today";
            var count = model.Expenses.Count;
            return $"{from} → {to} · {count} expense{(count == 1 ? "" : "s")}";
        }

        public static string FileName(DateOnly? from, DateOnly? to) =>
            $"Expenses_{from?.ToString("yyyy-MM-dd") ?? "all"}_{to?.ToString("yyyy-MM-dd") ?? "all"}.xlsx";
    }
}
