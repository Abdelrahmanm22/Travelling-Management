using ClosedXML.Excel;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// Renders the Tours report as a real .xlsx: one row per guest, the booking's facts merged down its
    /// guest rows, and one column per tour with a ✓ for every guest taking it.
    /// </summary>
    public class ToursReportExcelExporter
    {
        private const int HeaderRow = 1;
        private const int FirstDataRow = 2;

        /// <summary>Booking No, Pax Name, Arrival, Departure, Package — the tours start after these.</summary>
        private const int FixedColumns = 5;

        private const string Check = "✓";

        // Deliberately local, like FlightReportExcelExporter's palette: no other sheet uses it.
        private static readonly XLColor UnrecordedFill = XLColor.FromHtml("#FFF2CC");

        public byte[] Build(ToursReportViewModel model)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Tours");

            var lastColumn = FixedColumns + model.Tours.Count;
            ExcelFormat.WriteHeaderRow(sheet, HeaderRow,
                new[] { "Booking No", "Pax Name", "Arrival", "Departure", "Package" }
                    .Concat(model.Tours.Select(t => t.Name))
                    .ToArray());
            sheet.Range(HeaderRow, FixedColumns + 1, HeaderRow, Math.Max(lastColumn, FixedColumns + 1))
                 .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = FirstDataRow;
            foreach (var booking in model.Bookings)
            {
                var first = row;
                var last = row + booking.Guests.Count - 1;

                MergeDown(sheet, first, last, 1, booking.BookingCode).Style.Font.Bold = true;
                MergeDown(sheet, first, last, 3, booking.ArrivalDate.ToString("dd-MMM"));
                MergeDown(sheet, first, last, 4, booking.DepartureDate.ToString("dd-MMM"));
                MergeDown(sheet, first, last, 5, booking.PackageName);

                foreach (var guest in booking.Guests)
                {
                    sheet.Cell(row, 2).Value = guest.Name;

                    for (var i = 0; i < model.Tours.Count; i++)
                    {
                        var tourId = model.Tours[i].TourId;
                        var cell = sheet.Cell(row, FixedColumns + 1 + i);

                        if (guest.TourIds.Contains(tourId))
                            cell.Value = Check;

                        // Unnamed pax can't be pinned to a guest, so it is stated once, on the booking's first row.
                        if (row == first && booking.UnnamedPax.TryGetValue(tourId, out var pax))
                        {
                            cell.Value = cell.GetString() == Check
                                ? $"{Check} + {pax} pax — names not recorded"
                                : $"{pax} pax — names not recorded";
                            cell.Style.Fill.BackgroundColor = UnrecordedFill;
                        }
                    }

                    row++;
                }
            }

            var lastRow = Math.Max(row - 1, HeaderRow);
            var table = sheet.Range(HeaderRow, 1, lastRow, lastColumn);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Range(FirstDataRow, 3, lastRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (model.Tours.Count > 0)
                sheet.Range(FirstDataRow, FixedColumns + 1, lastRow, lastColumn)
                     .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            if (model.UnrecordedTourCount > 0)
            {
                var note = sheet.Cell(lastRow + 2, 1);
                note.Value = $"Note: {model.UnrecordedTourCount} tour(s) were sold before guest names were recorded — "
                           + "their pax count is shown on the booking's first row. Open the booking and tick who takes them.";
                note.Style.Font.Italic = true;
                note.Style.Font.FontColor = ExcelFormat.MutedText;
            }

            sheet.SheetView.Freeze(HeaderRow, 2);
            sheet.Columns(1, lastColumn).AdjustToContents();
            // A long name or package would otherwise stretch its column off the page.
            sheet.Column(2).Width = Math.Min(sheet.Column(2).Width, 34);
            sheet.Column(5).Width = Math.Min(sheet.Column(5).Width, 30);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static IXLCell MergeDown(IXLWorksheet sheet, int firstRow, int lastRow, int column, string value)
        {
            var cell = lastRow > firstRow
                ? sheet.Range(firstRow, column, lastRow, column).Merge().FirstCell()
                : sheet.Cell(firstRow, column);
            cell.Value = value;
            return cell;
        }

        public static string FileName(DateOnly? from, DateOnly? to) => (from, to) switch
        {
            ({ } f, { } t) => $"Tours_{f:yyyy-MM-dd}_{t:yyyy-MM-dd}.xlsx",
            ({ } f, null) => $"Tours_from-{f:yyyy-MM-dd}.xlsx",
            (null, { } t) => $"Tours_until-{t:yyyy-MM-dd}.xlsx",
            _ => "Tours_All.xlsx"
        };
    }
}
