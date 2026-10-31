using System.Globalization;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Travel.Presentation.Services.Vouchers
{
    /// <summary>
    /// The travel voucher PDF — theme G "Desert Daylight". Measures come from the approved HTML mockup
    /// at 0.75pt per CSS px. The hero sits on page 1 only; a long itinerary flows onto page 2, and the
    /// footer band repeats on every page.
    /// </summary>
    public sealed class BookingVoucherPdf : IDocument
    {
        public const string ContentType = "application/pdf";

        private const string Font = "Poppins";
        private const float Side = 27;          // page gutter
        private const float HeroHeight = 216;    // the art is never squeezed: a shorter box crops the plane
        private float RowGap => C(5.25f, 4);       // between timeline cards
        private float CardTop => C(176, 160);      // where the booking card starts, overlapping the hero

        private static readonly Color Ink = Color.FromHex("#141A33");
        private static readonly Color Muted = Color.FromHex("#6B7290");
        private static readonly Color Line = Color.FromHex("#E6EAF4");
        private static readonly Color Blue = Color.FromHex("#4A6AC3");
        private static readonly Color BlueDeep = Color.FromHex("#1F2E6B");
        private static readonly Color BlueSoft = Color.FromHex("#EEF2FC");
        private static readonly Color Orange = Color.FromHex("#EB6334");
        private static readonly Color OrangeText = Color.FromHex("#C4481D");
        private static readonly Color OrangeSoft = Color.FromHex("#FFF1EA");
        private static readonly Color Gold = Color.FromHex("#EAA533");
        private static readonly Color AmberSoft = Color.FromHex("#FFF4E0");
        private static readonly Color AmberText = Color.FromHex("#B45309");

        private const string PlanePath =
            "M21 16v-2l-8-5V3.5c0-.83-.67-1.5-1.5-1.5S10 2.67 10 3.5V9l-8 5v2l8-2.5V19l-2 1.5V22l3.5-1 3.5 1v-1.5L13 19v-5.5l8 2.5z";

        private readonly VoucherModel _m;
        private readonly VoucherAssets _assets;
        private readonly bool _compact;
        private readonly PageCountProbe _pages = new();

        /// <param name="compact">Tighter vertical rhythm below the hero, so a 4-stay trip still fits one page.</param>
        public BookingVoucherPdf(VoucherModel model, VoucherAssets assets, bool compact = false)
        {
            _m = model;
            _assets = assets;
            _compact = compact;
        }

        /// <summary>The regular measure, or its compact twin.</summary>
        private float C(float regular, float compact) => _compact ? compact : regular;

        public static string FileName(VoucherModel model) =>
            $"Voucher_{ExcelFormat.SanitizeFileName(model.BookingCode, "booking")}_{model.Text.Code}.pdf";

        /// <summary>
        /// The voucher at the density that suits it (the user's call): regular spacing, unless that runs
        /// past one page — then the compact layout, which keeps a 4-stay trip on a single sheet. A trip
        /// too long even for compact simply flows onto page 2; nothing is ever cut.
        /// </summary>
        public static byte[] Render(VoucherModel model, VoucherAssets assets)
        {
            var regular = new BookingVoucherPdf(model, assets);
            var bytes = regular.GeneratePdf();
            return regular._pages.TotalPages <= 1 ? bytes : new BookingVoucherPdf(model, assets, compact: true).GeneratePdf();
        }

        /// <summary>Records the page count of the finished layout — QuestPDF exposes it only to dynamic content.</summary>
        private sealed class PageCountProbe : IDynamicComponent
        {
            public int TotalPages { get; private set; }

            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                TotalPages = context.TotalPages;
                return new DynamicComponentComposeResult { Content = context.CreateElement(_ => { }), HasMoreContent = false };
            }
        }

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"{_m.Text.DocumentTitle} · {_m.BookingCode}",
            Author = "Musta Travel",
            Language = _m.Text.Culture.Name,
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(9).FontColor(Ink));

                // Page 1 starts with the full-bleed hero; later pages need their own top gutter.
                page.Header().SkipOnce().Height(27);
                page.Content().Column(col =>
                {
                    col.Item().Element(HeroAndCard);
                    col.Item().PaddingHorizontal(Side).PaddingTop(C(14, 10)).Element(Itinerary);
                    // EnsureSpace: never strand the "Travellers" heading at a page foot with its cards overleaf.
                    col.Item().PaddingHorizontal(Side).PaddingTop(C(8, 6)).PaddingBottom(14).EnsureSpace(90).Element(Travellers);
                });
                page.Footer().PaddingHorizontal(Side).PaddingBottom(C(19, 16)).Column(col =>
                {
                    col.Item().Dynamic(_pages);
                    col.Item().Element(Footer);
                });
            });
        }

        // ── Hero + overlapping booking card ─────────────────────────────────────────────────────────

        private void HeroAndCard(IContainer container)
        {
            container.Layers(layers =>
            {
                // Sized to the exact box: a fixed-ratio SVG at 216pt tall needs 595.5pt and A4 is 595.28,
                // which QuestPDF rejects as a layout conflict.
                layers.Layer().Height(HeroHeight).Svg(size => _assets.HeroSvgSized(size.Width, size.Height));

                layers.Layer().Height(HeroHeight).Padding(Side + 3).PaddingTop(C(19.5f, 15)).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.AutoItem()
                            .Background(Colors.White).CornerRadius(10.5f)
                            .Shadow(new BoxShadowStyle { Color = Color.FromHex("#26000000"), Blur = 10, OffsetY = 3 })
                            .PaddingVertical(5).PaddingHorizontal(9)
                            .Width(C(76, 70)).Image(_assets.LogoPng);
                        row.RelativeItem().AlignRight().AlignMiddle()
                            .Text(_m.Text.DocumentTitle).FontSize(7.5f).SemiBold().LetterSpacing(0.24f).FontColor(BlueDeep);
                    });

                    col.Item().PaddingTop(C(6, 5)).Text(text =>
                    {
                        text.Line(_m.Text.HeroLine1).FontSize(C(24.75f, 22)).Bold().LineHeight(0.95f).FontColor(Color.FromHex("#18213F"));
                        text.Span(_m.Text.HeroLine2).FontSize(C(24.75f, 22)).Bold().LineHeight(0.95f).FontColor(Color.FromHex("#18213F"));
                    });
                    col.Item().PaddingTop(C(5, 4)).Text(string.Format(_m.Text.PreparedFor, _m.LeadGuestName, _m.IssuedOn))
                        .FontSize(9.4f).FontColor(Color.FromHex("#55607A")).ClampLines(1);
                });

                layers.PrimaryLayer().PaddingTop(CardTop).PaddingHorizontal(Side).Element(BookingCard);
            });
        }

        private void BookingCard(IContainer container)
        {
            container
                .Background(Colors.White).CornerRadius(16.5f)
                .Shadow(new BoxShadowStyle { Color = Color.FromHex("#38141A33"), Blur = 24, OffsetY = 12, Spread = -8 })
                .Border(0.6f, Color.FromHex("#0D141A33"))
                .PaddingVertical(C(15, 12)).PaddingHorizontal(19.5f)
                .Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Element(Label(_m.Text.BookingNo));
                            c.Item().PaddingTop(1).Text(_m.BookingCode).FontSize(14.25f).Bold().FontColor(BlueDeep);
                        });
                        row.AutoItem().AlignMiddle().Row(chips =>
                        {
                            chips.Spacing(6);
                            if (_m.IsPending)
                                chips.AutoItem().Element(Pill(_m.Text.PendingConfirmation, AmberSoft, AmberText));
                            chips.AutoItem().Element(StarPill(_m.Stars));
                            chips.AutoItem().Element(Pill(_m.TravellersLabel, BlueSoft, BlueDeep));
                            chips.AutoItem().Element(Pill(_m.NightsLabel, BlueSoft, BlueDeep));
                        });
                    });

                    // No From → To route row: most trips start and end in Cairo, and "Cairo → Cairo" read
                    // as a dead end (the user's call — the timeline below already shows the route).
                    col.Item().PaddingTop(C(12, 9)).Row(row =>
                    {
                        row.Spacing(9);
                        row.RelativeItem().Element(Leg(_m.Text.Arrival, _m.Arrival, BlueSoft, "#4A6AC3", 135, C(9, 7)));
                        row.RelativeItem().Element(Leg(_m.Text.Departure, _m.Departure, OrangeSoft, "#EB6334", 45, C(9, 7)));
                    });
                });
        }

        private static Action<IContainer> Leg(string label, VoucherFlight flight, Color background, string iconHex, int rotation, float padding) =>
            container => container
                .Background(background).CornerRadius(10.5f)
                .PaddingVertical(padding).PaddingHorizontal(12)
                .Row(row =>
                {
                    row.Spacing(9);
                    row.ConstantItem(25.5f).AlignMiddle()
                        .Height(25.5f).Background(Colors.White).CornerRadius(8)
                        .Shadow(new BoxShadowStyle { Color = Color.FromHex("#26141A33"), Blur = 4, OffsetY = 1 })
                        .AlignCenter().AlignMiddle().Width(13).Height(13).Svg(Plane(iconHex, rotation));
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Element(Label(label));
                        c.Item().Text(flight.Date).FontSize(10.5f).SemiBold();
                        c.Item().Text(flight.Detail).FontSize(8.25f).FontColor(Muted);
                    });
                });

        // ── Itinerary timeline ──────────────────────────────────────────────────────────────────────

        private void Itinerary(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Element(SectionHeader(_m.Text.Itinerary, _m.StaysSummary, C(9, 6)));

                var items = _m.Timeline;
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var first = i == 0;
                    var last = i == items.Count - 1;

                    // One row per item, kept whole across a page break. The card sets the row's height; the
                    // node column is a layer over it, because a dynamic SVG has no height of its own and
                    // would otherwise stretch the row to the whole page.
                    col.Item().ShowEntire().Layers(layers =>
                    {
                        layers.Layer().PaddingLeft(42).Width(36)
                            .Svg(size => Node(size.Width, size.Height, item.Kind != VoucherTimelineKind.Stay, first, last));

                        layers.PrimaryLayer().Row(row =>
                        {
                            row.ConstantItem(42).PaddingBottom(RowGap).AlignMiddle().Column(c =>
                            {
                                c.Item().AlignCenter().Text(item.Day).FontSize(C(15, 13)).Bold().LineHeight(1);
                                c.Item().AlignCenter().Text(item.Month).FontSize(6.75f).SemiBold().LetterSpacing(0.12f).FontColor(Muted);
                            });
                            row.ConstantItem(36);
                            row.RelativeItem().PaddingBottom(RowGap).Element(TimelineCard(item));
                        });
                    });
                }

                if (items.Count == 2)
                    col.Item().PaddingLeft(78).PaddingBottom(6).Text(_m.Text.ItineraryToConfirm).FontSize(8.25f).Italic().FontColor(Muted);
            });
        }

        private Action<IContainer> TimelineCard(VoucherTimelineItem item) => container =>
        {
            var isFlight = item.Kind != VoucherTimelineKind.Stay;

            var card = container.CornerRadius(10.5f)
                .Shadow(new BoxShadowStyle { Color = Color.FromHex("#1F141A33"), Blur = 12, OffsetY = 4, Spread = -6 });
            card = isFlight
                ? card.BackgroundLinearGradient(0, new[] { Color.FromHex("#FFF6F1"), Colors.White }).Border(0.75f, Color.FromHex("#F8DCCD"))
                : card.Background(Colors.White).Border(0.75f, Line);

            card.PaddingVertical(C(6, 4)).PaddingHorizontal(12).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(c =>
                {
                    c.Item().Row(title =>
                    {
                        title.Spacing(6);
                        title.AutoItem().AlignMiddle().Text(item.Title).FontSize(C(10, 9.5f)).SemiBold();
                        if (item.Tag is not null)
                            title.AutoItem().AlignMiddle().Element(Pill(item.Tag, isFlight ? OrangeSoft : BlueSoft,
                                isFlight ? OrangeText : Blue, fontSize: 6.75f, bold: true));
                    });
                    c.Item().PaddingTop(C(1, 0)).Text(item.Subtitle).FontSize(C(8.25f, 7.9f)).FontColor(Muted);
                });

                if (isFlight)
                {
                    row.AutoItem().AlignMiddle().Width(13.5f).Height(13.5f)
                        .Svg(Plane("#EB6334", item.Kind == VoucherTimelineKind.Arrival ? 135 : 45));
                }
                else if (_compact)
                {
                    // Side by side instead of stacked: one line shorter per stay.
                    row.AutoItem().AlignMiddle().PaddingRight(8).Text(item.Range!).FontSize(7.9f).FontColor(Muted);
                    row.AutoItem().AlignMiddle().Element(Pill(item.NightsLabel!, BlueSoft, BlueDeep));
                }
                else
                {
                    row.AutoItem().AlignMiddle().Column(c =>
                    {
                        c.Item().AlignRight().Element(Pill(item.NightsLabel!, BlueSoft, BlueDeep));
                        c.Item().PaddingTop(2).AlignRight().Text(item.Range!).FontSize(7.9f).FontColor(Muted);
                    });
                }
            });
        };

        // ── Travellers ──────────────────────────────────────────────────────────────────────────────

        private void Travellers(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Element(SectionHeader(_m.Text.Travellers, _m.PaxSummary, C(9, 6)));

                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });

                    for (var i = 0; i < _m.Guests.Count; i++)
                    {
                        var guest = _m.Guests[i];
                        var column = i % 3;
                        table.Cell()
                            .PaddingLeft(column == 0 ? 0 : 4.5f).PaddingRight(column == 2 ? 0 : 4.5f).PaddingBottom(C(9, 7))
                            .ShowEntire()
                            .Element(GuestCard(guest));
                    }
                });
            });
        }

        private Action<IContainer> GuestCard(VoucherGuest guest) => container =>
        {
            container.Layers(layers =>
            {
                layers.PrimaryLayer()
                    .Background(Colors.White).CornerRadius(12).Border(0.75f, Line)
                    .Shadow(new BoxShadowStyle { Color = Color.FromHex("#1F141A33"), Blur = 12, OffsetY = 4, Spread = -6 })
                    .PaddingVertical(C(9, 6)).PaddingHorizontal(10.5f)
                    .Row(row =>
                    {
                        row.Spacing(9);
                        row.ConstantItem(C(28.5f, 24)).Height(C(28.5f, 24)).CornerRadius(C(9, 8))
                            .BackgroundLinearGradient(45, guest.IsLead
                                ? new[] { Color.FromHex("#F4A04A"), Orange }
                                : new[] { Color.FromHex("#6F8AD8"), Blue })
                            .AlignCenter().AlignMiddle()
                            .Text(guest.Initials).FontSize(9.75f).SemiBold().FontColor(Colors.White);
                        row.RelativeItem().AlignMiddle().Column(c =>
                        {
                            // Never clamped: the hotel matches the full name against a passport.
                            c.Item().Text(guest.Name).FontSize(9.75f).SemiBold().LineHeight(1.15f);
                            c.Item().Text(guest.Type).FontSize(7.9f).FontColor(Muted);
                        });
                    });

                if (guest.IsLead)
                    layers.Layer().AlignRight().PaddingRight(9).OffsetY(-6)
                        .Height(12).Background(Orange).CornerRadius(6).PaddingHorizontal(6.75f).AlignMiddle()
                        .Text(_m.Text.LeadGuest).FontSize(6.4f).Bold().LetterSpacing(0.1f).FontColor(Colors.White);
            });
        };

        // ── Footer ──────────────────────────────────────────────────────────────────────────────────

        private void Footer(IContainer container)
        {
            container.Background(BlueDeep).CornerRadius(12).PaddingVertical(C(10.5f, 9)).PaddingHorizontal(13.5f).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(c =>
                {
                    c.Item().Text(_m.Text.PresentAtCheckIn).FontSize(9).Bold().FontColor(Colors.White);
                    c.Item().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Color.FromHex("#C9D1EE")));
                        text.Span($"{_m.BookingCode} · {string.Format(_m.Text.IssuedOn, _m.IssuedOn)} · ");
                        text.CurrentPageNumber();
                        text.Span("/");
                        text.TotalPages();
                    });
                });

                row.AutoItem().AlignMiddle().Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(_m.ContactEmail))
                        c.Item().AlignRight().Text(_m.ContactEmail).FontSize(9).Bold().FontColor(Colors.White);
                    if (_m.Instagram.Count > 0)
                        c.Item().AlignRight().Row(ig =>
                        {
                            ig.Spacing(4);
                            ig.AutoItem().AlignMiddle().Width(8).Height(8).Svg(InstagramGlyph);
                            ig.AutoItem().AlignMiddle().Text(string.Join("  ·  ", _m.Instagram.Select(h => "@" + h.TrimStart('@'))))
                                .FontSize(7.5f).FontColor(Color.FromHex("#C9D1EE"));
                        });
                });
            });
        }

        // ── Small building blocks ───────────────────────────────────────────────────────────────────

        private static Action<IContainer> Label(string text) => container =>
            container.Text(text.ToUpper(CultureInfo.InvariantCulture)).FontSize(6.75f).SemiBold().LetterSpacing(0.12f).FontColor(Muted);

        private static Action<IContainer> Pill(string text, Color background, Color color, float fontSize = 8.25f, bool bold = false) =>
            container =>
            {
                var t = container.Background(background).CornerRadius(20)
                    .PaddingVertical(bold ? 1.5f : 3).PaddingHorizontal(bold ? 6 : 9)
                    .Text(text).FontSize(fontSize).FontColor(color);
                if (bold) t.Bold().LetterSpacing(0.06f); else t.SemiBold();
            };

        /// <summary>"5 ★" — the star is drawn, because Poppins has no U+2605 glyph.</summary>
        private static Action<IContainer> StarPill(int stars) => container =>
            container.Background(OrangeSoft).CornerRadius(20).PaddingVertical(3).PaddingHorizontal(9).Row(row =>
            {
                row.Spacing(3);
                row.AutoItem().AlignMiddle().Text(stars.ToString(CultureInfo.InvariantCulture)).FontSize(8.25f).SemiBold().FontColor(OrangeText);
                row.AutoItem().AlignMiddle().Width(8).Height(8).Svg(StarGlyph);
            });

        private const string StarGlyph =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M12 2.5l2.9 6.1 6.6.8-4.9 4.6 1.3 6.6L12 17.3l-5.9 3.3 1.3-6.6-4.9-4.6 6.6-.8z\" fill=\"#C4481D\"/></svg>";

        private static Action<IContainer> SectionHeader(string title, string aside, float gapBelow) => container =>
            container.PaddingBottom(gapBelow).Row(row =>
            {
                row.Spacing(7.5f);
                row.ConstantItem(3).Height(12).CornerRadius(1.5f).BackgroundLinearGradient(90, new[] { Orange, Gold });
                row.AutoItem().AlignMiddle().Text(title).FontSize(10.5f).SemiBold();
                row.RelativeItem().AlignRight().AlignMiddle().Text(aside).FontSize(7.9f).FontColor(Muted);
            });

        private static string Plane(string hex, int rotation) =>
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"{PlanePath}\" fill=\"{hex}\" transform=\"rotate({rotation} 12 12)\"/></svg>";

        /// <summary>
        /// The timeline's node column for one row: a connector above and below (none above the first, none
        /// below the last) and the dot, centred on the card — which sits above the row's gap.
        /// </summary>
        private string Node(float w, float h, bool isFlight, bool first, bool last)
        {
            var cx = w / 2;
            var cy = (h - RowGap) / 2;
            var color = isFlight ? "#EB6334" : "#4A6AC3";
            var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(w)}\" height=\"{N(h)}\" viewBox=\"0 0 {N(w)} {N(h)}\">";
            if (!first) svg += $"<line x1=\"{N(cx)}\" y1=\"0\" x2=\"{N(cx)}\" y2=\"{N(cy)}\" stroke=\"#4A6AC3\" stroke-opacity=\"0.3\" stroke-width=\"1.5\"/>";
            if (!last) svg += $"<line x1=\"{N(cx)}\" y1=\"{N(cy)}\" x2=\"{N(cx)}\" y2=\"{N(h)}\" stroke=\"#4A6AC3\" stroke-opacity=\"0.3\" stroke-width=\"1.5\"/>";
            svg += $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"7.5\" fill=\"#fff\"/>";
            svg += $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"4.5\" fill=\"#fff\" stroke=\"{color}\" stroke-width=\"3\"/>";
            return svg + "</svg>";
        }

        private const string InstagramGlyph =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><rect x=\"2.5\" y=\"2.5\" width=\"19\" height=\"19\" rx=\"5.5\" fill=\"none\" stroke=\"#C9D1EE\" stroke-width=\"2.2\"/>" +
            "<circle cx=\"12\" cy=\"12\" r=\"4.3\" fill=\"none\" stroke=\"#C9D1EE\" stroke-width=\"2.2\"/><circle cx=\"17.4\" cy=\"6.6\" r=\"1.3\" fill=\"#C9D1EE\"/></svg>";

        /// <summary>SVG numbers must use "." — a pt-BR server culture would write "12,5" and break the path.</summary>
        private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
