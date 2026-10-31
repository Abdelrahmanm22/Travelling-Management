using System.Globalization;
using Travel.Domain.Enums;

namespace Travel.Presentation.Services.Vouchers
{
    /// <summary>
    /// Every word the voucher and its email print, in both languages. One instance per
    /// <see cref="VoucherLanguage"/> so the PDF, the email body and its subject can never mix them.
    /// </summary>
    public sealed class VoucherText
    {
        public required VoucherLanguage Language { get; init; }
        public required CultureInfo Culture { get; init; }

        public required string Code { get; init; }               // "EN" / "PT" — file name suffix
        public required string DocumentTitle { get; init; }
        public required string HeroLine1 { get; init; }
        public required string HeroLine2 { get; init; }
        public required string PreparedFor { get; init; }        // {0} lead guest, {1} issue date
        public required string BookingNo { get; init; }
        public required string Arrival { get; init; }
        public required string Departure { get; init; }
        public required string Flight { get; init; }
        public required string Lands { get; init; }              // {0} time
        public required string Departs { get; init; }            // {0} time
        public required string FlightToFollow { get; init; }
        public required string Itinerary { get; init; }
        public required string ArrivalIn { get; init; }          // {0} city
        public required string DepartureFrom { get; init; }      // {0} city
        public required string ArrivalTag { get; init; }
        public required string DepartureTag { get; init; }
        public required string CruiseTag { get; init; }
        public required string HotelToConfirm { get; init; }
        public required string ShipToConfirm { get; init; }
        public required string ItineraryToConfirm { get; init; }
        public required string Travellers { get; init; }
        public required string LeadGuest { get; init; }
        public required string PendingConfirmation { get; init; }
        public required string PresentAtCheckIn { get; init; }
        public required string IssuedOn { get; init; }           // {0} date
        public required string EmailSubject { get; init; }       // {0} booking code
        public required string EmailGreeting { get; init; }      // {0} lead guest
        public required string EmailIntro { get; init; }
        public required string EmailAttached { get; init; }
        public required string EmailQuestions { get; init; }
        public required string EmailSignOff { get; init; }
        public required string EmailTeam { get; init; }
        public required string EmailMessageFrom { get; init; }   // heading over the operator's note

        private static string plural(int n, string one, string many) => n == 1 ? one : many;

        public required (string One, string Many) NightWord { get; init; }
        public required (string One, string Many) StayWord { get; init; }
        public required (string One, string Many) TravellerWord { get; init; }
        public required (string One, string Many) AdultWord { get; init; }
        public required (string One, string Many) ChildWord { get; init; }
        public required (string One, string Many) InfantWord { get; init; }
        public required string YearsShort { get; init; }        // {0} age
        public required string AdultType { get; init; }
        public required string ChildType { get; init; }
        public required string InfantType { get; init; }

        public string Nights(int n) => $"{n} {plural(n, NightWord.One, NightWord.Many)}";
        public string Stays(int n) => $"{n} {plural(n, StayWord.One, StayWord.Many)}";
        public string TravellerCount(int n) => $"{n} {plural(n, TravellerWord.One, TravellerWord.Many)}";
        public string Adults(int n) => $"{n} {plural(n, AdultWord.One, AdultWord.Many)}";
        public string Children(int n) => $"{n} {plural(n, ChildWord.One, ChildWord.Many)}";
        public string Infants(int n) => $"{n} {plural(n, InfantWord.One, InfantWord.Many)}";

        public string GuestType(GuestType type) => type switch
        {
            Domain.Enums.GuestType.Child => ChildType,
            Domain.Enums.GuestType.Infant => InfantType,
            _ => AdultType,
        };

        /// <summary>"Thu, 12 Nov 2026" / "Qui, 12 nov 2026".</summary>
        public string LongDate(DateOnly date) => Capitalise(date.ToString("ddd, dd MMM yyyy", Culture).Replace(".", ""));

        /// <summary>"12 Nov 2026" / "12 nov 2026".</summary>
        public string Date(DateOnly date) => date.ToString("dd MMM yyyy", Culture).Replace(".", "");

        /// <summary>"NOV" — the timeline's month under the day number.</summary>
        public string MonthShort(DateOnly date) => date.ToString("MMM", Culture).Replace(".", "").ToUpperInvariant();

        /// <summary>"12 → 14 Nov", or "30 Nov → 02 Dec" across a month boundary.</summary>
        public string Range(DateOnly from, DateOnly to) =>
            from.Month == to.Month && from.Year == to.Year
                ? $"{from:dd} → {to.ToString("dd MMM", Culture).Replace(".", "")}"
                : $"{from.ToString("dd MMM", Culture).Replace(".", "")} → {to.ToString("dd MMM", Culture).Replace(".", "")}";

        private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

        public static VoucherText For(VoucherLanguage language) =>
            language == VoucherLanguage.Portuguese ? Portuguese : English;

        public static readonly VoucherText English = new()
        {
            Language = VoucherLanguage.English,
            Culture = CultureInfo.GetCultureInfo("en-GB"),
            Code = "EN",
            DocumentTitle = "TRAVEL VOUCHER",
            HeroLine1 = "Your journey",
            HeroLine2 = "through Egypt",
            PreparedFor = "Prepared for {0} · issued {1}",
            BookingNo = "Booking no.",
            Arrival = "Arrival",
            Departure = "Departure",
            Flight = "Flight",
            Lands = "lands {0}",
            Departs = "departs {0}",
            FlightToFollow = "Flight details to follow",
            Itinerary = "Your itinerary",
            ArrivalIn = "Arrival in {0}",
            DepartureFrom = "Departure from {0}",
            ArrivalTag = "ARRIVAL",
            DepartureTag = "DEPARTURE",
            CruiseTag = "CRUISE",
            HotelToConfirm = "Hotel to be confirmed",
            ShipToConfirm = "Cruise ship to be confirmed",
            ItineraryToConfirm = "Your itinerary will be confirmed shortly.",
            Travellers = "Travellers",
            LeadGuest = "LEAD GUEST",
            PendingConfirmation = "Pending confirmation",
            PresentAtCheckIn = "Please present this voucher at check-in",
            IssuedOn = "Issued {0}",
            EmailSubject = "Your travel voucher · {0}",
            EmailGreeting = "Dear {0},",
            EmailIntro = "Thank you for travelling with Musta Travel. Here is a summary of your trip.",
            EmailAttached = "Your travel voucher is attached as a PDF. Please keep it on your phone or print it, and present it at check-in.",
            EmailQuestions = "Questions about your trip? Reply to this email or write to us at",
            EmailSignOff = "Have a wonderful journey,",
            EmailTeam = "The Musta Travel team",
            EmailMessageFrom = "A note from our team",
            NightWord = ("night", "nights"),
            StayWord = ("stay", "stays"),
            TravellerWord = ("traveller", "travellers"),
            AdultWord = ("adult", "adults"),
            ChildWord = ("child", "children"),
            InfantWord = ("infant", "infants"),
            YearsShort = "{0} yrs",
            AdultType = "Adult",
            ChildType = "Child",
            InfantType = "Infant",
        };

        public static readonly VoucherText Portuguese = new()
        {
            Language = VoucherLanguage.Portuguese,
            Culture = CultureInfo.GetCultureInfo("pt-BR"),
            Code = "PT",
            DocumentTitle = "VOUCHER DE VIAGEM",
            HeroLine1 = "Sua viagem",
            HeroLine2 = "pelo Egito",
            PreparedFor = "Preparado para {0} · emitido em {1}",
            BookingNo = "Nº da reserva",
            Arrival = "Chegada",
            Departure = "Partida",
            Flight = "Voo",
            Lands = "pouso às {0}",
            Departs = "decolagem às {0}",
            FlightToFollow = "Detalhes do voo a confirmar",
            Itinerary = "Seu roteiro",
            ArrivalIn = "Chegada em {0}",
            DepartureFrom = "Partida de {0}",
            ArrivalTag = "CHEGADA",
            DepartureTag = "PARTIDA",
            CruiseTag = "CRUZEIRO",
            HotelToConfirm = "Hotel a confirmar",
            ShipToConfirm = "Navio a confirmar",
            ItineraryToConfirm = "Seu roteiro será confirmado em breve.",
            Travellers = "Viajantes",
            LeadGuest = "TITULAR",
            PendingConfirmation = "Aguardando confirmação",
            PresentAtCheckIn = "Apresente este voucher no check-in",
            IssuedOn = "Emitido em {0}",
            EmailSubject = "Seu voucher de viagem · {0}",
            EmailGreeting = "Olá, {0},",
            EmailIntro = "Obrigado por viajar com a Musta Travel. Aqui está o resumo da sua viagem.",
            EmailAttached = "Seu voucher de viagem segue em anexo (PDF). Guarde-o no celular ou imprima-o e apresente-o no check-in.",
            EmailQuestions = "Dúvidas sobre a viagem? Responda a este e-mail ou escreva para",
            EmailSignOff = "Desejamos uma ótima viagem,",
            EmailTeam = "Equipe Musta Travel",
            EmailMessageFrom = "Um recado da nossa equipe",
            NightWord = ("noite", "noites"),
            StayWord = ("estadia", "estadias"),
            TravellerWord = ("viajante", "viajantes"),
            AdultWord = ("adulto", "adultos"),
            ChildWord = ("criança", "crianças"),
            InfantWord = ("bebê", "bebês"),
            YearsShort = "{0} anos",
            AdultType = "Adulto",
            ChildType = "Criança",
            InfantType = "Bebê",
        };
    }
}
