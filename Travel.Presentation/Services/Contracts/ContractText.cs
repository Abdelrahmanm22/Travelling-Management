using System.Globalization;
using Travel.Domain.Enums;

namespace Travel.Presentation.Services.Contracts
{
    /// <summary>
    /// Every generated word of the contract, in Portuguese (decision 19). The contract is pt-BR only, so
    /// this is a static class rather than a <c>VoucherText</c>-style pair of instances.
    ///
    /// <para>
    /// Money is pt-BR here — "US$1.950,00" — deliberately unlike <c>Money.Format</c>, which stays
    /// invariant for every screen in the app.
    /// </para>
    /// </summary>
    public static class ContractText
    {
        public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

        public const string PassengersHeading = "Nome / Dados do Passageiro:";
        public const string PaymentHeading = "Forma de pagamento:";
        public const string TotalLabel = "TOTAL";
        public const string PaidLabel = "Entrada (pago)";
        public const string BalanceLabel = "Saldo devedor";
        public const string ExtraNightsLabel = "Noites extras";
        public const string ToursLabel = "Passeios extras";
        public const string IncludesHeading = "Pacote Inclui";
        public const string ExcludesHeading = "Não está incluso";
        public const string AccommodationHeading = "Hospedagem";
        public const string ImportantHeading = "Importante:";
        public const string DocumentsHeading = "Documentos Requeridos";
        public const string VisaLine = "Visto de entrada no Egito";
        public const string ContractedLabel = "CONTRATADA";
        public const string ContractingLabel = "CONTRATANTE";

        /// <summary>"US$1.950,00".</summary>
        public static string Money(decimal amount) => "US$" + amount.ToString("#,##0.00", Culture);

        /// <summary>"US$1.950,00", or empty when unknown — never "US$0,00" for an unpriced booking.</summary>
        public static string Money(decimal? amount) => amount is { } a ? Money(a) : string.Empty;

        /// <summary>"AGOSTO/2026".</summary>
        public static string GroupLabel(DateOnly arrival) =>
            $"{Culture.DateTimeFormat.GetMonthName(arrival.Month).ToUpper(Culture)}/{arrival.Year}";

        public static string Group(string label) => $"GRUPO {label}";

        public static string GuestType(GuestType type) => type switch
        {
            Domain.Enums.GuestType.Child => "Criança",
            Domain.Enums.GuestType.Infant => "Bebê",
            _ => "Adulto",
        };

        /// <summary>"2 adultos", "1 criança", "1 bebê".</summary>
        public static string PaxCount(int count, GuestType type) => (type, count == 1) switch
        {
            (Domain.Enums.GuestType.Child, true) => "1 criança",
            (Domain.Enums.GuestType.Child, false) => $"{count} crianças",
            (Domain.Enums.GuestType.Infant, true) => "1 bebê",
            (Domain.Enums.GuestType.Infant, false) => $"{count} bebês",
            (_, true) => "1 adulto",
            _ => $"{count} adultos",
        };

        /// <summary>"2 adultos × US$1.950,00".</summary>
        public static string PaxLine(int count, GuestType type, decimal? price) =>
            price is { } p ? $"{PaxCount(count, type)} × {Money(p)}" : PaxCount(count, type);

        public static string PaymentTitle(string tripTitle) => $"Roteiro {tripTitle.ToUpper(Culture)}";

        public static string Category(PackageCategory category) =>
            category == PackageCategory.FiveStars ? "5 estrelas" : "4 estrelas";

        /// <summary>"Dia 01".</summary>
        public static string DayNumber(int number) => $"Dia {number:00}";

        /// <summary>"10 dias / 9 noites".</summary>
        public static string TripLength(int nights)
        {
            var days = nights + 1;
            return $"{days} {(days == 1 ? "dia" : "dias")} / {nights} {(nights == 1 ? "noite" : "noites")}";
        }

        public static string TripHeading(string tripTitle, int nights) => $"VIAGEM EGITO: {tripTitle} {TripLength(nights)}";

        public static string SectionHeading(string destination) => $"Programação {destination}";

        public static string FreeDayTitle(string destination) => $"{destination} – dia livre";

        public static string TourLine(string tourName, int pax) => $"Passeio extra: {tourName} ({pax} pax)";

        /// <summary>"São Paulo, 17/08/2026".</summary>
        public static string SignatureDate(string city, DateOnly date) =>
            $"{city}, {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

        public static string FileName(string bookingCode) =>
            $"Contrato_{ExcelFormat.SanitizeFileName(bookingCode, "booking")}.pdf";

        /// <summary>Today in Cairo, where the company operates. Falls back to UTC if the zone is unknown.</summary>
        public static DateOnly Today()
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return DateOnly.FromDateTime(DateTime.UtcNow);
            }
        }
    }
}
