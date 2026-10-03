using ClosedXML.Excel;
using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// Renders an arrival file as a real .xlsx workbook — the document an operator sends to the hotel
    /// or cruise line. The layout is identical whether the file covers one booking or twelve; only
    /// the wording and the filename say which. Lives in the web project so the ClosedXML dependency
    /// stays out of Travel.Services, which is deliberately NuGet-free.
    /// </summary>
    public class BlockExcelExporter
    {
        public const string ContentType = ExcelFormat.ContentType;

        private static readonly string[] Headers =
        {
            "Code", "Guest", "Type", "Age", "Rooms", "Nights", "Check-out",
            "Hotel", "Category", "Package", "Arr. flight", "Dep. flight", "Notes"
        };

        private const int LastColumn = 13;

        public byte[] Build(BlockFile file)
        {
            var summary = file.Summary;

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(ExcelFormat.SheetName(summary.DestinationName, Label(summary)));

            var title = sheet.Range(1, 1, 1, LastColumn).Merge().FirstCell();
            // The grade is part of what the hotel is being asked to block — and the other grade's file
            // for the same date looks identical otherwise.
            // "PRIVATE ARRIVAL" is for the supplier too: it must not fold this booking into the shared
            // block for the same destination and date.
            title.Value = $"{summary.DestinationName.ToUpperInvariant()} · {ExcelFormat.CategoryText(summary.Category).ToUpperInvariant()}"
                        + (summary.IsPrivate ? " — PRIVATE ARRIVAL" : "")
                        + $" — CHECK-IN {summary.CheckInDate:dd MMM yyyy}";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;

            var subtitle = sheet.Range(2, 1, 2, LastColumn).Merge().FirstCell();
            subtitle.Value = $"{BookingsText(summary.BookingCount)} · {summary.TotalPax} pax · {ExcelFormat.FormatRooms(summary.Rooms)}";
            subtitle.Style.Font.FontColor = XLColor.Gray;

            // Where this file stands with the supplier. The two email links are deliberately NOT
            // exported: this workbook is sent to the supplier, and they are internal references to
            // the operator's own mailbox — useless to the recipient and not theirs to see.
            ExcelFormat.WriteField(sheet, 3, "Status", ExcelFormat.BlockStatusText(summary.Status.Status), LastColumn);

            const int headerRow = 5;
            ExcelFormat.WriteHeaderRow(sheet, headerRow, Headers);

            var row = headerRow + 1;
            foreach (var booking in file.Bookings)
            {
                var lead = booking.Guests.FirstOrDefault(g => g.IsLeadGuest) ?? booking.Guests.FirstOrDefault();

                // A thin top border separates one booking's rows from the previous booking's.
                sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Thin;

                sheet.Cell(row, 1).Value = booking.Code;
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 2).Value = lead?.FullName ?? booking.LeadGuestName;
                sheet.Cell(row, 3).Value = ExcelFormat.GuestTypeText(lead?.Type ?? GuestType.Adult);
                if (lead?.Age is { } leadAge) sheet.Cell(row, 4).Value = leadAge;
                sheet.Cell(row, 5).Value = ExcelFormat.FormatRooms(booking.Rooms);
                sheet.Cell(row, 6).Value = booking.Nights;
                sheet.Cell(row, 7).Value = booking.CheckOutDate.ToString("dd MMM yyyy");
                sheet.Cell(row, 8).Value = booking.HotelName ?? "—";
                sheet.Cell(row, 9).Value = ExcelFormat.CategoryText(booking.Category);
                sheet.Cell(row, 10).Value = booking.PackageName;
                sheet.Cell(row, 11).Value = ExcelFormat.FormatFlight(booking.ArrivalFlightNo, booking.FlightArrivalDate, booking.ArrivalDate, booking.ArrivalTime);
                sheet.Cell(row, 12).Value = ExcelFormat.FormatFlight(booking.DepartureFlightNo, booking.FlightDepartureDate, booking.DepartureDate, booking.DepartureTime);
                sheet.Cell(row, 13).Value = booking.StayNotes ?? string.Empty;
                row++;

                // Remaining guests as indented sub-rows — the rooming list the hotel actually checks in.
                foreach (var guest in booking.Guests.Where(g => g != lead))
                {
                    var name = sheet.Cell(row, 2);
                    name.Value = guest.FullName;
                    name.Style.Alignment.Indent = 2;
                    name.Style.Font.FontColor = ExcelFormat.MutedText;

                    sheet.Cell(row, 3).Value = ExcelFormat.GuestTypeText(guest.Type);
                    if (guest.Age is { } age) sheet.Cell(row, 4).Value = age;
                    row++;
                }
            }

            sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "TOTAL";
            sheet.Cell(row, 2).Value = BookingsText(summary.BookingCount);
            sheet.Cell(row, 3).Value = $"{summary.TotalPax} pax";
            sheet.Cell(row, 5).Value = ExcelFormat.FormatRooms(summary.Rooms);
            sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;

            sheet.SheetView.FreezeRows(headerRow);
            sheet.Columns(1, LastColumn).AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public static string FileName(BlockSummary summary)
        {
            var label = Label(summary);
            // The category suffix keeps the 4★ and 5★ files of one date from colliding in a download folder.
            var category = summary.Category == PackageCategory.FiveStars ? "5Stars" : "4Stars";
            // A private file also names its booking, so two private arrivals on one date can't collide.
            var code = summary.IsPrivate ? $"_{ExcelFormat.SanitizeFileName(summary.PrivateBookingCode ?? "", "")}" : "";
            return $"{label}_{ExcelFormat.SanitizeFileName(summary.DestinationName, label)}_{summary.CheckInDate:yyyy-MM-dd}_{category}{code}.xlsx";
        }

        /// <summary>
        /// A one-booking file isn't a block — don't send the supplier a sheet that claims it is. A private
        /// booking's file says so outright.
        /// </summary>
        private static string Label(BlockSummary summary) =>
            summary.IsPrivate ? "Private" : summary.BookingCount == 1 ? "Arrival" : "Block";

        private static string BookingsText(int count) => $"{count} booking{(count == 1 ? "" : "s")}";
    }
}
