using System.Text.Json;

namespace Travel.Presentation.Services.Contracts
{
    /// <summary>
    /// The one shape the contract page edits, the draft stores and the PDF renders — so the preview and
    /// the final PDF can't drift. It has no CPF / RG / address property on purpose: those travel only in
    /// <see cref="ContractPartyDetails"/>, which is never persisted.
    ///
    /// The legal clauses are not here either; they are read live from Contract Terms at render time.
    /// </summary>
    public sealed class ContractDocument
    {
        /// <summary>The CONTRATANTE's name. Defaults to the lead guest.</summary>
        public string PartyName { get; set; } = string.Empty;

        public List<ContractPassenger> Passengers { get; set; } = new();
        public ContractPayment Payment { get; set; } = new();

        /// <summary>"AGOSTO/2026".</summary>
        public string GroupLabel { get; set; } = string.Empty;

        /// <summary>"Cairo + Cruzeiro + Hurghada".</summary>
        public string TripTitle { get; set; } = string.Empty;

        public List<ContractSection> Schedule { get; set; } = new();
        public List<string> Includes { get; set; } = new();
        public List<string> Excludes { get; set; } = new();
        public List<ContractAccommodation> Accommodation { get; set; } = new();
        public string Important { get; set; } = string.Empty;
        public string Documents { get; set; } = string.Empty;
        public DateOnly Date { get; set; }

        /// <summary>N in "D dias / N noites" — the nights the booking's stays carry, never typed.</summary>
        public int TotalNights => Schedule.Sum(s => Math.Max(0, s.Nights));
    }

    public sealed class ContractPassenger
    {
        public string Name { get; set; } = string.Empty;
        public string TypeLabel { get; set; } = string.Empty;
    }

    public sealed class ContractPayment
    {
        public string Title { get; set; } = string.Empty;
        public List<ContractPaymentLine> Lines { get; set; } = new();

        /// <summary>Null while the booking is unpriced — printed blank, never "US$0,00".</summary>
        public decimal? Total { get; set; }
        public decimal Paid { get; set; }
        public decimal? Balance { get; set; }

        /// <summary>Free text under the figures, e.g. "Valores por pessoa em quarto duplo."</summary>
        public string ExtraText { get; set; } = string.Empty;
    }

    public sealed class ContractPaymentLine
    {
        public string Label { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
    }

    /// <summary>One "Programação X" block of the schedule — one matched booking stay.</summary>
    public sealed class ContractSection
    {
        /// <summary>Printed above the section's days; blank prints no heading.</summary>
        public string Heading { get; set; } = string.Empty;

        /// <summary>The stay's nights. Feeds the trip length; 0 for a section the operator added.</summary>
        public int Nights { get; set; }

        public List<ContractDay> Days { get; set; } = new();
    }

    public sealed class ContractDay
    {
        /// <summary>Without the number — "Chegada ao Cairo". "Dia 01 –" is generated.</summary>
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        /// <summary>A free day added for an extra night. Highlighted on the page, printed like any day.</summary>
        public bool IsInserted { get; set; }
    }

    public sealed class ContractAccommodation
    {
        public string Destination { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// CPF, RG and address — typed on the page for every preview and generation, and never saved:
    /// not on the booking, not in the draft, not in a log line or the audit trail (decision 3).
    /// </summary>
    public sealed class ContractPartyDetails
    {
        public string Cpf { get; set; } = string.Empty;
        public string Rg { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }

    /// <summary>
    /// The booking facts a document was built from, per section. Comparing a fresh basis with the
    /// stored one says which sections the booking has changed since — without second-guessing what the
    /// operator edited on purpose (the user's call; "fingerprint the facts, don't timestamp the row").
    /// </summary>
    public sealed class ContractBasis
    {
        public List<string> Passengers { get; set; } = new();
        public List<string> Payment { get; set; } = new();
        public List<string> Schedule { get; set; } = new();
        public List<string> Lists { get; set; } = new();
        public List<string> Accommodation { get; set; } = new();

        public IReadOnlyList<string> SectionsDifferingFrom(ContractBasis fresh)
        {
            var stale = new List<string>();
            if (!Passengers.SequenceEqual(fresh.Passengers)) stale.Add(ContractSections.Passengers);
            if (!Payment.SequenceEqual(fresh.Payment)) stale.Add(ContractSections.Payment);
            if (!Schedule.SequenceEqual(fresh.Schedule)) stale.Add(ContractSections.Schedule);
            if (!Lists.SequenceEqual(fresh.Lists)) stale.Add(ContractSections.Lists);
            if (!Accommodation.SequenceEqual(fresh.Accommodation)) stale.Add(ContractSections.Accommodation);
            return stale;
        }

        /// <summary>Copies one section's facts from <paramref name="fresh"/> — the Refresh-from-booking half.</summary>
        public void Take(string section, ContractBasis fresh)
        {
            switch (section)
            {
                case ContractSections.Passengers: Passengers = fresh.Passengers.ToList(); break;
                case ContractSections.Payment: Payment = fresh.Payment.ToList(); break;
                case ContractSections.Schedule: Schedule = fresh.Schedule.ToList(); break;
                case ContractSections.Lists: Lists = fresh.Lists.ToList(); break;
                case ContractSections.Accommodation: Accommodation = fresh.Accommodation.ToList(); break;
            }
        }
    }

    /// <summary>The booking-driven sections a stale draft can be flagged and refreshed on.</summary>
    public static class ContractSections
    {
        public const string Passengers = "passengers";
        public const string Payment = "payment";
        public const string Schedule = "schedule";
        public const string Lists = "lists";
        public const string Accommodation = "accommodation";

        public static readonly IReadOnlyList<string> All = new[] { Passengers, Payment, Schedule, Lists, Accommodation };

        public static string Label(string section) => section switch
        {
            Passengers => "passengers",
            Payment => "payment",
            Schedule => "schedule (nights)",
            Lists => "included / not included",
            Accommodation => "accommodation",
            _ => section,
        };

        /// <summary>Replaces one section of <paramref name="target"/> with the fresh build's.</summary>
        public static void Refresh(string section, ContractDocument target, ContractDocument fresh)
        {
            switch (section)
            {
                case Passengers:
                    target.Passengers = fresh.Passengers;
                    break;
                case Payment:
                    // The free-text line is the operator's, not the booking's.
                    var extra = target.Payment.ExtraText;
                    target.Payment = fresh.Payment;
                    target.Payment.ExtraText = extra;
                    break;
                case Schedule:
                    target.Schedule = fresh.Schedule;
                    break;
                case Lists:
                    target.Includes = fresh.Includes;
                    target.Excludes = fresh.Excludes;
                    break;
                case Accommodation:
                    target.Accommodation = fresh.Accommodation;
                    break;
            }
        }
    }

    /// <summary>What <c>BookingContract.DraftJson</c> holds.</summary>
    public sealed class ContractDraft
    {
        public ContractDocument Document { get; set; } = new();
        public ContractBasis Basis { get; set; } = new();

        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public string Serialize() => JsonSerializer.Serialize(this, Json);

        /// <summary>Null when the stored JSON can't be read — the page then starts from a fresh build.</summary>
        public static ContractDraft? TryDeserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<ContractDraft>(json, Json); }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>A note shown on the contract page — never printed.</summary>
    public sealed record ContractWarning(string Level, string Message, string? Section = null)
    {
        public const string Danger = "danger";
        public const string Warning = "warning";
    }
}
