using ClosedXML.Excel;
using Travel.Domain.Enums;
using Travel.Domain.Services.BlockServices;

namespace Travel.Presentation.Services
{
    /// <summary>
    /// Formatting shared by every .xlsx exporter, so a booking sheet and a block file can't drift
    /// apart on how they spell a room type, a guest type or a flight.
    /// </summary>
    public static class ExcelFormat
    {
        public const string ContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public static readonly XLColor HeaderFill = XLColor.FromHtml("#EFF2F7");
        public static readonly XLColor MutedText = XLColor.FromHtml("#495057");

        public static string RoomTypeText(RoomType type) => type switch
        {
            RoomType.Single => "SGL",
            RoomType.Double => "DBL",
            _ => "TRP"
        };

        public static string GuestTypeText(GuestType type) => type switch
        {
            GuestType.Adult => "Adult",
            GuestType.Child => "Child (6-12)",
            _ => "Infant (0-5)"
        };

        public static string CategoryText(PackageCategory category) =>
            category == PackageCategory.FiveStars ? "5 stars" : "4 stars";

        /// <summary>
        /// Every part is optional. <paramref name="flightDate"/> is the actual flight day and falls
        /// back to the package date — but only once something else about the flight is known, so a
        /// booking with no flight details doesn't show a date it never recorded.
        /// </summary>
        public static string FormatFlight(string? flightNo, DateOnly? flightDate, DateOnly packageDate, TimeOnly? time)
        {
            if (string.IsNullOrWhiteSpace(flightNo) && flightDate is null && time is null)
                return "—";

            var when = $"{flightDate ?? packageDate:dd MMM}" + (time is { } t ? $" {t:HH\\:mm}" : "");
            return string.IsNullOrWhiteSpace(flightNo) ? when : $"{flightNo.Trim()} · {when}";
        }

        public static string FormatRooms(IReadOnlyList<RoomTally> rooms) =>
            rooms.Count == 0 ? "—" : string.Join(" + ", rooms.Select(r => $"{r.Quantity} {RoomTypeText(r.Type)}"));

        /// <summary>Excel rejects sheet names over 31 chars or containing []:*?/\.</summary>
        public static string SheetName(string value, string fallback)
        {
            var cleaned = new string(value.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
            if (string.IsNullOrEmpty(cleaned)) cleaned = fallback;
            return cleaned.Length <= 31 ? cleaned : cleaned[..31];
        }

        /// <summary>Names are operator-entered free text and may carry stray whitespace.</summary>
        public static string SanitizeFileName(string value, string fallback)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ' ' }).ToArray();
            var cleaned = new string(value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        /// <summary>Writes a bold, filled, bottom-bordered header row starting at column 1.</summary>
        public static void WriteHeaderRow(IXLWorksheet sheet, int row, params string[] headers)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = HeaderFill;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
        }

        /// <summary>A merged label/value pair used for the stacked facts above a table.</summary>
        public static void WriteField(IXLWorksheet sheet, int row, string label, string value, int lastColumn)
        {
            var l = sheet.Cell(row, 1);
            l.Value = label;
            l.Style.Font.Bold = true;
            l.Style.Font.FontColor = MutedText;

            sheet.Range(row, 2, row, lastColumn).Merge().FirstCell().Value = value;
        }
    }
}
