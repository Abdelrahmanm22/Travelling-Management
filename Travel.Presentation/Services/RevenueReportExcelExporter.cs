using ClosedXML.Excel;
using Travel.Presentation.Helpers;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// The Revenue report as a real .xlsx.
    ///
    /// <para>
    /// Money goes in as <b>numbers</b> with a display format and margin as a real <b>percentage</b>
    /// cell — never pre-formatted text — so whoever receives it can sum the columns and re-sort them.
    /// An unpriced booking writes no number at all rather than a 0, keeping "not priced yet" distinct
    /// from "free" in the sheet exactly as it is on screen.
    /// </para>
    /// </summary>
    public class RevenueReportExcelExporter
    {
        private const int LastColumn = 9;

        private static readonly XLColor MoneyFill = XLColor.FromHtml("#EAF3EA");

        public byte[] Build(RevenueReportViewModel model)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Revenue");

            var title = sheet.Range(1, 1, 1, LastColumn).Merge().FirstCell();
            title.Value = "REVENUE";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;
            title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var subtitle = sheet.Range(2, 1, 2, LastColumn).Merge().FirstCell();
            subtitle.Value = RangeText(model);
            subtitle.Style.Font.FontColor = XLColor.Gray;
            subtitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = 4;
            ExcelFormat.WriteHeaderRow(sheet, row++,
                "Booking No", "Lead Guest", "Arrival", "Package", "PAX",
                $"Sale ({Money.Code})", $"Expenses ({Money.Code})", $"Net ({Money.Code})", "Margin %");

            sheet.SheetView.FreezeRows(row - 1);

            foreach (var line in model.Rows)
            {
                sheet.Cell(row, 1).Value = line.BookingCode;
                sheet.Cell(row, 2).Value = line.LeadGuestName;
                sheet.Cell(row, 3).Value = line.ArrivalDate.ToString("dd MMM yyyy");
                sheet.Cell(row, 4).Value = line.PackageName;
                sheet.Cell(row, 5).Value = line.Pax;

                // An unpriced booking leaves Sale/Net/Margin genuinely empty. ClosedXML's XLCellValue
                // has no common type with decimal, so the cell is simply left untouched.
                if (line.SalePrice is { } sale)
                    Money2(sheet.Cell(row, 6), sale);

                Money2(sheet.Cell(row, 7), line.ExpensesBase);

                if (line.NetRevenue is { } net)
                    Money2(sheet.Cell(row, 8), net);

                if (line.MarginPercent is { } margin)
                {
                    var cell = sheet.Cell(row, 9);
                    cell.Value = margin / 100m;             // a real percentage, not the number 42.1
                    cell.Style.NumberFormat.Format = "0.0%";
                }

                // Marked red on the row, and explained in full under the totals. A cell comment would
                // be tidier but ClosedXML 0.105 has no such API here, and a note nobody can see beats
                // one that doesn't compile.
                if (line.HasUnratedExpense)
                    sheet.Cell(row, 7).Style.Font.FontColor = XLColor.Red;

                row++;
            }

            sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "TOTAL";
            sheet.Cell(row, 5).Value = model.PaxCount;
            Money2(sheet.Cell(row, 6), model.TotalSale);
            Money2(sheet.Cell(row, 7), model.TotalExpenses);
            Money2(sheet.Cell(row, 8), model.TotalNet);

            if (model.TotalMarginPercent is { } totalMargin)
            {
                var cell = sheet.Cell(row, 9);
                cell.Value = totalMargin / 100m;
                cell.Style.NumberFormat.Format = "0.0%";
            }

            sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;
            sheet.Range(row, 6, row, LastColumn).Style.Fill.BackgroundColor = MoneyFill;
            row++;

            // Anything the totals deliberately left out is stated in the sheet, not just on screen —
            // a figure that silently omits rows is worse than one that explains itself.
            if (model.UnpricedCount > 0)
            {
                sheet.Cell(row, 1).Value =
                    $"{model.UnpricedCount} booking(s) have no price set. Their costs are counted but their revenue "
                    + "cannot be, so net and margin above are lower than reality.";
                sheet.Range(row, 1, row, LastColumn).Merge().FirstCell().Style.Font.FontColor = ExcelFormat.MutedText;
                row++;
            }

            if (model.UnratedExpenseCount > 0)
            {
                sheet.Cell(row, 1).Value =
                    $"{model.UnratedExpenseCount} cost(s) excluded because their expense has no exchange rate recorded.";
                sheet.Range(row, 1, row, LastColumn).Merge().FirstCell().Style.Font.FontColor = XLColor.Red;
            }

            sheet.Columns(1, LastColumn).AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void Money2(IXLCell cell, decimal amount)
        {
            cell.Value = amount;
            cell.Style.NumberFormat.Format = Money.ExcelNumberFormat;
        }

        private static string RangeText(RevenueReportViewModel model)
        {
            var from = model.From?.ToString("dd MMM yyyy") ?? "the beginning";
            var to = model.To?.ToString("dd MMM yyyy") ?? "today";
            var count = model.Rows.Count;
            return $"Arrivals {from} → {to} · {count} booking{(count == 1 ? "" : "s")} · {model.PaxCount} pax";
        }

        public static string FileName(DateOnly? from, DateOnly? to) =>
            $"Revenue_{from?.ToString("yyyy-MM-dd") ?? "all"}_{to?.ToString("yyyy-MM-dd") ?? "all"}.xlsx";
    }
}
