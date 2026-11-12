using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Presentation.Services.Contracts
{
    /// <summary>Everything one contract render needs. The terms are read live from Setup, never from the draft.</summary>
    public sealed record ContractPrintModel(
        string BookingCode,
        ContractDocument Document,
        ContractPartyDetails Party,
        ContractSettings Settings,
        IReadOnlyList<ContractTermsSection> Terms,
        byte[] LogoPng);

    /// <summary>
    /// The client contract — a formal legal document, like the company's Word file but cleaner
    /// (decision 14): logo and title on top, justified body, bold clause headings, numbered days,
    /// signature lines and "Página X de Y".
    ///
    /// <para>
    /// Only glyphs Poppins has: QuestPDF 2026.9 throws on a missing one and has no system-font fallback.
    /// List bullets are drawn boxes, not "•".
    /// </para>
    /// </summary>
    public sealed partial class BookingContractPdf : IDocument
    {
        public const string ContentType = "application/pdf";

        private const string Font = "Poppins";
        private const float BodySize = 9.5f;

        private static readonly Color Ink = Color.FromHex("#18181B");
        private static readonly Color Muted = Color.FromHex("#52525B");
        private static readonly Color Rule = Color.FromHex("#D4D4D8");
        private static readonly Color Accent = Color.FromHex("#1F2E6B");

        private const string Blank = "____________________";

        private readonly ContractPrintModel _m;

        public BookingContractPdf(ContractPrintModel model) => _m = model;

        public static byte[] Render(ContractPrintModel model) => new BookingContractPdf(model).GeneratePdf();

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"Contrato {_m.BookingCode}",
            Author = "Musta Travel",
            Language = "pt-BR",
        };

        public void Compose(IDocumentContainer container)
        {
            var doc = _m.Document;

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(50);
                page.MarginTop(36);
                page.MarginBottom(30);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(BodySize).FontColor(Ink).LineHeight(1.35f));

                page.Content().Column(col =>
                {
                    col.Spacing(7);

                    col.Item().AlignCenter().Height(46).Image(_m.LogoPng).FitHeight();
                    col.Item().PaddingTop(4).PaddingBottom(6).AlignCenter()
                        .Text(_m.Settings.DocumentTitle).Bold().FontSize(12).LetterSpacing(0.02f);

                    Terms(col, ContractTermsPlacement.Opening);

                    Heading(col, ContractText.PassengersHeading);
                    foreach (var p in doc.Passengers)
                        col.Item().PaddingLeft(10).Text(t =>
                        {
                            t.Span(p.Name).SemiBold();
                            if (!string.IsNullOrWhiteSpace(p.TypeLabel)) t.Span($" — {p.TypeLabel}").FontColor(Muted);
                        });

                    Heading(col, ContractText.PaymentHeading);
                    col.Item().Element(Payment);

                    col.Item().PaddingTop(6).Text(ContractText.Group(doc.GroupLabel)).Bold();

                    Terms(col, ContractTermsPlacement.ObjectClause);

                    col.Item().PaddingTop(4).Text(ContractText.TripHeading(doc.TripTitle, doc.TotalNights)).Bold().FontSize(10.5f);
                    Schedule(col);

                    if (doc.Includes.Count > 0)
                    {
                        Heading(col, ContractText.IncludesHeading);
                        Bullets(col, doc.Includes);
                    }
                    if (doc.Excludes.Count > 0)
                    {
                        Heading(col, ContractText.ExcludesHeading);
                        Bullets(col, doc.Excludes);
                    }
                    if (doc.Accommodation.Count > 0)
                    {
                        Heading(col, ContractText.AccommodationHeading);
                        foreach (var a in doc.Accommodation)
                            col.Item().PaddingLeft(10).Text(t =>
                            {
                                t.Span($"{a.Destination}: ").Bold();
                                t.Span(a.Text);
                            });
                    }
                    if (!string.IsNullOrWhiteSpace(doc.Important))
                    {
                        Heading(col, ContractText.ImportantHeading);
                        Paragraphs(col, doc.Important);
                    }
                    if (!string.IsNullOrWhiteSpace(doc.Documents))
                    {
                        Heading(col, ContractText.DocumentsHeading);
                        Paragraphs(col, doc.Documents);
                    }

                    Terms(col, ContractTermsPlacement.Closing);

                    col.Item().PaddingTop(14).Element(Signatures);
                });

                page.Footer().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(_m.BookingCode).FontSize(7.5f).FontColor(Muted);
                    row.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                        t.Span("Página ");
                        t.CurrentPageNumber();
                        t.Span(" de ");
                        t.TotalPages();
                    });
                });
            });
        }

        // ── Sections ────────────────────────────────────────────────────────────────────────────────

        private static void Heading(ColumnDescriptor col, string text) =>
            col.Item().PaddingTop(8).Text(text).Bold().FontSize(10.5f);

        private void Terms(ColumnDescriptor col, ContractTermsPlacement placement)
        {
            foreach (var section in _m.Terms.Where(t => t.Placement == placement))
            {
                if (!string.IsNullOrWhiteSpace(section.Heading))
                    col.Item().PaddingTop(8).EnsureSpace(60).Text(section.Heading.Trim()).Bold().FontSize(10.5f);
                Paragraphs(col, FillPlaceholders(section.Body));
            }
        }

        /// <summary>
        /// The four known placeholders. Anything else in braces stays visible, so a typo in Setup shows
        /// on the page instead of silently printing nothing. An empty field prints a blank line to fill.
        /// </summary>
        private string FillPlaceholders(string body)
        {
            static string OrBlank(string? value) => string.IsNullOrWhiteSpace(value) ? Blank : value.Trim();

            return body
                .Replace("{Contratante}", OrBlank(_m.Document.PartyName))
                .Replace("{CPF}", OrBlank(_m.Party.Cpf))
                .Replace("{RG}", OrBlank(_m.Party.Rg))
                .Replace("{Endereco}", OrBlank(_m.Party.Address));
        }

        private void Payment(IContainer container)
        {
            var payment = _m.Document.Payment;
            container.PaddingLeft(10).Column(col =>
            {
                col.Spacing(2);
                if (!string.IsNullOrWhiteSpace(payment.Title))
                    col.Item().Text(payment.Title).SemiBold();

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(110);
                    });

                    void Line(string label, string amount, bool strong = false, bool ruleAbove = false)
                    {
                        var left = table.Cell().PaddingVertical(1.5f);
                        var right = table.Cell().PaddingVertical(1.5f);
                        if (ruleAbove)
                        {
                            left = left.BorderTop(0.75f).BorderColor(Rule).PaddingTop(3);
                            right = right.BorderTop(0.75f).BorderColor(Rule).PaddingTop(3);
                        }
                        var l = left.Text(label);
                        var r = right.AlignRight().Text(amount);
                        if (strong) { l.Bold(); r.Bold(); }
                    }

                    foreach (var line in payment.Lines)
                        Line(line.Label, ContractText.Money(line.Amount));
                    Line(ContractText.TotalLabel + ":", ContractText.Money(payment.Total), strong: true, ruleAbove: true);
                    Line(ContractText.PaidLabel + ":", ContractText.Money(payment.Paid));
                    Line(ContractText.BalanceLabel + ":", ContractText.Money(payment.Balance), strong: true);
                });

                if (!string.IsNullOrWhiteSpace(payment.ExtraText))
                    col.Item().PaddingTop(2).Text(payment.ExtraText.Trim()).FontColor(Muted);
            });
        }

        private void Schedule(ColumnDescriptor col)
        {
            var number = 0;
            foreach (var section in _m.Document.Schedule)
            {
                if (!string.IsNullOrWhiteSpace(section.Heading))
                    col.Item().PaddingTop(8).EnsureSpace(70).Text(section.Heading.Trim()).Bold().FontColor(Accent).FontSize(10.5f);

                foreach (var day in section.Days)
                {
                    number++;
                    var title = string.IsNullOrWhiteSpace(day.Title)
                        ? ContractText.DayNumber(number)
                        : $"{ContractText.DayNumber(number)} – {day.Title.Trim()}";
                    col.Item().PaddingTop(5).EnsureSpace(40).Text(title).Bold();
                    Paragraphs(col, day.Body, notes: true);
                }
            }
        }

        private static void Bullets(ColumnDescriptor col, IEnumerable<string> lines)
        {
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                col.Item().PaddingLeft(10).Row(row =>
                {
                    row.ConstantItem(12).PaddingTop(5).AlignLeft().Width(3.5f).Height(3.5f).Background(Ink);
                    row.RelativeItem().Text(line.Trim());
                });
        }

        /// <summary>
        /// Blank lines separate paragraphs; single line breaks are kept. With <paramref name="notes"/>,
        /// a line starting "* " prints as a muted note, as in the Word file.
        /// </summary>
        private static void Paragraphs(ColumnDescriptor col, string? text, bool notes = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            foreach (var paragraph in ParagraphBreak().Split(text.Replace("\r\n", "\n").Trim()))
            {
                var lines = paragraph.Split('\n').Select(l => l.TrimEnd()).ToList();
                var run = new List<string>();

                void Flush()
                {
                    if (run.Count == 0) return;
                    var body = string.Join("\n", run);
                    col.Item().Text(t => { t.Justify(); t.Span(body); });
                    run.Clear();
                }

                foreach (var line in lines)
                {
                    if (notes && line.TrimStart().StartsWith("* "))
                    {
                        Flush();
                        col.Item().Text(line.Trim()).FontSize(8.5f).FontColor(Muted);
                    }
                    else run.Add(line);
                }
                Flush();
            }
        }

        private void Signatures(IContainer container)
        {
            var doc = _m.Document;
            // ShowEntire: the date, both names and both lines move to the next page together.
            container.ShowEntire().Column(col =>
            {
                col.Spacing(4);
                col.Item().Text(ContractText.SignatureDate(_m.Settings.SignatureCity, doc.Date));

                void Block(string label, string name)
                {
                    col.Item().PaddingTop(26).Width(260).LineHorizontal(0.75f).LineColor(Ink);
                    col.Item().Text(t =>
                    {
                        t.Span($"{label}: ").Bold();
                        t.Span(name);
                    });
                }

                Block(ContractText.ContractedLabel, _m.Settings.CompanySignatureName);
                Block(ContractText.ContractingLabel, string.IsNullOrWhiteSpace(doc.PartyName) ? Blank : doc.PartyName.Trim());
            });
        }

        [GeneratedRegex(@"\n[ \t]*\n+")]
        private static partial Regex ParagraphBreak();
    }
}
