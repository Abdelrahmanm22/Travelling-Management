using ClosedXML.Excel;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// Renders the Flights report as a real .xlsx: one row per passenger, with the booking's arrival
    /// and departure flight facts repeated on each of its guests, under a two-row
    /// ARRIVAL / DEPARTURE grouped header.
    /// </summary>
    public class FlightReportExcelExporter
    {
        public const string ContentType = ExcelFormat.ContentType;

        private const int LastColumn = 8;
        private const int HeaderRow = 2;
        private const int FirstDataRow = 3;

        /// <summary>Excel's default row height — one stacked guest name per multiple of it.</summary>
        private const double LineHeight = 15d;

        // The report's own palette. Deliberately local: no other sheet uses these, and
        // ExcelFormat.HeaderFill stays the shared grey default.
        private static readonly XLColor KeyFill = XLColor.FromHtml("#DDEBF7");
        private static readonly XLColor ArrivalFill = XLColor.FromHtml("#E2EFDA");
        private static readonly XLColor DepartureFill = XLColor.FromHtml("#FCE4D6");

        public byte[] Build(IReadOnlyList<FlightReportRow> rows)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Flights");

            WriteHeader(sheet);

            var row = FirstDataRow;
            foreach (var line in rows)
            {
                var code = sheet.Cell(row, 1);
                code.Value = line.BookingCode;
                code.Style.Font.Bold = true;

                // Every guest of the booking in ONE cell, one per line.
                var name = sheet.Cell(row, 2);
                name.Value = line.PaxNames.Count == 0 ? "—" : string.Join("\n", line.PaxNames);
                name.Style.Alignment.WrapText = true;

                sheet.Cell(row, 3).Value = line.ArrivalDate.ToString("dd-MMM");
                sheet.Cell(row, 4).Value = Time(line.ArrivalTime);
                sheet.Cell(row, 5).Value = FlightNo(line.ArrivalFlightNo);

                sheet.Cell(row, 6).Value = line.DepartureDate.ToString("dd-MMM");
                sheet.Cell(row, 7).Value = Time(line.DepartureTime);
                sheet.Cell(row, 8).Value = FlightNo(line.DepartureFlightNo);

                sheet.Range(row, 3, row, LastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                sheet.Range(row, 1, row, LastColumn).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // Wrapped text is clipped to the default row height unless the height is set — Excel
                // only auto-fits a wrapped cell when the user touches the row.
                sheet.Row(row).Height = LineHeight * Math.Max(line.PaxNames.Count, 1);
                row++;
            }

            var lastRow = Math.Max(row - 1, HeaderRow);
            sheet.Range(1, 1, lastRow, LastColumn).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sheet.Range(1, 1, lastRow, LastColumn).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            sheet.SheetView.FreezeRows(HeaderRow);
            sheet.Columns(1, LastColumn).AdjustToContents();
            // AdjustToContents ignores wrapping, so a long name would otherwise stretch column B off the page.
            sheet.Column(2).Width = 34;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void WriteHeader(IXLWorksheet sheet)
        {
            // Booking No / Pax Names span both header rows; ARRIVAL and DEPARTURE sit above their trio.
            foreach (var (column, text) in new[] { (1, "Booking No"), (2, "Pax Names") })
            {
                var cell = sheet.Range(1, column, HeaderRow, column).Merge().FirstCell();
                cell.Value = text;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = KeyFill;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            ExcelFormat.WriteGroupHeader(sheet, 1, 3, 3, "ARRIVAL", ArrivalFill);
            ExcelFormat.WriteGroupHeader(sheet, 1, 6, 3, "DEPARTURE", DepartureFill);

            ExcelFormat.WriteHeaderCells(sheet, HeaderRow, 3, ArrivalFill, "Flight Date", "Arrival Time", "Flight Number");
            ExcelFormat.WriteHeaderCells(sheet, HeaderRow, 6, DepartureFill, "Flight Date", "Departure Time", "Flight Number");
        }

        private static string Time(TimeOnly? time) => time is { } t ? t.ToString("HH\\:mm") : "—";

        private static string FlightNo(string? flightNo) =>
            string.IsNullOrWhiteSpace(flightNo) ? "—" : flightNo.Trim();

        public static string FileName(DateOnly? from, DateOnly? to) => (from, to) switch
        {
            ({ } f, { } t) => $"Flights_{f:yyyy-MM-dd}_{t:yyyy-MM-dd}.xlsx",
            ({ } f, null) => $"Flights_from-{f:yyyy-MM-dd}.xlsx",
            (null, { } t) => $"Flights_until-{t:yyyy-MM-dd}.xlsx",
            _ => "Flights_All.xlsx"
        };
    }
}
