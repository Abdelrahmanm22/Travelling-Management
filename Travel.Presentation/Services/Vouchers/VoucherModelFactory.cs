using Microsoft.Extensions.Options;
using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Presentation.Services.Vouchers
{
    /// <summary>The contact block printed on every voucher — appsettings "Voucher", not code.</summary>
    public sealed class VoucherOptions
    {
        public string ContactEmail { get; set; } = string.Empty;

        /// <summary>Instagram handles without the "@".</summary>
        public List<string> Instagram { get; set; } = new();
    }

    /// <summary>
    /// Turns a booking into a <see cref="VoucherModel"/>. Needs the <c>BookingWithDetailsSpec</c> graph —
    /// <c>Stays.Destination</c> and <c>Guests</c> are read here, and a missing include renders an empty
    /// itinerary rather than throwing.
    /// </summary>
    public sealed class VoucherModelFactory
    {
        private readonly VoucherOptions _options;

        public VoucherModelFactory(IOptions<VoucherOptions> options) => _options = options.Value;

        public VoucherModel Build(Booking booking, VoucherLanguage language, DateOnly issuedOn)
        {
            var t = VoucherText.For(language);

            var stays = booking.Stays.OrderBy(s => s.SortOrder).ThenBy(s => s.CheckInDate).ToList();
            var fromCity = stays.FirstOrDefault()?.Destination?.Name?.Trim() ?? booking.Package?.Name?.Trim() ?? "—";
            var toCity = stays.LastOrDefault()?.Destination?.Name?.Trim() ?? fromCity;

            var arrivalDate = booking.FlightArrivalDate ?? booking.ArrivalDate;
            var departureDate = booking.FlightDepartureDate ?? booking.DepartureDate;

            var guests = booking.Guests
                .OrderByDescending(g => g.IsLeadGuest)
                .ThenBy(g => g.SortOrder)
                .Select(g => new VoucherGuest(
                    g.FullName.Trim(),
                    Initials(g.FullName),
                    g.Age is { } age && g.Type != GuestType.Adult
                        ? $"{t.GuestType(g.Type)} · {string.Format(t.YearsShort, age)}"
                        : t.GuestType(g.Type),
                    g.IsLeadGuest))
                .ToList();

            var leadName = guests.FirstOrDefault()?.Name ?? booking.LeadGuestName;

            var timeline = new List<VoucherTimelineItem>
            {
                new(VoucherTimelineKind.Arrival, arrivalDate.ToString("dd"), t.MonthShort(arrivalDate),
                    string.Format(t.ArrivalIn, fromCity), FlightLine(t, booking.ArrivalFlightNo, booking.ArrivalTime, t.Lands),
                    t.ArrivalTag, null, null),
            };

            foreach (var stay in stays)
            {
                var checkOut = stay.CheckInDate.AddDays(stay.Nights);
                var hotel = string.IsNullOrWhiteSpace(stay.HotelName)
                    ? (stay.IsCruise ? t.ShipToConfirm : t.HotelToConfirm)
                    : stay.HotelName.Trim();

                timeline.Add(new(VoucherTimelineKind.Stay, stay.CheckInDate.ToString("dd"), t.MonthShort(stay.CheckInDate),
                    stay.Destination?.Name?.Trim() ?? "—", hotel,
                    stay.IsCruise ? t.CruiseTag : null, t.Nights(stay.Nights), t.Range(stay.CheckInDate, checkOut)));
            }

            timeline.Add(new(VoucherTimelineKind.Departure, departureDate.ToString("dd"), t.MonthShort(departureDate),
                string.Format(t.DepartureFrom, toCity), FlightLine(t, booking.DepartureFlightNo, booking.DepartureTime, t.Departs),
                t.DepartureTag, null, null));

            var pax = new List<string> { t.Adults(booking.Adults) };
            if (booking.Children > 0) pax.Add(t.Children(booking.Children));
            if (booking.Infants > 0) pax.Add(t.Infants(booking.Infants));

            return new VoucherModel(
                Text: t,
                BookingCode: booking.Code,
                LeadGuestName: leadName,
                IssuedOn: t.Date(issuedOn),
                Stars: (int)booking.Category,
                CategoryLabel: $"{(int)booking.Category} ★",
                TravellersLabel: t.TravellerCount(booking.Pax),
                NightsLabel: t.Nights(booking.TripNights),
                StaysSummary: $"{t.Stays(stays.Count)} · {t.Nights(booking.TripNights)}",
                PaxSummary: string.Join(" · ", pax),
                // Completed bookings already ran; only the derived "not yet confirmed" state warns.
                IsPending: booking.Status == BookingStatus.InProgress,
                Arrival: new VoucherFlight(t.LongDate(arrivalDate), FlightLine(t, booking.ArrivalFlightNo, booking.ArrivalTime, t.Lands)),
                Departure: new VoucherFlight(t.LongDate(departureDate), FlightLine(t, booking.DepartureFlightNo, booking.DepartureTime, t.Departs)),
                Timeline: timeline,
                Guests: guests,
                ContactEmail: _options.ContactEmail,
                Instagram: _options.Instagram);
        }

        /// <summary>"Flight MS 774 · lands 03:40" — either half may be missing; both missing reads "to follow".</summary>
        private static string FlightLine(VoucherText t, string? flightNo, TimeOnly? time, string timeFormat)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(flightNo)) parts.Add($"{t.Flight} {flightNo.Trim()}");
            if (time is { } tm) parts.Add(string.Format(timeFormat, tm.ToString("HH:mm")));
            return parts.Count == 0 ? t.FlightToFollow : string.Join(" · ", parts);
        }

        private static string Initials(string fullName)
        {
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
            };
        }
    }
}
