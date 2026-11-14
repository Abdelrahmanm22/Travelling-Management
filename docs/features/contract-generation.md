# Feature spec — Contract Generation (العقد)

> **For Claude Code:** this is an agreed design, drawn out with the user one question at a time on
> 2026-11-12. The decisions below are **settled — don't re-ask them.** Read `CLAUDE.md` first
> (architecture, gotchas, UI rules) and follow it everywhere this spec is silent. Ask the user only
> about something this spec genuinely doesn't cover — §13 lists the points still open. Work on a
> `feature/contract-generation` branch cut from `develop`.

---

## 1. Goal

Today every client contract (*Contrato de Prestação de Serviços de Turismo*) is written by hand in
Word, from one `.docx` per package. The system should **generate it from the booking**, let the
operator **review and edit everything** on a page with a live preview, and produce a **PDF**.

Reference samples (the user's real contracts, read 2026-11-12):

| File | Package | Year |
|---|---|---|
| `CCH - CAIRO + CRUZEIRO + HURGHADA - CONTRATO - MUSTA TRAVEL 2026.docx` | Cairo + Cruise + Hurghada, 10 days / 9 nights | 2026 |
| `CCS - CAIRO + CRUZEIRO + SHARM - CONTRATO - MUSTA TRAVEL 2026.docx` | Cairo + Cruise + Sharm, 11 days / 10 nights | 2026 |
| `CLH - CAIRO + LUXOR + HURGHADA - MUSTA TRAVEL 2025.docx` | Cairo + Luxor + Hurghada, 8 days | 2025 |
| `CAIRO 5 dias - CONTRATO - MUSTA TRAVEL 2025.docx` | Cairo, 5 days / 4 nights | 2025 |

They are not in the repo. Ask the user for them if you need the exact wording.

### Anatomy of a contract (what the samples have in common)

| # | Section | Varies by | Source in this design |
|---|---|---|---|
| 1 | Title + opening paragraph: CONTRATANTE (name, **CPF, RG, address**) + CONTRATADA (Musta Travel Viagem Ltda, address, CNPJ) | client | **Contract terms** (text, with placeholders) + typed on the page |
| 2 | *Nome / Dados do Passageiro* | booking | Booking guests |
| 3 | *Forma de pagamento*: `Roteiro X — US$… por pessoa`, deposit, balance | booking | Booking pricing + transactions |
| 4 | `GRUPO {MÊS}/{ANO}` | booking | `ArrivalDate` |
| 5 | Clause 1.0 (object of the contract) | — | Contract terms |
| 6 | 1.1 *Cronograma*: `VIAGEM EGITO: {title} {D} dias / {N} noites`, then *Programação {destination}* headings and *Dia 01 – …* blocks | package + booking | **Package program** + booking stays |
| 7 | *Pacote Inclui* / *Não está incluso* | package + booking | Package program + `VisaIncluded` + extra tours |
| 8 | *Hospedagem*: `Cairo: Hotel 4 estrelas com café da manhã` | booking | Destination text + booking category + `HotelName` |
| 9 | *Importante* (tips, safety) and *Documentos Requeridos* | package | Package program |
| 10 | Clauses 1.3 → 7, the double-room clause, `São Paulo, {date}`, signature lines | — | Contract terms |

The legal clauses (1.3 → 7 + *Cláusula adicional – acomodação em quartos duplos*) are identical across
all four samples apart from typos. The 2025 samples also carry a **clause 1.2** (late payment: 10% fine,
3% monthly interest, IGPM) that the 2026 ones dropped.

---

## 2. Settled decisions (the user's calls)

| # | Decision | Rejected alternatives |
|---|---|---|
| 1 | The day-by-day program is stored **per package** | Per destination with a day library; per destination only. Rejected because the same destination's day has different text per package: Cairo day 3 is "Museum + Citadel" (Cairo 5 days), "city tour with luggage, then fly to Aswan" (CCH) and "Museum of Civilization + city tour" (CLH) |
| 2 | When a booking's leg is **longer** than the program was written for, **free days are inserted automatically** (default text per destination), placed **before the leg's departure day**, and everything is renumbered, including `D dias / N noites` | Copy the package as-is and fix it by hand; dates only |
| 3 | CONTRATANTE **CPF, RG, address are typed on the contract page each time and NEVER stored in the database** — not on the booking, not in the draft | New booking fields; per-guest fields |
| 4 | Output is a **PDF** (QuestPDF, like the voucher) | Word `.docx`; both |
| 5 | Editing happens on a **full page with a live PDF preview** | Large modal; one free-text editor |
| 6 | Legal clauses live on a **Contract terms** page in Setup, edited there once, **read-only on the contract page** | Editable per contract; hard-coded |
| 7 | Payment = the booking's pricing lines → `TOTAL = GrandTotal`; **Entrada = what has actually been paid** (`PaidTotal`); **Saldo devedor = `Balance`** | Fixed 30% deposit; per-person rate only |
| 8 | After generating, **the edits are saved as a draft** on the booking (minus the personal fields) **and the PDF is saved to the booking** | Download only |
| 9 | Hospedagem = destination's accommodation text + booking category **+ the stay's `HotelName` when recorded** | Category only; free text per package |
| 10 | *Importante* and *Documentos Requeridos* are **per package** (like the lists). The number of guide days in the tips text is just written into each package's text | Global text with a `{GuideDays}` placeholder; global fixed text |
| 11 | The terms start from the **2026 text, without clause 1.2**. It can be added back from Setup later | 2026 + restore 1.2 |
| 12 | Booking data adjusts the lists **automatically**: `VisaIncluded` moves the visa line from *Não Inclui* to *Inclui*; every extra tour bought is added to *Inclui*. All still editable on the page | Tours only; no automation |
| 13 | *Nome / Dados do Passageiro* lists **every guest with their type** (Adulto / Criança / Bebê) | Lead guest only; names only |
| 14 | PDF looks **like the current Word file, cleaner**: a formal legal document, logo on top, bold clause headings, numbered days, signature lines, page numbers | The voucher's Desert Daylight look |
| 15 | Regenerating **replaces** the saved PDF (one per booking). The audit trail records every generation | Keep every version |
| 16 | **Two new permission areas:** `Bookings.Contract` (open the page + generate) and a `ContractTerms` module (View / Edit). The package program rides on `Packages.Edit`. **Cancelled bookings can't generate** | Reuse `Bookings.Voucher` |
| 17 | Package programs are **filled by hand** in Setup — no import, no seed, no "fill from template" | Template picker; seed by package name |
| 20 | **An unpriced booking can't generate a contract.** The page, the preview and Save draft still work; **Generate** is refused until `Booking.IsPriced` (§5.3) | Generate with a total typed on the page |
| 19 | The contract is **Portuguese only**: no language picker, no `VoucherLanguage`. Every generated word (labels, months, guest types, `Dia`, `noites`, `Página X de Y`) is pt-BR, and the Setup texts are typed in Portuguese | EN + PT like the voucher |
| 18 | A draft older than a booking change is **kept as edited**, with a warning naming the sections that differ from a fresh build and a per-section **Refresh from booking** button (§6.4) | Always rebuild payment and passengers live, keeping only the text sections from the draft |

**Consequence the user accepted:** the saved PDF *contains* the CPF/RG/address. The database never
does, but the file on disk does. Keep both out of logs, the audit trail and the draft JSON.

---

## 3. Data model

All entities inherit `BaseModel`. Configs in `Travel.Repository/Data/Configrations/`.

### 3.1 `Destination` — new columns (Setup ▸ Destinations form)

| Field | Notes |
|---|---|
| `string? ContractAccommodationText` | max 200, e.g. `Hotel {Category} com café da manhã`, `Resort {Category} all inclusive`, `Cruzeiro {Category}, pensão completa`. `{Category}` → `4 estrelas` / `5 estrelas`. Null → the destination isn't listed under Hospedagem (and the page warns) |
| `string? ContractFreeDayTitle` | max 150, e.g. `Hurghada – dia livre` |
| `string? ContractFreeDayText` | max 2000, e.g. `Café da manhã e dia livre no resort.` |

Null free-day text falls back to `{Destination} – dia livre` / empty body.

### 3.2 Package program — new tables (Setup ▸ Packages ▸ a **Contract program** section/tab on Edit)

**`Package`** gets:
| Field | Notes |
|---|---|
| `string? ContractTitle` | max 150, the Portuguese name: `Cairo + Cruzeiro + Hurghada`. Feeds `VIAGEM EGITO: {title}` and `Roteiro {TITLE}` (upper-cased). Null → `Package.Name` |
| `string? ContractImportantText` | nvarchar(max), the *Importante* section |
| `string? ContractDocumentsText` | nvarchar(max), the *Documentos Requeridos* section |

**`PackageContractLeg`** — one row per leg of the package (see §5.1 for what a leg is):
| Field | Notes |
|---|---|
| `PackageId` | FK Cascade |
| `int Leg` | leg index (§5.1) |
| `int WrittenForNights` | how many nights this leg's days were written for. Defaults from the package's generated schedule; editable (for cruise packages the lead leg varies by sailing) |
| `string? Heading` | `Programação Cairo` / `Programação Cruzeiro`. Null → `Programação {Destination}` |

Unique index `IX_PackageContractLegs_Package_Leg` (**no `_Code` in the name**).

**`PackageContractDay`**:
| Field | Notes |
|---|---|
| `PackageId` | FK Cascade |
| `int Leg` | which leg the day belongs to, or the special **Final** leg (the last day of the trip) |
| `int SortOrder` | order within the program |
| `string Title` | max 200, without the number: `Chegada ao Cairo`. The number `Dia 01 –` is generated |
| `string Body` | nvarchar(max), plain multi-line text (paragraphs separated by blank lines; a line starting `* ` renders as a note, `Observação:` lines kept as typed) |
| `bool IsDepartureDay` | the day the guest **leaves** this leg's destination. Inserted free days go **before** it (§5.2) |

**`PackageContractListItem`**:
| Field | Notes |
|---|---|
| `PackageId` | FK Cascade |
| `ContractListKind Kind` | new enum `Includes = 1`, `Excludes = 2` |
| `int SortOrder` | |
| `string Text` | max 500 |
| `bool IsVisaLine` | at most one per package, only on `Excludes`. Moved to *Inclui* when `Booking.VisaIncluded` (§5.4) |

**Package delete**: the program is owned (Cascade). **Package copy-to-new** is out of scope.

### 3.3 Contract terms — `ContractTermsSection` (Setup ▸ Contract Terms)

An ordered list of sections, one table, no versioning:

| Field | Notes |
|---|---|
| `ContractTermsPlacement Placement` | new enum: `Opening = 1` (title + party paragraph, before the passengers), `ObjectClause = 2` (clause 1.0, before the schedule), `Closing = 3` (1.3 → 7 + the double-room clause, after Documentos) |
| `int SortOrder` | |
| `string? Heading` | `CLÁUSULA SEGUNDA - OBRIGAÇÕES DA CONTRATADA`; null = a continuation paragraph |
| `string Body` | nvarchar(max) |

Plus three single-value settings on the same page — store them as `ContractTermsSection` rows is
awkward, so use **a one-row `ContractSettings` table**: `SignatureCity` (`São Paulo`), `CompanySignatureName`
(`MUSTA TRAVEL`), `DocumentTitle` (`CONTRATO DE PRESTAÇÃO DE SERVIÇOS DE TURISMO MUSTA TRAVEL`).

**Placeholders** allowed in terms bodies (replaced at generation, unknown ones left visible so a typo
shows): `{Contratante}`, `{CPF}`, `{RG}`, `{Endereco}`. Nothing else — the legal text is otherwise
literal.

**Seed (the one exception to decision 17):** the migration — or a startup seed guarded on "table
empty" — inserts the **2026 CCH terms text verbatim, without clause 1.2**, so Contract Terms isn't
blank on day one. This is the company's standard legal text, not a package program, and doesn't depend
on package codes. Confirm with the user if in doubt; the CCH text is the source.

### 3.4 Per-booking — `BookingContract` (draft + generation record)

| Field | Notes |
|---|---|
| `BookingId` | FK **Cascade**, unique index `IX_BookingContracts_Booking` |
| `string DraftJson` | nvarchar(max), the serialised `ContractDocument` (§4) **without** `{CPF, RG, Endereco}` |
| `DateTime UpdatedAt` | |
| `DateTime? GeneratedAt`, `string? GeneratedById`, `string? GeneratedByName` | last generation (plain string + name snapshot, the `BookingTransaction.CreatedById` pattern — no FK to `AspNetUsers`) |

**The draft DTO type must not have properties for CPF/RG/address at all** — enforce it by type, not by
remembering to null them before serialising.

### 3.5 The saved PDF — a new attachment category

`AttachmentCategory.GeneratedContract = 5`, stored as a **`BookingAttachment` file row**
(`FileUrl` + `OriginalFileName = Contrato_{Code}.pdf`, folder `BookingAttachments`), **at most one per
booking**.

| Why not reuse the `Contract` row | `BookingController.Edit` **deletes the Contract row when the link box is cleared** (`BookingController.cs` ~line 555). A PDF on that row would be lost — and its file orphaned — by an unrelated edit |
|---|---|

- Regenerating deletes the old row **and its file on disk** (after the new save succeeds), then adds the new one.
- `BookingController.Delete` already loops `booking.Attachments` deleting files — covered.
- **Exclude it from the Edit form's removable attachments list** (the `.Where(a => a.Category != AttachmentCategory.Contract)` near line 1082) — it is replaced only by regeneration.
- Value **3 stays retired** (`CLAUDE.md`).

### 3.6 Audit

`AuditAction.ContractGenerated = 6`, written by the contract controller (not the interceptor), the
`VoucherSent` pattern. Summary `Contract generated`; **no detail rows carrying CPF/RG/address**.
`_BookingAuditTrail` renders it like the voucher events.

### 3.7 Migration

`AddContractGeneration`. Read the generated SQL (`dotnet ef migrations script`) before `database update`:
the enum-default trap applies to `Placement` and `Kind` (scaffolder emits `0`, hand-edit to `1` if it
adds a default to an existing table — these are new tables, so it shouldn't).

---

## 4. The contract document model

One DTO, **`ContractDocument`** (in `Travel.Presentation/Services/Contracts/` — it's rendered by
QuestPDF there), is what the page edits, the draft stores and the PDF renders. The **same object**
drives the preview and the final PDF, so they can't drift.

```
ContractDocument
  Party            { Name }                              // + CPF, RG, Address on a separate, never-persisted ContractParty
  Passengers       [ { Name, TypeLabel } ]
  Payment          { Title, Lines[ {Label, Amount} ], Total?, Paid, Balance?, ExtraText? }
  GroupLabel       "AGOSTO/2026"
  TripTitle        "Cairo + Cruzeiro + Hurghada"
  Days / Nights    computed from Schedule (shown, not typed)
  Schedule         [ Section { Heading, Days[ { Title, Body, IsInserted } ] } ]
  Includes         [ string ]
  Excludes         [ string ]
  Accommodation    [ { Destination, Text } ]
  Important        string
  Documents        string
  Date             DateOnly (today, editable)
  Warnings         [ string ]   // not persisted, not printed — see §6.3
```

The legal sections are **not** in the DTO: they are read live from `ContractTermsSection` at render
time, so the page can't change them (decision 6). Consequence: a terms edit in Setup reaches the next
generation of every draft — intended.

**Money formatting is pt-BR**: `US$1.950,00`. The app's `Money.Format` is `InvariantCulture`
(`1,950.00`), so add a pt-BR formatter next to `VoucherText`'s pt-BR culture rather than changing
`Money`.

---

## 5. Building a fresh document (`ContractBuilder`, one pure owner)

`IContractBuilder.BuildAsync(bookingId)` → `ContractDocument`. Pure rules in one class (the
`BlockSnapshot` / `MovementPresence` pattern) so the page, the preview and any test agree.

### 5.1 Legs

A package's generated schedule (`ItineraryGeneratorService`) is: **Lead** stay at the base destination
(may be 0 nights) → one stay per `PackageSegment` in order → **Return** stay at the base (may be 0
nights). The program's legs mirror that:

| Leg index | Meaning |
|---|---|
| `0` | Lead (base destination) |
| `1 … n` | `Segments[SortOrder]` |
| `n + 1` | Return (base destination) |
| `Final` (`-1` or a constant) | the last day of the trip (`Dia 10 – Cairo / País de Origem`) |

The Setup editor shows the legs as groups (`1. Cairo · 2. Cruzeiro · 3. Hurghada · 4. Cairo (retorno) ·
Último dia`), so the operator never types an index.

**Matching booking stays to legs**: walk the booking's stays (`SortOrder`) and the package legs in
order, aligning on `DestinationId` (skip a 0-night leg the booking doesn't have). An unmatched
**stay** (operator hand-added) → all its days are free days of that destination + a warning. An
unmatched **leg** → its days are dropped + a warning.

### 5.2 Day count per leg — the free-day rule (decision 2)

For each matched leg: `delta = stay.Nights − leg.WrittenForNights`.

- `delta > 0` → insert `delta` free days (`Destination.ContractFreeDay*`) **immediately before the
  leg's first `IsDepartureDay` day**, or at the end of the leg if it has none. Mark them `IsInserted`
  (the page highlights them).
- `delta < 0` → remove `|delta|` days from the end of the leg's **non-departure** days + a warning
  ("Hurghada: 1 day removed — the booking has fewer nights than the program").
- Then the **Final** leg's days.
- Renumber `Dia 01 …` across the whole trip. `D dias / N noites` = `N = Σ stay.Nights`, `D = N + 1`.

Example (CCH, written Hurghada = 2 nights; booking has 4):
```
Dia 07 – Luxor | Hurghada                ← program
Dia 08 – Hurghada (Mar Vermelho)         ← program
Dia 09 – Hurghada – dia livre            ← inserted
Dia 10 – Hurghada – dia livre            ← inserted
Dia 11 – Hurghada                        ← program, IsDepartureDay (flight to Cairo later)
...
```

Days are **not dated** in the contract (the samples aren't). The page may show the date next to each
day as a hint for the operator, not printed.

### 5.3 Payment (decision 7)

```
Roteiro {TRIPTITLE}
  {n} adulto(s)   × US${AdultPrice}   = …
  {n} criança(s)  × US${ChildPrice}   = …
  {n} bebê(s)     × US${InfantPrice}  = …        (only the types with count > 0)
  Noites extras                         = ExtraNightsTotal   (only when > 0)
  Passeios extras                       = ToursTotal          (only when > 0)
TOTAL: GrandTotal
Entrada (pago): PaidTotal
Saldo devedor: Balance
```

- An **unpriced** booking (`!Booking.IsPriced`, so `GrandTotal` is null) **can't be generated**
  (decision 20). Total and Saldo render blank in the preview (**never `US$0,00`**: 0 means free,
  `CLAUDE.md` Money Model) and a red banner reads *"This booking isn't fully priced. Price it on the
  booking before generating the contract."* with a link to `Booking/Edit` when the user holds
  `Bookings.EditPricing`. The Generate button is disabled, **and** the `Generate` endpoint re-checks
  `IsPriced` on a freshly loaded booking and refuses (`{ success: false, message }`). The client-side
  disable is a convenience only. `IsPriced` covers unpriced extra nights too, so a stay with chargeable
  extra nights and no room rates blocks generation as well.
- The payment amounts stay editable on the page once the booking is priced (decision 7). Editing them
  doesn't change the booking.
- The builder's spec **must include** what `GrandTotal`/`PaidTotal` read: `Stays.Rates`, `Tours`,
  `Transactions` — `BookingWithDetailsSpec` already does. A missing include makes it silently too low.
- The contract page is gated on `Bookings.Contract`, **not** `ViewPricing`: the contract inherently
  shows the price (the Revenue report precedent). Write that in the catalog description.

### 5.4 Lists (decision 12)

Start from the package's `Includes` / `Excludes` in order, then:
- `VisaIncluded` → move the `IsVisaLine` item to the end of *Inclui* as `Visto de entrada no Egito`.
  If the package has no visa line, add that line to *Inclui* and warn.
- For each `BookingTour` → append `Passeio extra: {Tour.Name} ({Pax} pax)` to *Inclui*.

### 5.5 The rest

- **Passengers** (decision 13): every `BookingGuest` in order, `Adulto / Criança / Bebê` from `GuestType`.
- **Party name**: defaults to the lead guest, editable.
- **GroupLabel**: `ArrivalDate` → `AGOSTO/2026` (pt-BR month, upper case).
- **Accommodation** (decision 9): one line per distinct destination on the itinerary, in order:
  `{Destination}: {ContractAccommodationText with {Category}}` + ` ({HotelName})` when any of that
  destination's stays has one (several distinct names → join with ` / `). A destination with no
  accommodation text is omitted + warning.
- **Important / Documents**: the package's text.
- **Date**: today in Cairo time (`Africa/Cairo`), editable.

---

## 6. The contract page — `BookingContract/Edit/{bookingId}`

Entry point: a **Contract** button on `Booking/Details` (gated `Bookings.Contract`; disabled with an
inline note when the booking is Cancelled, like the voucher). Beside it: **Download contract** when a
`GeneratedContract` attachment exists, and "Last generated {date} by {name}".

### 6.1 Layout

Two columns on desktop (`col-xl-6` / `col-xl-6`), stacked on phones (preview below, collapsible).
**Check 375 px** — no sideways scroll.

Left — collapsible cards, in contract order:
1. **Contracting party** — Name, CPF, RG, Address. A muted line: *"CPF, RG and address are not saved —
   type them each time."* Fields are plain inputs, **never** `asp-for`-bound to anything persisted.
2. **Passengers** — editable list (add / remove / reorder).
3. **Payment** — the lines, total, paid, balance, all editable; a free-text line for extra terms.
4. **Trip** — group label, trip title.
5. **Schedule** — sections → days. Each day: title, body (textarea), drag handle, delete. `+ Add day`
   per section. Inserted free days carry a muted `added — extra night` badge. Day numbers renumber
   live.
6. **Includes / Not included** — two editable lists.
7. **Accommodation** — editable lines.
8. **Important**, **Documents** — textareas.
9. **Legal terms** — read-only, collapsed, with a link to Setup ▸ Contract Terms for holders of
   `ContractTerms.Edit`.

Right — **live PDF preview**: an `<iframe>` showing the PDF rendered from the current form state,
refreshed **debounced (~800 ms) after the last change** via `POST BookingContract/Preview` (returns
`application/pdf`). Render cost is the voucher's (QuestPDF, no Chromium) — fine for the 1-vCPU VPS at
this rate; don't refresh per keystroke.

Bottom bar (sticky): **Save draft** · **Rebuild from booking** (confirm: discards the draft) ·
**Generate PDF** (primary).

### 6.2 Behaviour

- **Open**: if a `BookingContract` draft exists → load it; else build fresh (§5). Party CPF/RG/address
  always start empty.
- **Save draft**: persists the document (minus party PII). No PDF.
- **Generate PDF**: refuses an unpriced or cancelled booking (re-read from the database, §5.3),
  validates (party name, CPF, RG, address required; at least one day), renders,
  saves draft + replaces the `GeneratedContract` attachment + writes the audit event — **one transaction**
  for the rows, file deleted only after commit — then returns the PDF as a download.
- **Rebuild from booking**: discards the draft, rebuilds fresh.
- Forms post as one JSON body (the `InternalMovement/Save` pattern: `[FromBody]`, antiforgery token
  in the `RequestVerificationToken` header). Errors come back as `{ success, message, errors[] }` and
  render **inside the page next to their field** — no invisible rules (`CLAUDE.md` 2026-08-02).

### 6.3 Warnings (shown on the page, never printed)

- The package has **no contract program** → *"This package has no contract program yet — fill it in
  Setup ▸ Packages."* with a link (if `Packages.Edit`). Generation is **allowed** but the schedule is
  empty; the operator can type days by hand.
- Leg mismatches and removed days (§5.1, §5.2).
- Unpriced booking (§5.3): **red, and it blocks Generate**. All the other warnings are amber and
  block nothing.
- Destination without accommodation text (§5.5).
- Package without a visa line while `VisaIncluded` (§5.4).
- **The draft is out of date with the booking** — see §6.4.

### 6.4 A draft that no longer matches the booking (decision 18)

On open, when a draft exists, also build a fresh document (§5) and compare **section by section**.
Only the sections the booking drives are compared; text the operator wrote is never second-guessed:

| Section | Differs when |
|---|---|
| Passengers | the list of (name, type) differs |
| Payment | any amount (lines, total, paid, balance) differs |
| Schedule | the per-leg night counts (`D dias / N noites`, inserted free days) differ — **not** the day texts |
| Includes / Not included | the automatic items (visa line, `Passeio extra:` lines) differ |
| Accommodation | the lines differ |

Each differing section shows an amber banner inside its card — *"The booking changed since this draft
was saved: payment"* — with a **Refresh from booking** button that replaces **only that section** with
the fresh build (confirm first; no undo). A summary line at the top lists all of them. Nothing is
refreshed automatically, and **Generate** is not blocked: the operator may have edited the section on
purpose.

Compare the fresh build against the draft's **stored** values, not against a timestamp: a booking save
that changed nothing contract-relevant (notes, a flight number) must not raise the warning. This is
the `BlockSnapshot` "fingerprint the facts, don't timestamp the row" rule.

---

## 7. Setup screens

### 7.1 Package ▸ Contract program
On `Package/Edit` (a separate card/tab, saved by its own POST so the existing package form is
untouched): contract title; legs with `WrittenForNights` + heading + days (title, body, departure
toggle, reorder, add/remove); *Inclui* / *Não Inclui* lists (visa-line radio on Excludes);
*Importante*; *Documentos*. Gated `Packages.Edit`; visible read-only with `Packages.View`.

Show a muted per-leg check: *"Cairo: 3 days written for 3 nights"* — a hint, never a block.

### 7.2 Destination form
Three new fields (§3.1) in a **Contract** group. The `{Category}` placeholder explained inline (no
tooltips).

### 7.3 Contract Terms (`ContractTerms/Index`)
The three settings + the sections grouped by placement, each editable (heading, body), reorderable,
add/remove. Placeholders listed inline. Gated `ContractTerms.View` / `.Edit`.

---

## 8. The PDF (`BookingContractPdf`, QuestPDF)

Decision 14 — **a formal legal document, like the Word file, cleaner**:
- A4, generous margins, Poppins (already registered by `VoucherAssets.RegisterFonts()`; QuestPDF
  2026.9 has no system-font fallback — `CLAUDE.md` voucher gotcha). Body ~10 pt, justified.
- Header on page 1: `voucher-logo.png` + `DocumentTitle` centered, small caps, bold.
- Clause headings bold; sub-clause numbers as written in the terms.
- Schedule: `VIAGEM EGITO: …` bold, `Programação X` sub-headings, `Dia 01 – Título` bold, body below.
- Lists as bullets. Hospedagem as `Destination:` bold + text.
- Closing: `{SignatureCity}, {dd/MM/yyyy}`, then two signature blocks (`CONTRATADA: MUSTA TRAVEL`,
  `CONTRATANTE: {name}`) with lines; `EnsureSpace` so a signature block never splits from its line.
- Footer: `Página X de Y` + booking code.
- **No glyph Poppins lacks** (★, ☎, ✓…) — the voucher gotcha. Check by rendering.
- Filename `Contrato_{Code}.pdf`.

---

## 9. Permissions

| Module | Permissions |
|---|---|
| `Bookings` (existing — append inside) | `Contract` — "Generate contract: open the contract page, preview and generate. **Shows the sale price** regardless of `ViewPricing`." |
| `ContractTerms` (new, **Order 17**, appended — nothing renumbers) | `View`, `Edit` |
| `Packages` (existing) | the program rides on `Edit` / `View` — no new permission |

**81 → 84.** Add `ContractTerms` to `PermissionIcons` (`bx-file`, verified present). Packing
(`PermissionClaims`) makes the count harmless.

---

## 10. Sidebar

| Title | New item | Icon (verified in `icons.min.css`) |
|---|---|---|
| Setup | Contract Terms | `bx-file` |

Add `ContractTerms.View` to the **Setup** title's `mode="any"` list. `ActiveItem("ContractTerms")`.
The contract page itself is reached from `Booking/Details` and highlights **Bookings**
(`BookingContract` controller → map it in `ActiveItem`).

---

## 11. Logging

`ILogger<T>`, per `CLAUDE.md` → Logging. **No CPF, RG, address, or guest names** — codes and ids only.
- `Contract generated for MUSB-000014` · `Contract draft saved for MUSB-000014`
- `Contract terms edited (+2 sections / -1)` · `Package MUSP-000003 contract program edited`
- Warning on refusals: cancelled booking, validation failure (the global `ModelStateLoggingFilter` won't
  see a JSON endpoint — log it yourself, **without the field values**).

---

## 12. Files (expected)

- **Domain:** `Models/PackageContractLeg.cs`, `PackageContractDay.cs`, `PackageContractListItem.cs`,
  `ContractTermsSection.cs`, `ContractSettings.cs`, `BookingContract.cs`; `Package`/`Destination` new
  columns; enums `ContractListKind`, `ContractTermsPlacement`, `AttachmentCategory.GeneratedContract = 5`,
  `AuditAction.ContractGenerated = 6`; permissions + catalog.
- **Repository:** configs, `DbSet`s, migration `AddContractGeneration` (+ the terms seed).
- **Presentation:** `Services/Contracts/` (`ContractDocument`, `ContractBuilder`, `BookingContractPdf`,
  pt-BR text helper), `BookingContractController` (`Edit`, `Preview`, `SaveDraft`, `Generate`,
  `Rebuild`, `Download`), `ContractTermsController`, a `PackageContractController` (or actions on
  `PackageController`), views `Views/BookingContract/`, `Views/ContractTerms/`, the package program
  partial, `wwwroot/back/assets/js/booking-contract.js` + `package-contract.js`, styles in
  `app-extras.css` (tokens only, light + dark).

> `ContractBuilder` lives in Presentation only because the document DTO is rendered there; its rules
> are pure. If a test project ever appears, it is the first thing to cover.

---

## 13. Open questions — ask the user before building these

1. ~~A stale draft~~ — **settled 2026-11-12**, see decision 18 and §6.4.
2. ~~Contract language~~ — **settled 2026-11-12**, see decision 19.
3. ~~Unpriced booking~~ — **settled 2026-11-12**, see decision 20.

No questions are open.

---

## 14. Traps from `CLAUDE.md` that apply here

- Run and migrate with `ASPNETCORE_ENVIRONMENT=Development` explicitly; never `--no-launch-profile`.
  Read the migration SQL first.
- Every validation error needs a visible surface **on the contract page**.
- Bootstrap **5.0.0-beta2**: no `fw-semibold`, no `getOrCreateInstance`, no tooltips. Grep classes and
  icons before using them.
- A missing include makes `GrandTotal` silently too small — use a spec that reaches `Stays.Rates`,
  `Tours`, `Transactions`.
- QuestPDF: every symbol must exist in Poppins; numbers interpolated into SVG must be culture-invariant.
- Deleting a booking must delete the generated PDF file too (covered by the attachments loop — verify).
- The `<permission>` tag helper is C#: rebuild after touching it.
- Don't leave a `dotnet run` holding port 5145.

---

## 15. Verification

Use real local data and put it back afterwards. Verify permissions with a **non-SuperAdmin**.
1. A CCH booking at the program's exact length: the PDF matches the CCH sample's structure (days,
   lists, hospedagem, terms).
2. The same booking with **+2 Hurghada nights**: two free days inserted before the departure day,
   renumbered, `D dias / N noites` updated.
3. A cruise booking whose sailing gives a **longer lead leg**: free Cairo day before Cairo's departure day.
4. `VisaIncluded` = Yes moves the visa line; two extra tours appear in *Inclui*.
5. Payment: a booking with adults + a child + extra nights + a tour + a payment shows each line, the
   right TOTAL (= `GrandTotal`), Entrada (= `PaidTotal`) and Saldo. An unpriced booking shows blanks +
   the red banner, never `US$0,00`. Generate is disabled, **and** a direct POST to `Generate` is
   refused. A booking with an unpriced extra night is blocked too.
6. **CPF/RG/address appear nowhere in the database or logs** — grep the `BookingContracts` rows, the
   audit tables and today's log file for a test CPF after generating.
7. Generate twice: one `GeneratedContract` row, the old file gone from disk. Clearing the contract
   **link** on Edit leaves the generated PDF in place.
8. Edit Contract Terms → the next generation shows the new text; the contract page can't change it.
9. Cancelled booking: button disabled, endpoints refuse.
10. A package with no program: the warning shows, generation still works with typed days.
11. 375 px: the page is usable, no sideways scroll; dark mode reads correctly.
12. Stale draft: save a draft, then change the booking's price → only **Payment** is flagged; Refresh
    replaces only that section. Edit only the booking's notes → **no** warning.

## 16. When done

Add a **Contract Generation** section to `CLAUDE.md` (decisions + why), list the new files in the File
Structure Reference, update the permission count (84), and record any new trap in *Gotchas & Past
Mistakes*.
