using System.Globalization;
using ClosedXML.Excel;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// Renders the Movements report (the drivers' sheet) as a real .xlsx: a frozen header, one merged
    /// header row per day, then that day's movements by pickup time, with the Status column flagging
    /// anything that needs review or has no flight recorded.
    /// </summary>
    public class MovementReportExcelExporter
    {
        private const int HeaderRow = 1;

        // Deliberately local: no other sheet uses this palette.
        private static readonly XLColor ReviewFill = XLColor.FromHtml("#FDE2E2");
        private static readonly XLColor WarningFill = XLColor.FromHtml("#FFF2CC");

        public byte[] Build(MovementReportViewModel model)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Movements");

            var headers = new List<string> { "Date", "Pickup time", "Booking code", "Lead guest", "Pax", "Movement" };
            if (model.ShowDestination) headers.Add("Destination");
            headers.AddRange(new[] { "Hotel", "Flight no", "Flight time", "Driver", "Status" });
            var lastColumn = headers.Count;

            ExcelFormat.WriteHeaderRow(sheet, HeaderRow, headers.ToArray());

            var row = HeaderRow + 1;
            foreach (var day in model.Days)
            {
                var header = ExcelFormat.WriteGroupHeader(sheet, row, 1, lastColumn,
                    $"{day.Date.ToString("dddd dd MMM yyyy", CultureInfo.InvariantCulture)} — {day.Rows.Count} movement{(day.Rows.Count == 1 ? "" : "s")}",
                    ExcelFormat.HeaderFill);
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                row++;

                foreach (var r in day.Rows)
                {
                    var c = 1;
                    sheet.Cell(row, c++).Value = day.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
                    sheet.Cell(row, c++).Value = r.PickupTime;
                    sheet.Cell(row, c++).Value = r.BookingCode;
                    sheet.Cell(row, c++).Value = r.LeadGuest;
                    sheet.Cell(row, c++).Value = r.Pax;
                    sheet.Cell(row, c++).Value = r.Movement;
                    if (model.ShowDestination) sheet.Cell(row, c++).Value = r.Destination;
                    sheet.Cell(row, c++).Value = r.Hotel;
                    sheet.Cell(row, c++).Value = r.FlightNo;
                    sheet.Cell(row, c++).Value = r.FlightTime;
                    sheet.Cell(row, c++).Value = r.Driver;
                    var status = sheet.Cell(row, c);
                    status.Value = r.Status;

                    if (r.NeedsReview)
                    {
                        sheet.Range(row, 1, row, lastColumn).Style.Fill.BackgroundColor = ReviewFill;
                        status.Style.Font.Bold = true;
                    }
                    else if (r.NoFlightRecorded)
                    {
                        status.Style.Fill.BackgroundColor = WarningFill;
                    }

                    row++;
                }
            }

            var lastRow = Math.Max(row - 1, HeaderRow);
            var table = sheet.Range(HeaderRow, 1, lastRow, lastColumn);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Column(5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            if (model.Days.Count == 0)
                sheet.Cell(HeaderRow + 1, 1).Value = "No movements in this range.";

            sheet.SheetView.FreezeRows(HeaderRow);
            sheet.Columns(1, lastColumn).AdjustToContents();
            // Day headers are merged across the sheet, so they mustn't drive column 1's width.
            sheet.Column(1).Width = 13;
            sheet.Column(4).Width = Math.Min(sheet.Column(4).Width, 30);
            sheet.Column(lastColumn).Width = Math.Min(Math.Max(sheet.Column(lastColumn).Width, 12), 48);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public static string FileName(string? destinationName, DateOnly? from, DateOnly? to)
        {
            var destination = ExcelFormat.SanitizeFileName(destinationName ?? "All", "All");
            return (from, to) switch
            {
                ({ } f, { } t) => $"Movements_{destination}_{f:yyyy-MM-dd}_{t:yyyy-MM-dd}.xlsx",
                ({ } f, null) => $"Movements_{destination}_from-{f:yyyy-MM-dd}.xlsx",
                (null, { } t) => $"Movements_{destination}_until-{t:yyyy-MM-dd}.xlsx",
                _ => $"Movements_{destination}_All.xlsx",
            };
        }
    }
}
