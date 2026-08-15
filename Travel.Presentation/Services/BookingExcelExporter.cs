using ClosedXML.Excel;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Presentation.Helpers;

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

        /// <param name="includePricing">
        /// Bookings.ViewPricing. False leaves the sheet exactly as it was before pricing existed, so the
        /// same Export button hands a priced file to a sales agent and an unpriced one to everyone else.
        /// </param>
        public byte[] Build(Booking booking, bool includePricing = false)
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
            ExcelFormat.WriteField(sheet, row++, "Nationality", booking.Nationality?.Name ?? "—", LastColumn);
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
            row = WriteSchedule(sheet, row, booking);

            if (includePricing)
            {
                row += 2;
                WritePricing(sheet, row, booking);
            }

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

        private static int WriteSchedule(IXLWorksheet sheet, int row, Booking booking)
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

            return row;
        }

        /// <summary>
        /// The sale price block. Money goes in as real numbers with a display format applied — never as
        /// pre-formatted text — so the recipient can sum the column.
        /// </summary>
        private static void WritePricing(IXLWorksheet sheet, int row, Booking booking)
        {
            var heading = sheet.Cell(row, 1);
            heading.Value = "PRICING";
            heading.Style.Font.Bold = true;
            row++;

            if (!booking.IsPriced)
            {
                sheet.Cell(row, 1).Value = "Not priced yet.";
                sheet.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
                return;
            }

            ExcelFormat.WriteHeaderRow(sheet, row++, "Type", "Count", $"Price ({Money.Code})", $"Total ({Money.Code})");

            foreach (var (label, count, price) in new[]
            {
                ("Adults", booking.Adults, booking.AdultPrice),
                ("Children", booking.Children, booking.ChildPrice),
                ("Infants", booking.Infants, booking.InfantPrice),
            })
            {
                if (count == 0)
                    continue;

                sheet.Cell(row, 1).Value = label;
                sheet.Cell(row, 2).Value = count;
                WriteMoney(sheet.Cell(row, 3), price ?? 0m);
                WriteMoney(sheet.Cell(row, 4), count * (price ?? 0m));
                row++;
            }

            // The other two components of the total. A sheet showing only the pax lines but a grand
            // total that includes these would look like an arithmetic error to whoever receives it.
            foreach (var stay in booking.Stays.Where(s => s.ExtraNights > 0).OrderBy(s => s.SortOrder))
            {
                sheet.Cell(row, 1).Value = $"Extra nights — {stay.Destination?.Name ?? "—"}";
                sheet.Cell(row, 2).Value = stay.ExtraNights;
                WriteMoney(sheet.Cell(row, 3), stay.ExtraNightPrice ?? 0m);
                WriteMoney(sheet.Cell(row, 4), stay.ExtraNightsTotal);
                row++;
            }

            foreach (var tour in booking.Tours.OrderBy(t => t.SortOrder))
            {
                sheet.Cell(row, 1).Value = $"Tour — {tour.Tour?.Name ?? "—"}";
                sheet.Cell(row, 2).Value = tour.Pax;
                WriteMoney(sheet.Cell(row, 3), tour.UnitPrice);
                WriteMoney(sheet.Cell(row, 4), tour.Total);
                row++;
            }

            sheet.Range(row, 1, row, 4).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "TOTAL";
            WriteMoney(sheet.Cell(row, 4), booking.GrandTotal ?? 0m);
            sheet.Range(row, 1, row, 4).Style.Font.Bold = true;
            row += 2;

            if (booking.Transactions.Count == 0)
                return;

            var paidHeading = sheet.Cell(row, 1);
            paidHeading.Value = "PAYMENTS";
            paidHeading.Style.Font.Bold = true;
            row++;

            ExcelFormat.WriteHeaderRow(sheet, row++, "Date", "Type", "Method", $"Amount ({Money.Code})");

            foreach (var t in booking.Transactions.OrderBy(t => t.PaidOn).ThenBy(t => t.Id))
            {
                sheet.Cell(row, 1).Value = t.PaidOn.ToString("dd MMM yyyy");
                sheet.Cell(row, 2).Value = ExcelFormat.TransactionTypeText(t.Type);
                sheet.Cell(row, 3).Value = ExcelFormat.PaymentMethodText(t.Method);
                WriteMoney(sheet.Cell(row, 4), t.SignedAmount);
                row++;
            }

            sheet.Range(row, 1, row, 4).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            sheet.Cell(row, 1).Value = "PAID";
            WriteMoney(sheet.Cell(row, 4), booking.PaidTotal);
            sheet.Range(row, 1, row, 4).Style.Font.Bold = true;
            row++;

            sheet.Cell(row, 1).Value = "BALANCE";
            WriteMoney(sheet.Cell(row, 4), booking.Balance ?? 0m);
            sheet.Range(row, 1, row, 4).Style.Font.Bold = true;
        }

        private static void WriteMoney(IXLCell cell, decimal amount)
        {
            cell.Value = amount;
            cell.Style.NumberFormat.Format = Money.ExcelNumberFormat;
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
