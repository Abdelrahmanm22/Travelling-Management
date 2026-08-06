using ClosedXML.Excel;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// Renders one booking as a real .xlsx workbook, laid out like the Details page:
    /// booking facts, then the guest roster, rooming and the schedule.
    /// </summary>
    public class BookingExcelExporter
    {
        public const string ContentType = ExcelFormat.ContentType;

        private const int LastColumn = 7;

        public byte[] Build(Booking booking)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(ExcelFormat.SheetName(booking.Code, "Booking"));

            var title = sheet.Range(1, 1, 1, LastColumn).Merge().FirstCell();
            title.Value = $"{booking.Code} — {booking.LeadGuestName}";
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 14;

            var rooms = TallyRooms(booking);
            var subtitle = sheet.Range(2, 1, 2, LastColumn).Merge().FirstCell();
            subtitle.Value = $"{booking.Package?.Name ?? "—"} · {ExcelFormat.CategoryText(booking.Category)} · " +
                             $"{booking.Pax} pax · {ExcelFormat.FormatRooms(rooms)}";
            subtitle.Style.Font.FontColor = XLColor.Gray;

            var row = 4;
            ExcelFormat.WriteField(sheet, row++, "Status", ExcelFormat.BookingStatusText(booking.Status), LastColumn);
            // The package start/end; the flight rows below carry the actual flight dates.
            ExcelFormat.WriteField(sheet, row++, "Arrival", booking.ArrivalDate.ToString("dd MMM yyyy"), LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Departure", booking.DepartureDate.ToString("dd MMM yyyy"), LastColumn);
            ExcelFormat.WriteField(sheet, row++, "PAX", PaxBreakdown(booking), LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Rooms", ExcelFormat.FormatRooms(rooms), LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Email", booking.GuestEmail, LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Phone", booking.GuestPhone, LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Nationality", booking.Nationality, LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Arrival flight",
                ExcelFormat.FormatFlight(booking.ArrivalFlightNo, booking.FlightArrivalDate, booking.ArrivalDate, booking.ArrivalTime), LastColumn);
            ExcelFormat.WriteField(sheet, row++, "Departure flight",
                ExcelFormat.FormatFlight(booking.DepartureFlightNo, booking.FlightDepartureDate, booking.DepartureDate, booking.DepartureTime), LastColumn);

            if (booking.ExtraNights > 0)
            {
                ExcelFormat.WriteField(sheet, row++, "Extra nights",
                    $"+{booking.ExtraNights} — {booking.ExtensionReason}".TrimEnd(' ', '—'), LastColumn);
            }

            if (!string.IsNullOrWhiteSpace(booking.Notes))
                ExcelFormat.WriteField(sheet, row++, "Notes", booking.Notes, LastColumn);

            row++;
            row = WriteGuests(sheet, row, booking);

            row++;
            WriteSchedule(sheet, row, booking);

            sheet.Columns(1, LastColumn).AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static int WriteGuests(IXLWorksheet sheet, int row, Booking booking)
        {
            var heading = sheet.Cell(row, 1);
            heading.Value = "GUESTS";
            heading.Style.Font.Bold = true;
            row++;

            ExcelFormat.WriteHeaderRow(sheet, row++, "#", "Name", "Type", "Age", "Lead");

            var n = 1;
            foreach (var guest in booking.Guests.OrderBy(g => g.SortOrder))
            {
                sheet.Cell(row, 1).Value = n++;
                sheet.Cell(row, 2).Value = guest.FullName;
                sheet.Cell(row, 3).Value = ExcelFormat.GuestTypeText(guest.Type);
                if (guest.Age is { } age) sheet.Cell(row, 4).Value = age;
                if (guest.IsLeadGuest) sheet.Cell(row, 5).Value = "lead";
                row++;
            }

            return row;
        }

        private static void WriteSchedule(IXLWorksheet sheet, int row, Booking booking)
        {
            var heading = sheet.Cell(row, 1);
            heading.Value = "SCHEDULE";
            heading.Style.Font.Bold = true;
            row++;

            ExcelFormat.WriteHeaderRow(sheet, row++, "#", "Stay", "Check-in", "Check-out", "Nights", "Hotel", "Notes");

            var n = 1;
            foreach (var stay in booking.Stays.OrderBy(s => s.SortOrder))
            {
                sheet.Cell(row, 1).Value = n++;
                sheet.Cell(row, 2).Value = stay.Destination?.Name?.Trim() ?? "—";
                sheet.Cell(row, 3).Value = stay.CheckInDate.ToString("dd MMM yyyy");
                sheet.Cell(row, 4).Value = stay.CheckInDate.AddDays(stay.Nights).ToString("dd MMM yyyy");
                sheet.Cell(row, 5).Value = stay.Nights;
                sheet.Cell(row, 6).Value = stay.HotelName ?? "—";
                sheet.Cell(row, 7).Value = stay.Notes ?? string.Empty;
                row++;
            }

            sheet.Range(row, 1, row, LastColumn).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "TOTAL";
            sheet.Cell(row, 5).Value = booking.Stays.Sum(s => s.Nights);
            sheet.Range(row, 1, row, LastColumn).Style.Font.Bold = true;
        }

        private static string PaxBreakdown(Booking booking)
        {
            var parts = new List<string> { $"{booking.Adults} adult{(booking.Adults == 1 ? "" : "s")}" };
            if (booking.Children > 0) parts.Add($"{booking.Children} child{(booking.Children == 1 ? "" : "ren")}");
            if (booking.Infants > 0) parts.Add($"{booking.Infants} infant{(booking.Infants == 1 ? "" : "s")}");
            return $"{booking.Pax} ({string.Join(", ", parts)})";
        }

        private static IReadOnlyList<RoomTally> TallyRooms(Booking booking) =>
            booking.Rooms.GroupBy(r => r.Type)
                         .Select(g => new RoomTally(g.Key, g.Sum(r => r.Quantity)))
                         .OrderBy(t => t.Type)
                         .ToList();

        public static string FileName(Booking booking) =>
            $"Booking_{ExcelFormat.SanitizeFileName(booking.Code, "Booking")}.xlsx";
    }
}
