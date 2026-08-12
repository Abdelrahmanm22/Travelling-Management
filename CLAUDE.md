# CLAUDE.md — Travel Management SaaS

## Project Overview

A **.NET 9 MVC Web Application** (server-rendered Razor Views) — a **SaaS for travel companies** to manage their **internal operations/processes**.



**Solution:** `Travelling management.sln` · **Framework:** `net9.0` · **DB:** SQL Server (`TravelDB`) via EF Core 9
**Auth:** ASP.NET Identity (cookie-based, MVC — NOT JWT) · **Logging:** Serilog (console + daily rolling files in `Travel.Presentation/Logs/`) · **Mapping:** AutoMapper (registered — `AddAutoMapper(typeof(Program).Assembly)`) · **UI:** Skote Admin Template (Bootstrap 5, dark sidebar, Boxicons)
**UI Preference:** Client wants a modern/professional/premium UI/UX — use modern elements and creative touches.

---

## Architecture — Onion / Clean Architecture

```
Travel.Domain      (Core — zero project references)
    ├── Models/            (BaseModel; Models/Identity/AppUser)
    ├── Enums/             (Gender)
    ├── Repositories/      (IGenericRepository<T>)
    ├── Services/          (interfaces only — IFileStorageService)
    ├── Specifications/    (ISpecifications<T>, BaseSpecifications<T>)
    └── IUnitOfWork.cs

Travel.Repository  (Infrastructure — references Travel.Domain)
    ├── Data/TravelContext.cs              (IdentityDbContext<AppUser>)
    ├── Data/Configrations/                (Fluent API per entity — "Configrations" typo is established, keep it)
    ├── Data/AppIdentityDbContextSeed.cs   (seeds the first admin user)
    ├── Data/Migrations/                   (InitialMigration)
    ├── GenericRepository.cs
    ├── SpecificationEvalutor.cs           (typo intentional — keep it)
    ├── Storage/FileStorageService.cs      (IFileStorageService impl — disk-backed)
    └── UnitOfWork.cs

Travel.Services    (Business Logic — references Travel.Domain)
    ├── ItineraryGeneratorService.cs
    ├── CodeGeneration/    (CodeGeneratorService)
    └── Identity/          (RoleService, UserAccountService)

Travel.Presentation (MVC Web App — references Travel.Repository + Travel.Services)
    ├── Controllers/       (AccountController, HomeController)
    ├── ViewModels/        (LoginViewModel; ErrorViewModel [namespace Travel.Presentation.Models])
    ├── Views/             (Account/Login, Home/Index+Privacy, Shared layout partials)
    ├── Helpers/, MappingProfiles/ (empty — reserved)
    ├── wwwroot/           (Skote assets: css, js, lib, back/)
    ├── Program.cs         (startup, DI, pipeline)
    └── appsettings.json
```

### Dependency Flow

```
Travel.Presentation → Travel.Services   → Travel.Domain
Travel.Presentation → Travel.Repository → Travel.Domain
```

**Key rule:** `Travel.Domain` has ZERO project references. Its only NuGet is `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. Service **interfaces** live in `Travel.Domain/Services/`; **implementations** in `Travel.Services/` (or `Travel.Presentation` for web-coupled ones).

---

## NuGet Packages by Project

| Project | Key Packages |
|---|---|
| **Travel.Domain** | `Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.18` |
| **Travel.Repository** | `Microsoft.EntityFrameworkCore.SqlServer 9.0.18` (+ ref Travel.Domain) |
| **Travel.Services** | _(no NuGet — only ref Travel.Domain)_ |
| **Travel.Presentation** | `AutoMapper.Extensions.Microsoft.DependencyInjection 12.0.1`, `Serilog.AspNetCore 10.0.0`, `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation 9.0.18`, `Microsoft.EntityFrameworkCore.Tools 9.0.18`, `Microsoft.VisualStudio.Web.CodeGeneration.Design 9.0.12`, `ClosedXML 0.105.1` (Block Services .xlsx export) |

---

## Domain Models

- **`BaseModel`** — `int Id`. All DB-persisted entities inherit from it.
- **`AppUser`** : `IdentityUser` — `FirstName`, `LastName`, `FullName` (computed), `Gender` (enum), `IsActive` (default true), `ImageUrl?`. Configured in `AppUserConfig` (FirstName/LastName required max 100; ImageUrl max 500; unique index on UserName + Email).
- **`ICodedEntity`** (`Travel.Domain/Models/`) — marker interface, `string Code { get; set; }`, for entities carrying an auto-generated sequential code. Implemented by **`Booking`** and **`Package`** — see Code Generation.

---

## Enums

| Enum | Values | Location |
|---|---|---|
| `Gender` | `Male=1`, `Female=2` | `Travel.Domain/Enums/` |
| `BlockStatus` | `NotBooked=1`, `InProgress=2`, `Confirmed=3`, `NeedsAmendment=4` (**derived, never stored**) | `Travel.Domain/Enums/` |
| `BookingStatus` | `InProgress=1`, `Confirmed=2`, `Completed=3`, `Cancelled=4` | `Travel.Domain/Enums/` |

---

## Key Patterns

### Generic Repository + Specification

All data access goes through `IGenericRepository<T>` + `ISpecifications<T>`:

```csharp
var spec = new SomeSpecification(params);
var one  = await _unitOfWork.Repository<Entity>().GetByIdAsync(spec);
var many = await _unitOfWork.Repository<Entity>().GetAllAsync(spec);
```

**`IGenericRepository<T>`:** `GetAllAsync(spec)`, `GetByIdAsync(spec)`, `AddAsync(entity)`, `Update(entity)`, `Delete(entity)`.
**`ISpecifications<T>`:** `Criteria` (WHERE), `Includes` (eager-load expressions), `IncludeStrings` (ThenInclude chains, e.g. `"Categories.Items"`), `OrderBy`/`OrderByDesc`, `Skip`/`Take`/`IsPaginationEnabled`.
**`BaseSpecifications<T>`:** parameterless (get all) + criteria ctor; helpers `AddInclude(string)`, `SetOrderBy(...)`, `SetOrderByDesc(...)`, `ApplyPagination(skip, take)`. Name spec classes with the **`Spec`** suffix in per-entity folders under `Travel.Domain/Specifications/`.

### Unit of Work + Transactions

`_unitOfWork.Repository<Entity>()` (cached per entity in a `Hashtable`) · `await _unitOfWork.CompleteAsync()` (SaveChanges).

Wrap **multiple** DB operations that must be atomic in an explicit transaction:

```csharp
await _unitOfWork.BeginTransactionAsync();
try { /* ... multiple Repository + CompleteAsync calls ... */ await _unitOfWork.CommitTransactionAsync(); }
catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
```

Use a transaction when a method calls `CompleteAsync()` more than once (e.g. needs a DB-generated Id mid-flow) or writes two+ unrelated entities atomically. A single `CompleteAsync()` is already atomic — no transaction needed. `DisposeAsync` rolls back any open transaction to prevent connection leaks.

### SpecificationEvalutor

`Travel.Repository/SpecificationEvalutor.cs` (typo intentional) builds the EF query: Criteria → OrderBy → OrderByDesc → Pagination → Includes → IncludeStrings.

### Code Generation (`ICodeGeneratorService`)

Generates sequential unique codes for any entity implementing **`ICodedEntity`** (`string Code`). Entity-agnostic — the caller supplies the prefix.

| Entity | Prefix | Example |
|---|---|---|
| `Booking` | `MUSB` | `MUSB-000001` |
| `Package` | `MUSP` | `MUSP-000001` |

Prefixes are consts in **`Travel.Domain/Services/CodeGeneration/CodePrefixes.cs`**.

**Padding:** 6-digit zero-padded for 1–999,999; plain number above that (`MUSP-1000000`).

**How it reads the last number — three load-bearing constraints:**
1. **One row**, via `LastCodedEntitySpec<T>` + `GetByIdAsync` (`SELECT TOP 1`) — never `GetAllAsync` + a C# `Max`, which materializes the whole table (tracked, all columns) on every create.
2. **Ordered by `Id` desc, not `Code`** — codes are issued monotonically against the IDENTITY PK, so the newest row holds the highest number. Ordering by the Code *string* breaks at the padding boundary: `MUSB-1000000` sorts before `MUSB-999999`.
3. **One prefix per entity type** — what makes a single row sufficient. Two prefixes on one entity would need the max computed per prefix.

**The read-then-write race is handled by the database, not the generator.** Two admins creating at the same instant can read the same last row; the unique index rejects the loser. `UnitOfWork.CompleteAsync` translates SQL error 2601/2627 on an index whose name contains `_Code` into **`DuplicateCodeException`** (`Travel.Domain/Exceptions/`), and `AddWithCodeAsync` retries with a fresh code (3 attempts) before letting it surface. The index names are pinned via `HasDatabaseName("IX_Bookings_Code" / "IX_Packages_Code")` so the check can't confuse `IX_Packages_Name` for a code collision.

**Usage** — create paths only; the code is issued once and never reassigned:
```csharp
await _codes.AddWithCodeAsync(booking, CodePrefixes.Booking);   // AddAsync + CompleteAsync + retry
```
The whole entity graph (`Stays`/`Guests`/`Rooms`, `Segments`) saves in the same `SaveChanges`. **Adding a new coded entity:** implement `ICodedEntity`, add a `CodePrefixes` const, add the `Property`/`HasIndex` pair to its `Config` (index name must contain `_Code`), migrate, and call `AddWithCodeAsync` in the create action.

**Immutability** is enforced in the AutoMapper profiles: `.ForMember(d => d.Code, o => o.Ignore())` on the **ViewModel → Entity** map, so neither an edit nor a tampered POST can change an issued code. Views render it as static text, never an input.

---

## Block Services (derived read model — no table, no migration)

Operators block rooms **per property, per arrival date**, not per booking. `/BlockService` inverts the data: it groups **`BookingStay` rows by `DestinationId` + `CheckInDate`**. A `Cairo + Cruise + Hurghada` booking therefore appears in three different groups.

**Every group is a file, including a one-booking one** — the supplier needs the rooming list either way. The two-booking line is a **presentation split**, not an existence threshold:

| Page | Shows | Reached by |
|---|---|---|
| `/BlockService` | groups with **2+ bookings** | sidebar |
| `/BlockService/SingleArrivals` | groups with **exactly 1** | a dismissible alert on the Block Services list (no sidebar entry, by design) |

Both link to the **same** `Details` / `Export` — a single arrival gets an identical document, only the wording and filename differ (`Arrival_…` vs `Block_…`). `GetArrivalsAsync` returns both halves (`ArrivalGroups`) from one materialisation, so each page's list *and* the other page's alert count cost a single query.

**Nothing is persisted.** There is no `BlockService` entity, no code, no status, no workflow — every request re-derives from live booking data. A group's identity is its `(destinationId, checkIn)` pair, passed as **query-string** parameters (`DateOnly` in a route template is awkward); links emit `asp-route-checkIn="@x.ToString("yyyy-MM-dd")"`, which the default binder round-trips and `<input type="date">` posts natively.

| Decision | Why |
|---|---|
| Key is destination + check-in, **not hotel** | `BookingStay.HotelName` is optional free text; it renders as a column inside the file instead |
| `MinimumBookings = 2` splits the **two lists**, it doesn't gate existence | Originally it hid singletons entirely — wrong: every arrival needs a file sent, so they had no page and no export at all |
| Rooming comes from **`Booking.Rooms`** (trip-level) | There is no per-stay rooming; the same tally repeats in every group a booking joins |
| Grouping is **in-memory** | `BaseSpecifications` has no projection/GroupBy, and `Booking.Pax` / `LeadGuestName` are computed C# properties EF can't translate |
| `GetBlockAsync` returns **`null`** only on an **empty** group | A link to a destination + date no longer holding any booking 404s; one booking is a valid file |

`DistinctByBooking` keeps one stay per booking — guards a booking visiting the same destination twice on the same check-in, which would otherwise double-count its pax and rooms.

**Files:** DTOs + interface in `Travel.Domain/Services/BlockServices/`, `BlockStaysSpec` in `Travel.Domain/Specifications/BlockServices/`, impl in `Travel.Services/BlockServices/`, `BlockExcelExporter` in `Travel.Presentation/Services/` (ClosedXML lives in the web project so `Travel.Services` stays NuGet-free), views in `Views/BlockService/` (`Index`, `SingleArrivals`, `Details`, and the shared `_ArrivalFilters` partial — a `<form method="get">` with **no `asp-action`**, so it filters whichever list is showing).

**Export/print:** `Export` returns a real `.xlsx` (merged title, frozen header, guest names as indented sub-rows under their booking, bold TOTAL row). Permissions are `BlockServices.View` / `BlockServices.Export`. See Printable Documents & Excel Export.

**`Details` links each booking back to `/Booking/Details`** — the code badge and an "Open booking" button, both `target="_blank"` so the block file stays open in its own tab. Resolved **once**, via `IAuthorizationService.AuthorizeAsync(User, Permissions.Bookings.View)`, not the `<permission>` tag helper: a user holding only `BlockServices.View` (no `Bookings.View`) must still see the code badge, just as plain text instead of a link, and the tag helper only knows "render or don't" — not "render differently."

### Domestic flight documents (owned by the group, not the booking)

A domestic leg is arranged **per arrival group** — one Cairo→Aswan ticket file covers everyone flying that day — so the document hangs off the group's `BlockServiceStatus` row as **`BlockAttachment`**, uploaded once on `BlockService/Details`. Every booking in the group carries it, **including ones created afterwards**, and removing it removes it for all of them.

It used to be `AttachmentCategory.DomesticFlight` on the booking form, which forced the operator to attach the same file to every booking by hand. That category is **retired** (value 3, never reuse it), the Dropzone is gone from Booking Create/Edit, and the old rows were deleted by the `BlockDocumentsAndAmendments` migration.

| Decision | Why |
|---|---|
| FK to `BlockServiceStatus`, not a repeat of `(DestinationId, CheckInDate)` | Every read path already loads that row (`BlockStatusSpec` in both `GetArrivalsAsync` and `GetBlockAsync`), so `AddInclude("Attachments")` makes the documents free on the list *and* Details with no extra query |
| No `Category` column | The area is domestic-flights-only. A general "block documents" area with a picker was considered and rejected |
| Rows created lazily by **either** a status change or an upload | A group whose only stored fact is a document reads `NotBooked`, which is correct — attaching a ticket says nothing about the supplier |
| Screen-only: not in the `.xlsx`, `d-print-none` on the card | The "is this ours, or theirs?" rule — a domestic ticket is our document; the block file goes to the hotel supplier. Same call as the email links. The block **status** does go in both |
| Reuses `attachment-dropzones.js` (renamed from `booking-attachments.js`) | That script was already generic — it drives every `.attachment-dropzone`, pairing `#dropzone-{key}` with `#input-{key}`, and knows nothing about bookings |
| `AttachmentRules` (`Helpers/`) holds the extension/size/request-cap rules | Two upload paths now exist; a shared constant is what stops them disagreeing on what a valid file is |

**`BlockServiceStatus` is no longer just a status row** — it is the arrival group's persisted record: status, both email links, documents (`Attachments`) and the confirmation snapshot (`ConfirmedBookings`). The groups themselves are still derived from live `BookingStay` rows on every request.

**Files:** `Travel.Domain/Models/BlockAttachment.cs`, `BlockAttachmentConfig.cs`, `BlockDocumentLine` in `BlockDtos.cs`, `UploadDocuments`/`DeleteDocument` on `BlockServiceController`, the card in `Views/BlockService/Details.cshtml`, uploads into `wwwroot/Files/BlockAttachments/`. Permission: **`BlockServices.ManageDocuments`**.

### Missing domestic-flight document flag

An operator working an arrival file needs to see which bookings still have no domestic flight document. The rule:

> **`Booking.RequiresDomesticFlight`** (a computed property beside `Pax`/`LeadGuestName`) — the booking has a cruise stay **or** its itinerary touches 2+ distinct destinations. **Missing** — requires one and **none of the arrival groups on its whole itinerary** holds a `BlockAttachment`.

There is no `Booking.MissingDomesticFlightDoc` any more: the document lives on the group, so the fact isn't answerable from a booking in isolation. `BlockServiceQueryService.LoadMissingDomesticDocsAsync` owns it, running `BookingDocsSpec` once per request and returning a `HashSet<int>` that both `BuildSummary` and `BuildBookingLine` read.

**🔴 The flag cannot be computed from `BlockStaysSpec`'s own includes, and adding `Booking.Stays` there would look like it works.** That spec is windowed by date and optionally narrowed to one destination, so its `Booking.Stays` is the *filtered* itinerary — a Cairo-only filter hides the Hurghada leg, and **both** halves of the rule then read wrong ("2+ destinations" goes `false`, and the Hurghada group's document is never consulted). Flag wrong, no error. `BookingDocsSpec` (`Specifications/Bookings/`) loads the **whole** itinerary for the booking ids in play.

**Screen-only, by the "is this ours, or theirs?" rule.** A missing *internal* document is the operator's concern, not the supplier's, so `BlockExcelExporter` doesn't write it and the Details badge carries `d-print-none` — same reasoning as the email links.

`BlockSummary.MissingDomesticFlightCount` (a count, so the icon's `title` can say "2 of 5") renders as an inline `bx-error-circle` in the **Destination cell** of `Index`/`SingleArrivals` — deliberately **not a new column**, so no DataTables positional index moves. `BlockBookingLine.MissingDomesticFlightDoc` renders as a badge on the Details booking card. `Booking/Details` lists the covering documents read-only, grouped by arrival, from `BookingBlockLine.Status.Documents`.

---

## Block & Booking Status

Two statuses that are **linked, not independent**.

| | Values | Owned by |
|---|---|---|
| **`BlockStatus`** (per arrival group) | `NotBooked` / `InProgress` / `Confirmed` | the operator, via `/BlockService` |
| | `NeedsAmendment` | **derived at read time** — never stored, never settable |
| **`BookingStatus`** | `InProgress` / `Confirmed` | **derived** — never set by hand |
| | `Completed` / `Cancelled` | the operator, via `Booking/SetStatus` |

**The rule, in one line:** a booking is `Confirmed` exactly when **every** arrival group on its itinerary holds a confirmation snapshot that **still matches** it, and drops back to `InProgress` the moment one doesn't. A Cairo + Cruise + Hurghada booking needs all three. `RecalculateAsync` (`BlockStatusService`) is the single owner of this and **never touches a booking whose status is `Completed` or `Cancelled`** — those are the human's.

**`BlockServiceStatus` is the first persisted row Block Services has ever had**, and now carries the group's whole record (status, both email links, `Attachments`, `ConfirmedBookings`). The groups themselves are still derived from live `BookingStay` rows on every request; this side table just hangs off the same identity a group already had, `(DestinationId, CheckInDate)`. Rows are **created lazily** by a status change *or* a document upload — a group with no row reads as `NotBooked` (`BlockStatusInfo.NotBooked`), and an orphaned row (every booking moved off that date) is harmless: it simply re-applies if a booking returns.

### `NeedsAmendment` — the confirmation snapshot

> A block is **Confirmed** exactly while the supplier's confirmation still describes the group as it stands now. The moment a booking joins, leaves, or changes its **rooms / PAX / nights**, it reads **Needs amendment** — and *only the bookings no longer covered* lose their Confirmed badge.

Before this existed, a booking created into an already-confirmed set of blocks landed `Confirmed` immediately — claiming a confirmation no supplier had given, with nothing anywhere to say so.

**`BlockConfirmedBooking`** is what makes it derivable: one row per booking per group, written when the operator confirms and deleted when the block moves off `Confirmed`. It stores a **fingerprint of the supplier-relevant facts** (`Adults`/`Children`/`Infants`/`Nights` + a canonical `RoomsFingerprint` like `"1x2|2x1"`), not a timestamp — so no `Booking.UpdatedAt` column is needed, and fixing a typo in the notes or filling in a flight number two weeks later doesn't falsely re-open a settled block. **`BlockConfirmation`** (`Travel.Domain/Services/BlockServices/`) is the one pure owner of `RoomsFingerprint` / `Matches` / `Snapshot` / `Resolve`, shared by the read and write sides so they can't drift.

| Decision | Why |
|---|---|
| Supplier-relevant = **rooms, PAX, nights** only | What the supplier actually blocks. Guest names, hotel, category and flight details deliberately don't flag — flights are filled in late and would make the status noise |
| `NeedsAmendment` is **derived, not stored** | Nothing can park a block in a state it can't leave; re-clicking Confirmed rewrites the snapshot and is exactly the "re-confirm" action. `SetStatusAsync` rejects it explicitly — `Enum.IsDefined` alone would let it through — and `block-status.js` maps a derived `4` back to `3` when opening the modal, since the select has no option for it |
| Only the **changed** bookings drop | The bookings the supplier did confirm are still confirmed; dropping all of them would be a lie in the other direction. This is why `RecalculateAsync` reads the *snapshot*, not the block's status |
| Booking FK on the snapshot is **`NoAction`** | Breaks the second cascade path into that table (SQL Server error 1785). `BookingController.Delete` clears the rows by hand |

**Triggers:** `SetStatusAsync` (writes/clears the snapshot) and `BookingController.Create`/`Edit`/`Regenerate` (recalculate). The block's own badge needs no trigger at all — it is resolved on every read.

**Two permanent, independent email links** — `RequestEmailLink` (the email that requested the block) and `ConfirmationEmailLink` (the supplier's reply). Named for the business fact, not the status. They are **only ever written, never cleared**: a blank field in the modal leaves the stored value alone, so confirming can't lose the request thread and moving back to `NotBooked` can't lose either. Validated server-side to absolute `http`/`https` (they render as anchors — a `javascript:` value must not survive).

**The links are screen-only.** They are internal references into the operator's own mailbox, and both the `.xlsx` and the printed sheet go **to the supplier** — so `BlockExcelExporter` doesn't write them and the Details block carries `d-print-none`. The block **status** does appear in both. Anything added to the supplier-facing file needs the same question asked of it: *is this ours, or theirs?*

| Decision | Why |
|---|---|
| Booking `Confirmed` requires **all** blocks, not the first | The booking isn't really confirmed while two hotels are still unbooked; a partial confirmation that reads "Confirmed" is a lie the operator would act on |
| Coverage is read from the **snapshot**, not from `BlockStatus == Confirmed` | A block reading `NeedsAmendment` because a *different* booking changed must not un-confirm this one |
| Un-confirming a block **reverts** its bookings | Re-evaluation runs in both directions, so the badge always reflects current reality rather than a high-water mark |
| `InProgress`/`Confirmed` are **not** hand-settable | `BookingController.SetStatus` rejects them outright. Offering them would let a booking claim a confirmation no supplier gave |
| "Reactivate" posts **no** status | It hands the booking back to the derivation instead of guessing a value. A reactivated booking rejoins its groups, so any of them that were confirmed without it now read `NeedsAmendment` |
| Cancelled bookings are **excluded from Block Services entirely** | A supplier file that counts a cancelled booking over-states rooms. `BlockStaysSpec` filters them in **both** ctors — see the red-flag note below |
| `Status` is `Ignore()`d in `BookingProfile` (VM → entity) | Same reasoning as `Code`/`EmbarkDay`: an ordinary Edit save (or a tampered POST) must not overwrite a derived value |

**Recalculation triggers:** `SetStatusAsync` (both directions), and `BookingController.Create`/`Edit`/`Regenerate` — all three can change which blocks a booking belongs to, or the facts the snapshot pins.

**Permissions:** `BlockServices.ChangeStatus`, `Bookings.ChangeStatus`, `BlockServices.ManageDocuments` (all added to existing modules, so no `PermissionCatalog` renumbering). 35 at the time; 37 once Reports landed — still well under the ~60 cookie ceiling.

**Presentation:** **`Travel.Presentation/Helpers/StatusBadges.cs`** is the single source of truth for label + badge class + icon (`ForBlock` / `ForBooking`), mirroring `PermissionIcons`. There is no `GetDisplayName()` helper in this project, which is why enum display strings otherwise get hand-written per view. `ExcelFormat.BlockStatusText`/`BookingStatusText` are its counterpart for exports. `Booking/Details` reads `BookingBlockLine.IsCovered` — not the block's status — for both the "N of M blocks confirmed" line and the per-row "this booking is / isn't covered" note.

**Files:** enums in `Travel.Domain/Enums/BlockStatus.cs` + `BookingStatus.cs`; entities `BlockServiceStatus.cs`, `BlockAttachment.cs`, `BlockConfirmedBooking.cs`; configs of the same names; `IBlockStatusService` + `BlockStatusChange` + **`BlockConfirmation.cs`** in `Travel.Domain/Services/BlockServices/`; `BlockStatusInfo`/`BookingBlockLine`/`BlockDocumentLine` in `BlockDtos.cs`; `BlockStatusSpec` + `BlockStaysSpec` + `BookingsForStatusSpec` in `Specifications/`; impls `Travel.Services/BlockServices/BlockStatusService.cs` + **`BlockGroupLoader.cs`** (the shared narrow-then-match-in-memory key loader); UI in `Views/BlockService/_BlockStatusCell.cshtml` + `_BlockStatusModal.cshtml`, `Views/Booking/_BookingStatusActions.cshtml`, and `wwwroot/back/assets/js/block-status.js`.

---

## Printable Documents & Excel Export

Two pages are documents an operator prints or sends out: **`BlockService/Details`** (a supplier's arrival file — a block or a single arrival, same document) and **`Booking/Details`** (a single booking). Both carry `Print` + `Excel` buttons, gated on `BlockServices.Export` / `Bookings.Export` respectively.

**Print** — put **`.print-sheet`** on the card that *is* the document. The `@media print` block in `app-extras.css` then hides `#page-topbar` / `.vertical-menu` / `.footer` / `.right-bar`, zeroes the content padding, strips the card border, keeps `thead` with its rows (`display: table-header-group`) and avoids splitting rows across pages. Buttons carry **`d-print-none`**. Adding print support to a new page is just those two classes — don't write a second `@media print` block.

**Excel** — one exporter per document in `Travel.Presentation/Services/`, sharing **`ExcelFormat`** (content type, room/guest/category/flight text, `FormatRooms`, `SheetName`, `SanitizeFileName`, `WriteHeaderRow`, `WriteHeaderCells`, `WriteGroupHeader`, `WriteField`). Put any new formatting **mechanic** there rather than in an exporter, so a booking sheet and a block file can't disagree on how a `RoomType` is spelled. A **palette** used by exactly one document stays on that exporter — see `FlightReportExcelExporter`.

| Exporter | Output | Filename |
|---|---|---|
| `BlockExcelExporter` | Arrival file — one row per booking + indented guest sub-rows, frozen header, TOTAL row. **Same layout at any booking count**; only the label changes | `Block_{Destination}_{yyyy-MM-dd}.xlsx`, or `Arrival_…` when the group holds one booking |
| `BookingExcelExporter` | Booking — facts block, `GUESTS` table, `SCHEDULE` table with computed check-outs + total nights | `Booking_{Code}.xlsx` |
| `FlightReportExcelExporter` | Flights report — one row per booking, guests stacked in one cell, under a two-row merged `ARRIVAL`/`DEPARTURE` header. See Reports | `Flights_{from}_{to}.xlsx` |

ClosedXML lives **only** in `Travel.Presentation` so `Travel.Services` stays NuGet-free.

---

## Booking Attachments

Bookings carry supporting documents: **Flights** and **Passports** (each many files) and a **Contract** (a link, not a file). All three live in one table, **`BookingAttachment`** (`Travel.Domain/Models/`) — `Category` (`AttachmentCategory` enum), `FileUrl`/`OriginalFileName` for the file categories, `LinkUrl` for `Category = Contract`. One entity instead of a separate scalar column on `Booking` keeps the Create/Edit/Details code uniform across all categories.

> **Domestic flights are no longer here.** They belong to the arrival group — see Domestic flight documents under Block Services. `AttachmentCategory` value **3 is retired; never reuse it.**

| Decision | Why |
|---|---|
| Contract is a row in the same table (`LinkUrl` set, `FileUrl` null), not a `Booking.ContractLink` column | Same CRUD path as the file categories — no special-cased scalar to keep in sync |
| Attachments are **additive on Edit**, not wholesale-rebuilt like `Stays`/`Guests`/`Rooms` | Files can't be regenerated from form state — `RemoveAttachmentIds` (checkbox list, posted ids) deletes specific rows; new uploads are appended; the Contract row is upserted by `Category` |
| Files saved via the existing `IFileStorageService` into a `BookingAttachments` folder — no new storage code | Same contract as `PackageController`'s cover image (`SaveFileAsync` → GUID-prefixed name → public `/Files/{folder}/{name}` URL stored on the entity) |
| Validation is server-side only (`BookingController.ValidateAttachments` → **`AttachmentRules`**): `.pdf/.jpg/.jpeg/.png`, 10MB/file | Client-side `acceptedFiles`/`maxFilesize` on the Dropzone instances mirror this but aren't trusted. The rules live in `Helpers/AttachmentRules.cs` because `BlockServiceController` uploads too |
| `Create`/`Edit` POST carry `[RequestSizeLimit]` + `[RequestFormLimits(MultipartBodyLengthLimit = ...)]` (100MB) | Kestrel's own default request-body cap (~28.6MB) is smaller than a handful of real photos across three unlimited-count categories — see the 2026-08-02 Gotcha below, this silently killed real uploads before any of our validation ran |
| No new permissions | Gated by the existing `Bookings.Create`/`Edit`/`View` policies — attachments are just part of a booking, not a separately-grantable capability |

**Deleting a booking must delete its files, not just its rows.** The DB cascade (`BookingAttachmentConfig`, ownership FK) removes the `BookingAttachment` rows, but not the physical files on disk — `BookingController.Delete` loops `booking.Attachments` and calls `_fileStorage.DeleteFile(...)` before deleting the booking, same as `PackageController.Delete` does for the cover image.

**Dropzone.js UI (`Views/Booking/_BookingAttachments.cshtml` + `wwwroot/back/assets/js/attachment-dropzones.js`)** — the Skote template ships Dropzone.js unused; this is its first use. The script is **generic and shared with `BlockService/Details`**: it drives every `.attachment-dropzone` on the page, pairing `#dropzone-{key}` with `#input-{key}`, and knows nothing about bookings. Each category renders a `.dropzone` div plus a hidden native `<input type="file" multiple name="FlightFiles">` (etc.) — Dropzone never uploads anything itself (`autoProcessQueue: false`, `url: '#'`); instead, on every `addedfile`/`removedfile`/`error` event the script rebuilds the hidden input's `FileList` via the `DataTransfer` API, filtering out files still in `Dropzone.ERROR` status, so the files travel with the ordinary form POST (`enctype="multipart/form-data"`, added to both `Create.cshtml` and `Edit.cshtml`). `Dropzone.autoDiscover = false` is required at the top of the script — the vendor file auto-scans for `.dropzone` elements on `DOMContentLoaded` otherwise and double-inits.

**Files:** enum in `Travel.Domain/Enums/AttachmentCategory.cs`, entity in `Travel.Domain/Models/BookingAttachment.cs`, config in `Travel.Repository/Data/Configrations/BookingAttachmentConfig.cs`, form fields + display DTO in `BookingViewModel.cs`, all controller logic in `BookingController.cs` (`ValidateAttachments`, `BuildNewAttachmentsAsync`, `ToAttachmentViewModels`), shared partial `Views/Booking/_BookingAttachments.cshtml` used by both Create and Edit, Details renders its own grouped read-only list.

---

## Booking Schedule & Flights

`Booking.ArrivalDate`/`DepartureDate` are the **package** dates — they drive the itinerary generator and Block Services grouping. Flight details are a separate, optional, operator-entered fact: `ArrivalFlightNo?`/`ArrivalTime?`/`DepartureFlightNo?`/`DepartureTime?` (all nullable — a blank form field mapping onto a non-nullable `TimeOnly` would silently store, and display, a real-looking `00:00`) plus `FlightArrivalDate?`/`FlightDepartureDate?`. A guest routinely flies in the night before the package starts and out after it ends, so the flight date is never assumed to equal the package date; a null flight date **displays** as a fallback to the package date but is stored separately.

**The flight dates are recorded exactly as entered and are deliberately unvalidated against the package dates** (removed 2026-08-11). They used to be required to bracket the package — arrive on or before, depart on or after — enforced in three places at once: `ValidateFlightDates` in `BookingController`, a submit-time `toastr` gate in `booking-form.js`, and native `min`/`max` on the two date inputs. All three are gone. The rule was wrong in practice: a late joiner flies in *after* the package starts and an early leaver flies out *before* it ends, and an operator who can't record that has no way to save the booking at all. Nothing downstream depends on the ordering — these fields drive no itinerary, no grouping, no derived date; they are display-only. `syncFlightDates()` still **defaults** each field to the corresponding package date until the operator edits it (clearing a field resumes mirroring) — a convenience, not a constraint, and the only reason `flightDirty` exists.

**Create posts a client-built schedule instead of always regenerating server-side.** `Create.cshtml` calls the same `Preview` JSON endpoint Edit's "Regenerate" button uses, renders the stay rows in an editable table, and posts them as `model.Stays`. `BookingController.Create` uses them verbatim (`BuildStaysFromInputsAsync`, the same path `Edit` already used) whenever `model.Stays` is non-empty; an empty list means the client script never ran (JS blocked, stale page) and Create falls back to generating server-side from the package, same as before this change.

**The stored `EmbarkDay` is derived from the final schedule, not trusted from the posted radio.** `DeriveEmbarkFromStays` reads the actual `CheckInDate` of the cruise stay in the final `Stays` list and converts it to a `WeekDays` flag (`1 << (int)DayOfWeek` — see the `[Flags]` layout in `WeekDays.cs`: Sunday=1 … Saturday=64; returns `null` for a non-cruise package). This runs after `Stays` is rebuilt in **both** Create and Edit (and after `Regenerate`), so a hand-edited schedule and the stored `EmbarkDay` can never disagree — the posted `EmbarkDay` value only matters as the *first draft* the schedule table is built from.

**The embark-sailing picker has one server-side source of truth, `EmbarkOptionViewModel.Build`, rendered by one partial, `_BookingEmbarkOptions.cshtml`, shared by Create and Edit.** Edit renders its options server-side (not via AJAX) specifically so a page load doesn't call `Preview` and silently regenerate the saved schedule. `booking-form.js`'s `renderEmbark()` rebuilds the same markup client-side from the `<template id="embark-option-template">` in that partial whenever the package/arrival/sailing changes after load — a markup change to the partial must be mirrored in `renderEmbark()`, and vice versa.

---

## Nationalities

A plain reference lookup — **`Nationality`** is `BaseModel` + a single required, unique `Name` (max 100). No `IsActive`, no code, no seed: the table ships empty and the operator adds the nationalities they actually deal with. The CRUD slice is a deliberate copy of `Destination`'s (controller, `_NationalityForm` partial shared by Create/Edit, `[Remote]` `IsNameAvailable` gated on **View**, SweetAlert2 → JSON `Delete` → Toastr) with the cruise/departure-days machinery removed, so there is no `nationality-form.js`.

**It replaced `Booking.Nationality`, which used to be free text.** The field is now `int? NationalityId` + a `Nationality?` navigation, rendered as a plain `form-select` in the Contact card of Create/Edit.

| Decision | Why |
|---|---|
| Booking-level, **not per guest** | Exactly where the free-text field already was. A per-guest `<select>` would also have needed `reindexGuests()` widened — it walks `input` only, so a `<select>` in a guest row would never get reindexed (`reindexRooms`/stays use `'select, input'`) |
| DB column **nullable**, ViewModel `[Required]` | The migration drops the old string with **no backfill** (the user's call), so existing rows must be allowed to have no value; every new or edited booking must still pick one. Old bookings render "—" until next edited |
| `int?` + `[Required]`, not `int` | Same reason as `Category`: a non-nullable int behind an empty `<select>` yields the raw binder message ("The value '' is invalid") instead of "Nationality is required." |
| FK is `Restrict` | Reference FK, not ownership — matches `Booking.Package`. `NationalityController.Delete` refuses an in-use nationality first, so the constraint is only a backstop |
| `BookingWithDetailsSpec` gained `AddInclude("Nationality")` | Without it `Details` and `BookingExcelExporter` render "—" for **every** booking regardless of what's stored — a silent failure, since the FK is legitimately nullable |
| Catalog module inserted at **Order 5** | Grouped with the Travel modules rather than appended after Roles; Users→6 and Roles→7 were renumbered in the same pass |

**Files:** `Travel.Domain/Models/Nationality.cs`, `Travel.Repository/Data/Configrations/NationalityConfig.cs` (index `IX_Nationalities_Name` — no `_Code` in the name), `Travel.Presentation/{ViewModels,MappingProfiles,Controllers}/Nationality*`, `Views/Nationality/`, migration `AddNationalities`.

---

## Reports

Read-only operational views built from live booking data. **Nothing is stored** — no table, no migration, no status; every request re-derives its rows, same as Block Services. `ReportController` is the one home for them, one action per report plus its `Export…` twin.

The sidebar entry is the app's **first `has-arrow` / `sub-menu`** item. Skote's metisMenu ships that markup and initialises it (`$("#side-menu").metisMenu()` in `back/assets/js/app.js`), it had just never been used — so a second report is a new `<li>` in the existing `<ul class="sub-menu">`, nothing structural.

### Flights (`/Report/Flights`)

One row **per booking**, with **all of its guests stacked inside a single `Pax Names` cell** — the airport / meet-and-greet team's pickup list. Filter is a date range and nothing else, deliberately.

> It was built one-row-per-passenger first, which repeated the booking code, both dates, both times and both flight numbers on every guest — a 4-guest booking restated the same flight four times. The grain is the **booking**: that's what has a flight. Guests are an attribute of it, so they go in one cell, one name per line (`string.Join("\n", …)` + `WrapText` in Excel, `<div>` per name on screen).

> **A booking is listed when its arrival *or* its departure falls in the range.** Not both, not arrival-only. A booking arriving 05-Jul and leaving 12-Jul belongs on both that week's and the next week's sheet — it is two movements, and an AND (or an arrival-only window) silently loses one of them.

| Decision | Why |
|---|---|
| The date is **`FlightArrivalDate ?? ArrivalDate`** (same for departure) | A null flight date means "not recorded yet"; the package date is the best estimate available. The **filter and the rendered cell use the same coalesced value**, so a row can never appear in a range its own date column disagrees with. 9 of 13 live bookings have null flight dates — this path is the common one, not the edge case |
| **`ExcelFormat.FormatFlight` is not reused** | It returns `—` unless a flight number or time is also known, and packs number + date + time into one cell. Right for a booking sheet; wrong here, where a passenger with no flight info yet must still appear **with a visible gap to fill**, in three separate columns |
| Bookings with **no flight details are included** | The report doubles as a "still need flight info" checklist. This was the user's explicit call |
| **Cancelled bookings excluded** | Somebody would otherwise wait at arrivals for a passenger who isn't coming. Same reasoning as `BlockStaysSpec`, and the same red flag: `FlightReportSpec`'s `Criteria` decides who reaches a sheet that gets sent out |
| A booking with **zero guests still emits a row** (empty name list → `—`) | Dropping it hides a real flight movement. The `MinimumBookings = 2` / single-arrivals bug, third occurrence — a filtered-out row can't complain. It counts as 0 passengers, not 1 |
| Excel **row height is set explicitly** (`15pt × name count`) | A wrapped multi-line cell is clipped to the default height until the user touches the row — Excel does not auto-fit on open. Without this the 2nd and 3rd guest are invisible in the file the supplier receives |
| Per-report permissions (`Reports.ViewFlights` / `Reports.ExportFlights`) | More reports are coming; a blanket `Reports.View` couldn't grant one report without granting all of them |
| Catalog module **appended at Order 8** | Unlike Nationalities (inserted at 5 and renumbered), this one is appended, so Bookings…Roles keep their orders. It therefore renders **after** Roles in the picker — move it to 6 and renumber Users/Roles if that grouping ever matters |

**Excel** — `FlightReportExcelExporter`, 8 columns, matching the client's sketch exactly: `Booking No` and `Pax Names` merged down rows 1–2 (blue), `ARRIVAL` merged over C:E (green) and `DEPARTURE` over F:H (peach), sub-headers on row 2, header frozen. The three fills are **exporter-local** — no other sheet uses that palette and `ExcelFormat.HeaderFill` stays the shared grey. The generic mechanics went into `ExcelFormat` instead: **`WriteGroupHeader`** (merged, centered, spanning) and **`WriteHeaderCells`** (`WriteHeaderRow` at an arbitrary start column with a caller-chosen fill) — `WriteHeaderRow` is hard-anchored to column 1 and can't write the second row of a grouped header.

**`FlightReportRow`** (in `FlightReportViewModel.cs`) is the single shape both the table and the `.xlsx` render, built once by `ReportController.BuildRows`, so the screen and the sheet cannot drift. It carries `PaxNames` as an `IReadOnlyList<string>` rather than a pre-joined string — the two renderers need different separators (`"\n"` vs `<div>`), and the passenger count falls out of it.

**Files:** `Travel.Domain/Specifications/Bookings/FlightReportSpec.cs`, `Travel.Presentation/Controllers/ReportController.cs`, `ViewModels/FlightReportViewModel.cs`, `Services/FlightReportExcelExporter.cs`, `Views/Report/Flights.cshtml`, the Reports block in `Views/Shared/_Sidebar.cshtml`.

---

## Service Layer

| Interface (Travel.Domain/Services) | Implementation | Purpose |
|---|---|---|
| `IFileStorageService` | `FileStorageService` (Travel.Repository/Storage) | Disk-backed upload/delete — see File Storage |
| `IItineraryGeneratorService` | `ItineraryGeneratorService` (Travel.Services) | Builds a booking's itinerary from its package |
| `ICodeGeneratorService` | `CodeGeneratorService` (Travel.Services/CodeGeneration) | Sequential codes for any `ICodedEntity` — see Code Generation |
| `IBlockServiceQuery` | `BlockServiceQueryService` (Travel.Services/BlockServices) | Groups booking stays into block files — see Block Services |
| `IBlockStatusService` | `BlockStatusService` (Travel.Services/BlockServices) | Block status + email links, and the only owner of booking-status derivation — see Block & Booking Status |
| `IRoleService` | `RoleService` (Travel.Services/Identity) | Role CRUD + atomic role-claim replacement |
| `IUserAccountService` | `UserAccountService` (Travel.Services/Identity) | User CRUD, roles, direct permissions, lockout guards |

Services return **`OperationResult` / `OperationResult<T>`** (`Travel.Domain/Services/OperationResult.cs`) with `From(IdentityResult)`; controllers turn that into `ModelState` errors or the `{ success, message }` JSON the AJAX flows expect.

> Business-logic services go in `Travel.Services` (interface in Domain, impl in Services). Keep them free of **web** types — `IFormFile`, `SignInManager`, controllers, `HttpContext` — so a future `Travel.Api` could reuse them. `UserManager`/`RoleManager` **are** allowed: they live in `Microsoft.Extensions.Identity.Core` (no HTTP coupling), an API would inject the same types, and `AppUser : IdentityUser` already puts Identity on Domain's public surface. `SignInManager.RefreshSignInAsync` stays in controllers.

---

## Database

**`TravelContext` : `IdentityDbContext<AppUser>`** — applies configs via `modelBuilder.ApplyConfigurationsFromAssembly(...)`. Only `AspNet*` Identity tables exist so far (with `AppUser`'s custom columns). Add DbSets + `Config` classes as entities are introduced.

**FK cascade note (SQL Server):** SQL Server forbids multiple cascade paths to the same table (error 1785). As relations are added, use `DeleteBehavior.Cascade` only for true ownership FKs and `NoAction` for reference FKs to break cycles.

**Seeding:** `AppIdentityDbContextSeed.SeedUserAsync` seeds one admin (`MustafaAdmin`) on first run, called from `Program.cs` after `MigrateAsync()`. No roles seeded yet.

**Connection string** (`appsettings.json` → `DefaultConnection`):
```
Server=.;Database=TravelDB;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;
```

---

## Program.cs — DI Registration & Pipeline

```csharp
// Data & infra
AddDbContext<TravelContext>(SqlServer)
AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>))
AddScoped<IUnitOfWork, UnitOfWork>()
AddScoped<IFileStorageService>(_ => new FileStorageService(builder.Environment.WebRootPath))

// MVC
AddControllersWithViews() + AddRazorRuntimeCompilation()
AddAutoMapper(typeof(Program).Assembly)

// Identity (cookie auth)
AddIdentity<AppUser, IdentityRole>(password policy)
    .AddEntityFrameworkStores<TravelContext>()   // REQUIRED — wires the user/role stores
    .AddDefaultTokenProviders()
ConfigureApplicationCookie: LoginPath=/Account/Login, AccessDeniedPath=/Account/AccessDenied, 8h sliding
```

**Startup:** `MigrateAsync()` then seed, inside a try/catch that logs via Serilog.
**Pipeline:** `ExceptionHandler`/`HSTS` (prod) → HTTPS → Routing → Authentication → Authorization → StaticAssets → MapControllerRoute. Default route `{controller=Account}/{action=Login}/{id?}` — app starts on the Login page.

---

## Controllers & Views

### AccountController
| Action | Method | Auth | Description |
|---|---|---|---|
| `Login` | GET/POST | Anonymous | Cookie sign-in; supports email OR username; blocks inactive users; honors local `returnUrl` |
| `SignOut` | GET | Authorize | Sign out → Login |
| `AccessDenied` | GET | Anonymous | 403 page |

> A profile view/update-image flow is stubbed out (commented) in `AccountController` — reactivate it once the profile view + `ProfileViewModel` exist.

### HomeController `[Authorize]`
`Index` (dashboard placeholder), `Privacy`, `Error` (**`[AllowAnonymous]`** — it's the `UseExceptionHandler` target; requiring auth there would swallow real errors behind a login redirect).

### BookingController `[Authorize]`
`Index(arrivalFrom, arrivalTo, packageId, status)` / `Details` / `Create` / `Edit` / `Delete` (JSON) / `SetStatus` (JSON — Completed/Cancelled/reactivate only, see Block & Booking Status) / `Preview` (JSON — regenerates the itinerary + embark options for the AJAX schedule editor, shared by Create's live editor and Edit's "Regenerate" button). `Index`'s filters are opt-in GET params bound to `BookingListViewModel` (`HasFilters` distinguishes "no matches" from "no bookings yet") — a first visit with no query string still shows everything, and the `Package` dropdown deliberately includes **inactive** packages, since an old booking commonly points at a retired one and would otherwise be unfilterable. The arrival column carries `data-order` in ISO (`yyyy-MM-dd`) so DataTables sorts it correctly — the visible cell is a display string (`"01 Apr 2026"`), which sorts wrong lexicographically. `Create`/`Edit` carry `[RequestSizeLimit]`/`[RequestFormLimits]` for attachment uploads — see Booking Attachments — and share the schedule/flight-date handling in Booking Schedule & Flights.

### BlockServiceController `[Authorize]`
`Index(from, to, destinationId, status, applied)` / `SingleArrivals(same args)` / `Details(destinationId, checkIn)` / `Export(destinationId, checkIn)` / **`SetStatus`** (JSON POST) / **`UploadDocuments`** (multipart POST → redirect; carries `[RequestSizeLimit]` + `[RequestFormLimits]`) / **`DeleteDocument`** (JSON POST). The two lists share **`BuildListModelAsync`** (and one `BlockServiceListViewModel` carrying both `Blocks` and `Singles`), so their filter semantics can't drift: it defaults `from` to today **only when `applied == false`**, and the filter form posts a hidden `applied=true` so a deliberately cleared From box isn't silently re-filled. `Details`/`Export` return `NotFound()` only when **no** stay matches the key.

### NationalityController `[Authorize]`
`Index` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable` — the `DestinationController` shape with nothing added. See Nationalities.

### RoleController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable`, each `[Authorize(Policy = Permissions.Roles.X)]`. Permissions are edited inline on the form, so `Roles.ManagePermissions` is enforced **inside** the POST and via `<permission>` around the picker.

### UserController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` / `ToggleActive` / `ResetPassword` (all JSON for the AJAX ones) / `Permissions` GET+POST / `IsUserNameAvailable` / `IsEmailAvailable`.
The C# methods behind `/User/Permissions` are named **`DirectPermissions`/`DirectPermissionsPost`** with `[ActionName("Permissions")]` — a member called `Permissions` would shadow the `Permissions` static class and break every `[Authorize(Policy = Permissions.X)]` in the file.

### ReportController `[Authorize]`
`Flights(from, to, applied)` / `ExportFlights(same args)`, each `[Authorize(Policy = Permissions.Reports.X)]`. Both go through one private `BuildAsync`, so the table and the sheet share their rows. See Reports.

### Views
`Account/Login`+`AccessDenied`, `Home/Index`+`Privacy`, `Booking/`, `BlockService/` (`Index`+`SingleArrivals`+`Details`+`_ArrivalFilters`), `Package/`, `Destination/`, `Nationality/`, `Report/Flights`, `Role/`, `User/`, and `Shared/` partials (`_Layout`, `_AuthLayout`, `_Sidebar`, `_Nav`, `_Head`, `_Scripts`, `_Footer`, `_RightSideBar`, `_Notifications`, `_ValidationScriptsPartial`, **`_PermissionModules`**, `Error`).
`_ViewImports` imports `Travel.Presentation`, `.Models`, `.ViewModels`, `Travel.Domain.Authorization`, and registers `@addTagHelper *, Travel.Presentation`.
**`Shared/_PermissionModules.cshtml`** is the one permission picker, shared by `Role/_RoleForm` and `User/Permissions` — driven by `IsLocked`, so the two can't drift. Its JS is `wwwroot/back/assets/js/role-permissions.js`.

---

## Authorization System (implemented)

Claims-based permissions on top of ASP.NET Identity. **No `Permission` table and no EF migration** — the catalog is C# constants; assignments are rows in `AspNetRoleClaims` / `AspNetUserClaims` with `ClaimType = "permission"`.

### `Travel.Domain/Authorization/`

| File | Purpose |
|---|---|
| `AppClaimTypes.cs` | `Permission = "permission"` — the claim type |
| `Permissions.cs` | Nested static classes per module; `const string` values shaped `Module.Action`. **Consts are mandatory** — `[Authorize(Policy = ...)]` needs a compile-time constant |
| `AppRoles.cs` | `SuperAdmin` const + `IsProtected(name)`. Named `AppRoles`, not `Roles`, to avoid clashing with the `Permissions.Roles` module |
| `PermissionCatalog.cs` | Explicit descriptor list (display name, description, module, order) driving the picker UI. `Sanitize()`, `IsValid()`, `FindCatalogGaps()` |

Modules: **Bookings** (+`Regenerate`, `Export`, `ChangeStatus`), **BlockServices** (`View`, `Export`, `ChangeStatus`, `ManageDocuments`), **Packages**, **Destinations**, **Nationalities**, **Users** (+`ToggleActive`, `ResetPassword`, `AssignRoles`, `ManagePermissions`), **Roles** (+`ManagePermissions`), **Reports** (`ViewFlights`, `ExportFlights` — one pair per report). 37 total.

**Adding a permission:** add the `const` to `Permissions.cs` **and** an entry to `PermissionCatalog.Modules`. `FindCatalogGaps()` logs a Serilog warning at startup if the two drift. A whole new **module** also needs an icon in `Travel.Presentation/Helpers/PermissionIcons.cs` keyed by the same module `Key`, and inserting one mid-list means **renumbering every later module's `Order`**.

### `Travel.Presentation/Authorization/`

- `PermissionRequirement` / `PermissionAuthorizationHandler` — **SuperAdmin short-circuits**; everyone else needs the matching claim.
- `PermissionPolicyProvider` (**singleton**) — builds a policy on demand when `PermissionCatalog.IsValid(policyName)`; anything else falls through to the default provider, so a typo throws loudly instead of silently 403ing.
- `AppUserClaimsPrincipalFactory` — the base `UserClaimsPrincipalFactory<AppUser, IdentityRole>` **already** unions role claims + user claims, so this only de-duplicates, drops claims for permissions removed from the catalog, and emits **none** for SuperAdmin (keeps the cookie small).

### Effective permissions

`effective = union(role permissions, direct user permissions)` — **grants only, no deny**. On `/User/Permissions/{id}`, role-inherited items render `checked disabled` (browsers never submit disabled inputs, so they can't be saved as direct claims); `ReplaceDirectPermissionsAsync` also strips them server-side, while preserving a pre-existing direct claim that a role now happens to cover.

### Propagation to signed-in users

`SecurityStampValidatorOptions.ValidationInterval = 2 min`. On tick with a matching stamp, Identity calls `RefreshSignInAsync` → re-runs the claims factory → **permissions refresh silently, no re-login**. Rotating the stamp (`UpdateSecurityStampAsync`) instead signs the user out — done on role changes, deactivation, and (by Identity) password resets.

### Gating the UI

`<permission required="@Permissions.Bookings.View" mode="any|all">…</permission>` — evaluates via `IAuthorizationService`, **not** `User.HasClaim` (a SuperAdmin has no permission claims). `output.TagName = null` erases the wrapper so `<li>` stays a direct child of `<ul class="metismenu">`. Registered by `@addTagHelper *, Travel.Presentation` in `_ViewImports`. **It's C#, so a `.cshtml`-only edit isn't enough — rebuild**, or the tag renders literally.

### Lockout guards

`UserAccountService` blocks: deleting/deactivating yourself, removing your own SuperAdmin role, and anything leaving zero active SuperAdmins. `RoleService` blocks editing/renaming/deleting SuperAdmin, and deleting any role that still has members.

### Seeding

`AppIdentityDbContextSeed.SeedRolesAsync` (creates SuperAdmin, **no claims**) → existing `SeedUserAsync` → `AssignSuperAdminAsync` (assigns SuperAdmin if nobody holds it, falling back to the oldest user; backfills the seed user's name/gender). Each is guarded independently of `if (!userManager.Users.Any())`, which is already false on any existing DB.

---

## ViewModels

- **`LoginViewModel`** — `EmailOrUserName` (required), `Password` (required), `RememberMe`.
- **`ErrorViewModel`** — `RequestId` (namespace `Travel.Presentation.Models`, not `.ViewModels`).
- **`DestinationViewModel`**, **`PackageViewModel`**, **`BookingViewModel`** (+ nested inputs).
- **`RoleViewModel`** + `PermissionGroupViewModel` / `PermissionItemViewModel` (the item carries `IsSelected`, `IsLocked`, `LockedReason`, `IsAlsoDirect` so the same partial serves roles and users).
- **`UserFormViewModel`**, `RoleOptionViewModel`, `UserPermissionsViewModel`, `ResetPasswordViewModel`.
- **`BlockServiceListViewModel`** — filter state (`From`/`To`/`DestinationId`/`Applied`) + `IReadOnlyList<BlockSummary>`. `BlockService/Details` binds straight to the `BlockFile` DTO, no ViewModel.
- **`BookingListViewModel`** — filter state (`ArrivalFrom`/`ArrivalTo`/`PackageId`, `HasFilters`) + `IReadOnlyList<Booking>` for `Booking/Index`. **`EmbarkOptionViewModel`** — one cruise sailing choice (`Value`/`Text`/`ExtraNights`), built by `EmbarkOptionViewModel.Build(...)` so the server-rendered radios (`_BookingEmbarkOptions.cshtml`) and the `Preview` JSON response can't disagree on the label text.

Name new ones with the **`ViewModel`** suffix; use `[Remote]` for client-side uniqueness checks.

**Checkbox lists** (`SelectedPermissions`, `SelectedRoles`, `SelectedDepartureDays`) bind from repeated `name="X"` inputs with distinct `value`s into a `List<T>`. **Never use `asp-for` on them** — it emits a companion hidden `false` input that corrupts the list. Keep the property initialised to `new()` and non-nullable: when everything is unchecked nothing posts, and the empty list correctly means "clear all".

---

## File Storage

**`IFileStorageService`** (Travel.Domain) → **`FileStorageService`** (Travel.Repository/Storage):
- `SaveFileAsync(Stream, originalFileName, folder)` → writes to `{basePath}/Files/{folder}` with a GUID-prefixed name (auto-creates the folder), returns the stored name.
- `DeleteFile(fileName, folder)` — no-op if missing.
- `IsImage(fileName)` — extension-based; **single source of truth** for image-vs-file checks (comment classification + profile-picture validation).

Takes a **`Stream`, not `IFormFile`**, so Domain/Repository stay web-agnostic; controllers pass `IFormFile.OpenReadStream()`. `basePath` is injected at startup (`builder.Environment.WebRootPath`).

---

## File Structure Reference

```
Travelling management/
├── Travelling management.sln
├── CLAUDE.md
├── .claude/commands/commit.md          (custom /commit workflow)
├── Travel.Domain/
│   ├── IUnitOfWork.cs
│   ├── Authorization/  AppClaimTypes.cs, Permissions.cs, AppRoles.cs, PermissionCatalog.cs
│   ├── Enums/          Gender, GuestType, PackageCategory, RoomType, WeekDays, AttachmentCategory,
│   │                   BlockStatus, BookingStatus
│   ├── Exceptions/     DuplicateCodeException.cs
│   ├── Models/         BaseModel, ICodedEntity, Booking*, BlockServiceStatus, BlockAttachment,
│   │                   BlockConfirmedBooking, Destination, Nationality, Package*;
│   │                   Models/Identity/AppUser.cs
│   ├── Repositories/IGenericRepository.cs
│   ├── Services/       IFileStorageService.cs, OperationResult.cs, Itinerary/,
│   │                   CodeGeneration/ICodeGeneratorService.cs + CodePrefixes.cs,
│   │                   BlockServices/IBlockServiceQuery.cs + IBlockStatusService.cs + BlockDtos.cs
│   │                                + BlockConfirmation.cs,
│   │                   Identity/IRoleService.cs, Identity/IUserAccountService.cs
│   └── Specifications/ BaseSpecifications.cs, ISpecifications.cs, Packages/,
│                       Bookings/BookingWithDetailsSpec.cs + BookingsForStatusSpec.cs
│                                + BookingDocsSpec.cs + FlightReportSpec.cs,
│                       BlockServices/BlockStaysSpec.cs + BlockStatusSpec.cs,
│                       CodeGeneration/LastCodedEntitySpec.cs
├── Travel.Repository/
│   ├── GenericRepository.cs, SpecificationEvalutor.cs, UnitOfWork.cs
│   ├── Storage/FileStorageService.cs
│   └── Data/TravelContext.cs, AppIdentityDbContextSeed.cs,
│       Configrations/…Config.cs, Migrations/
├── Travel.Services/
│   ├── ItineraryGeneratorService.cs
│   ├── BlockServices/BlockServiceQueryService.cs + BlockStatusService.cs + BlockGroupLoader.cs
│   ├── CodeGeneration/CodeGeneratorService.cs
│   └── Identity/RoleService.cs, UserAccountService.cs
└── Travel.Presentation/
    ├── Program.cs, appsettings.json
    ├── Authorization/  PermissionRequirement, PermissionAuthorizationHandler,
    │                   PermissionPolicyProvider, AppUserClaimsPrincipalFactory
    ├── TagHelpers/PermissionTagHelper.cs
    ├── Helpers/        AvatarHelper.cs, PermissionIcons.cs, StatusBadges.cs, AttachmentRules.cs
    ├── Services/        ExcelFormat.cs (shared), BlockExcelExporter.cs, BookingExcelExporter.cs,
    │                    FlightReportExcelExporter.cs
    │                    (ClosedXML — web project keeps the NuGet out of Travel.Services)
    ├── Controllers/    Account, Home, Booking, BlockService, Package, Destination, Nationality,
    │                   Report, Role, User
    ├── ViewModels/, MappingProfiles/
    ├── Views/ (Account, Home, Booking, BlockService, Package, Destination, Nationality, Report,
    │           Role, User, Shared)
    └── wwwroot/
        ├── css/app-extras.css   (ONLY place for project CSS — see UI/UX Guidelines)
        ├── back/                (Skote template: css/, js/, libs/, images/)
        ├── Files/               (uploads: Packages/, Users/, BookingAttachments/, BlockAttachments/)
        └── lib/, favicon.ico
```

---

## Coding Conventions

1. **PascalCase** public members/types; **camelCase** private fields; **_camelCase** injected deps (`_unitOfWork`, `_mapper`).
2. Suffixes: `Spec` (specifications), `SpecParams`, `ViewModel`, `Config` (EF configs), `Profile` (AutoMapper).
3. Service interfaces in `Travel.Domain/Services/`, impls in `Travel.Services/`; repo interface in Domain, impl in Repository; enums in `Travel.Domain/Enums/`.
4. `DateTime.UtcNow` for all timestamps. All DB entities inherit `BaseModel`.
5. `IncludeStrings` for multi-level (ThenInclude) navigation.
6. Comments only when intent isn't obvious — remove redundant ones.
7. Controllers: `[Authorize(Policy = ...)]` for permission gates, `[Authorize]` for authenticated-only, `[AllowAnonymous]` for login/access-denied. Delete actions return JSON for AJAX; Create/Edit use POST + redirect.
8. Keep controllers HTTP-only — push orchestration into services and view-model building into factories as the app grows.

---

## UI/UX Guidelines

- **Template:** Skote Admin & Dashboard (Bootstrap 5); dark sidebar (`data-sidebar="dark"`), Boxicons.
- **Client preference:** modern, professional, premium feel.
- **Notifications:** Toastr via `TempData`. **Tables:** DataTables. **Delete:** SweetAlert2 confirm → AJAX POST → Toastr.
- **Forms:** Bootstrap card forms with `jquery.validate` + unobtrusive validation.
- **Layouts:** `_AuthLayout` (login, no sidebar) vs `_Layout` (admin: sidebar + topnav + footer + theme sidebar).
- **Branding:** Musta Travel logo at `wwwroot/back/assets/images/Logo_MustaTravel.png` — use it in the auth + admin layouts.

### ⚠️ The template is Bootstrap **5.0.0-beta2**, not 5.3

Before using any Bootstrap utility class, check it exists in `wwwroot/back/assets/css/bootstrap.min.css`. Notably **missing** from this build:
- `bg-*-subtle`, `text-*-emphasis`, `border-*-subtle` (all arrived in 5.3)
- `--bs-primary-rgb` and every other `--bs-*-rgb` variable — so `rgba(var(--bs-primary-rgb), .1)` silently fails; hardcode the rgba
- `btn-soft-*` — Skote's own spelling is the two-class `bg-primary bg-soft`

`bg-*-subtle` and `btn-soft-*` are used ~75 times across 19 views, so rather than rewrite them they are **defined in `wwwroot/css/app-extras.css`**. Palette from `:root`: primary `#556ee6`, secondary `#74788d`, success `#34c38f`, info `#50a5f1`, warning `#f1b44c`, danger `#f46a6a`, dark `#343a40`.

### `wwwroot/css/app-extras.css` — the only place for project CSS

Never edit `bootstrap.min.css` / `app.min.css` (a Skote update replaces them). Linked last in `_Head.cshtml` **and** `_AuthLayout.cshtml`, deliberately **without an `id`**: `back/assets/js/app.js` implements light/dark by rewriting the `href` of `#bootstrap-style` and `#app-style`, so an id-less link survives the swap and always wins the cascade.
> `wwwroot/css/site.css` and `Views/Shared/_Layout.cshtml.css` exist but are **not referenced by anything** — don't put styles there. (`_Layout.cshtml.css` would actually override Skote's primary color if CSS isolation were ever wired up.)

**Dark mode is reachable** via `_RightSideBar` and persists in `sessionStorage`. The swap replaces the whole stylesheet rather than setting `data-bs-theme`, so **there is no dark-mode selector to hook** — new components must be theme-agnostic: build them from `var(--bs-success)` etc. (identical in both themes) and `rgba()` neutrals, never literal `#fff` / `#eff2f7`.

### Boxicons

Many glyphs exist only in the **solid** (`bxs-`) family — `bxs-ship`, `bxs-plane-alt`, `bxs-plane-land`, `bxs-plane-take-off`, `bxs-eraser` — and `bx-refresh` doesn't exist (use `bx-revision`). Verify with `grep "\.bx-name:before" wwwroot/back/assets/css/icons.min.css` before using an icon; a missing one renders as blank space with no error. (`bx-spin` / `bx-tada` are animation modifiers, not glyphs.)

### Permission picker

`Views/Shared/_PermissionModules.cshtml` — a responsive card grid (`row-cols-1 / md-2 / xl-3 / xxl-4`, cards `h-100` so rows align; **no masonry** — Skote ships `masonry.pkgd.min.js` but never initialises it, and it breaks tab order). Each card carries an adaptive **`SELECT ALL` ↔ `DESELECT ALL`** link (`type="button"` — a default `submit` would post the form), rendered only when `group.HasSelectable`.

Three indicator states, styled in `app-extras.css` on a **real `<input type="checkbox">`** (never a div — the form binding, keyboard nav and disabled-doesn't-post behaviour all depend on it):

| State | Look |
|---|---|
| Not granted | empty circle, grey border |
| Granted directly | filled green circle, white check |
| Inherited from a role | green **outline** circle + green check + lock icon, `disabled` |

Catalog descriptions and the "Granted by role: …" source render as **inline muted text** under the label (`.perm-desc` / `.perm-source`) — not tooltips. Only 7 of 28 permissions carry a description, so the height cost is small, and cards use `h-100` so a row still aligns.

### 🚫 Don't use Bootstrap tooltips — show the text inline

Tooltips are **not used anywhere in this project**, and new code shouldn't introduce them. They were tried for permission descriptions and rendered detached from their trigger; the root cause was never fully pinned down (`app.js` double-initialising every `[data-bs-toggle="tooltip"]` before page scripts run was one confirmed factor, and `container: 'body'` did not resolve it). They're also unusable on touch devices and invisible to keyboard users.

**Show the information inline instead** — as a muted second line under the label (`.perm-desc`, `.perm-source` in `app-extras.css`) or as visible badge text. This is what the permission picker, `User/Details` and `_UserForm` all now do. Note `app.js` still runs its own tooltip init over the page; that's vendor code and harmless while no element carries the attribute.

---

## Important Notes

- **`SpecificationEvalutor.cs`** and **`Data/Configrations/`** have intentional typos — do NOT rename them.
- `SuperAdmin` is seeded and assigned on startup; see Authorization System.
- File upload is `IFileStorageService` (Domain) → `FileStorageService` (Repository); `Stream`-based so a future `Travel.Api` can reuse it.
- **`Account/Profile` is still linked from `_Nav` but the action is commented out** — the link 404s. Reviving it needs a `ProfileViewModel` + view.
- **Cookie size:** ~34 permissions ≈ a 2–3 KB encrypted cookie (chunked automatically past 4 KB). Past **~60 permissions**, switch to one comma-joined claim decoded in the handler, or an `ITicketStore`.

---

## Gotchas & Past Mistakes for Claude

> Track every mistake here so it is never repeated.

### 2026-07-17 — Identity registered without `AddEntityFrameworkStores`
**What went wrong:** `AddIdentity<AppUser, IdentityRole>(...)` was configured but never chained to `.AddEntityFrameworkStores<TravelContext>()`, so `IUserStore` couldn't be resolved. This broke the EF **design-time** factory (`dotnet ef migrations add` failed to build the `DbContext`) **and** would crash at runtime (UserManager/SignInManager unconstructable).
**Fix:** chain `.AddEntityFrameworkStores<TravelContext>().AddDefaultTokenProviders()` onto `AddIdentity`.

### 2026-08-01 — Traps hit while building Roles & Permissions
- **A member named `Permissions` shadows the `Permissions` class.** `UserController` needs an action at `/User/Permissions`, but naming the C# method `Permissions` makes every `[Authorize(Policy = Permissions.Users.X)]` in that file fail to compile. **Fix:** name the method `DirectPermissions` and add `[ActionName("Permissions")]`.
- **The base claims factory already unions role claims.** `UserClaimsPrincipalFactory<TUser, TRole>` calls `RoleManager.GetClaimsAsync` for every role, so role permissions were *already* in the cookie. Don't write code to add them — the custom factory only de-duplicates and suppresses claims for SuperAdmin.
- **A `<permission>` TagHelper must not check `User.HasClaim`.** SuperAdmin carries no permission claims (the handler bypasses on the role), so a raw claim check hides the whole UI from the one account that has full access. Go through `IAuthorizationService`.
- **`output.TagName = null` in the TagHelper is load-bearing** — without it the `<permission>` wrapper stays in the DOM and breaks `<ul class="metismenu">`.
- **The TagHelper is C#, not Razor.** `AddRazorRuntimeCompilation` won't pick it up: without a rebuild, `<permission>` renders literally in the page source. That's the symptom to look for.
- **Seed guards must be independent.** The existing `if (!userManager.Users.Any())` is already false on any live DB, so role seeding placed inside it silently never runs. Use a separate `RoleExistsAsync` check.
- **Roles/users can't use `IUnitOfWork.Repository<T>()`** — it's `where T : BaseModel` (int Id), and `IdentityRole`/`AppUser` have string Ids. Use `RoleManager`/`UserManager`. They share the scoped `TravelContext`, so `BeginTransactionAsync` still makes a multi-claim replacement atomic (each `Add/RemoveClaimAsync` is its own `SaveChanges`).
- **Always go through `FindByNameAsync`/`RoleExistsAsync`/`SetRoleNameAsync`.** Identity queries `NormalizedName`; `context.Roles.Add(new IdentityRole("Ops"))` leaves it null and the role becomes invisible to every lookup.
- **Disabled checkboxes don't post** — that's exactly how inherited permissions stay out of the direct-grant list. `readonly` doesn't apply to checkboxes and *would* post.
- **Bootstrap tooltips render detached in this template — don't use them.** Permission descriptions were built as tooltips and appeared far from their icon. Two contributing causes were confirmed (`app.js` initialises every tooltip *before* page scripts, so a page-level `new bootstrap.Tooltip(...)` created a second instance with its own hover listeners; and the default `container: false` puts the tip inside a flex row within a `position: relative` card). Neither de-duplicating nor `container: 'body'` fixed it, so the mechanism was **abandoned entirely** in favour of inline text. Don't re-introduce tooltips expecting them to work — see UI/UX Guidelines.
- **Utility classes that don't exist fail silently.** `bg-*-subtle` and `btn-soft-*` were used ~75 times across 19 views and rendered as bare text / unstyled buttons for months, because a missing CSS class produces no error anywhere. Same for a missing Boxicon. **Grep the stylesheet before using a class or icon** — see the Bootstrap 5.0.0-beta2 note under UI/UX Guidelines.
- **`/Home/Error` needs `[AllowAnonymous]`** once `HomeController` is `[Authorize]`d — it's the `UseExceptionHandler` target, and requiring auth there redirects to Login and swallows the real error.
- **`AccessDeniedPath` pointed at a view that didn't exist**, so every 403 threw "view not found". Adding policy gates makes that path reachable immediately — build the view first.
- **Gate `IsNameAvailable`/`Preview` on `View`, not `Create`.** Both are called from the Edit form too; gating them on Create silently breaks editing.

### 2026-08-01 — Traps around the sequential code generator

- **🔴 Adding soft delete to a coded entity silently breaks code generation.** `Booking` is slated for soft delete. The moment a `HasQueryFilter` lands on it, `LastCodedEntitySpec` stops seeing soft-deleted rows — but those rows still own their `Code` in the (unfiltered) unique index, so the generator re-issues a taken code and every create fails on the retry limit. **Fix when that day comes:** add an `IgnoreQueryFilters` flag to `ISpecifications`/`BaseSpecifications`, honour it in `SpecificationEvalutor`, and set it in `LastCodedEntitySpec`. The warning is written on the spec class itself.
- **Never order the "last code" lookup by `Code`.** The string sort breaks at the padding boundary — `MUSB-1000000` sorts before `MUSB-999999`, so the generator would re-issue `MUSB-1000000` forever. Order by `Id`, which is monotonic with issuance.
- **EF generates `defaultValue: ""` for a new non-nullable string column**, which gives every existing row the *same* code and makes the unique index creation fail on any table with 2+ rows. The `AddEntityCodes` migration is **hand-edited**: two `migrationBuilder.Sql` backfills (`ROW_NUMBER() OVER (ORDER BY Id)`) sit *between* the `AddColumn` calls and the `CreateIndex` calls. Do the same for any future coded entity.
- **The `DuplicateCodeException` translation is a string match on the index name.** `UnitOfWork.IsCodeUniqueViolation` checks `sql.Message.Contains("_Code")` — so a code index **must** be created with an explicit `HasDatabaseName(...)` ending in `_Code`, and unrelated unique indexes must not contain that substring. `IX_Packages_Name` is the near-miss this guards against.
- **`AddAsync` goes *outside* the retry loop.** A failed `SaveChanges` leaves the entity graph tracked as `Added`; re-adding inside the loop would duplicate it. Only the `Code` is reassigned per attempt.
- **Adding a column to a DataTables view shifts every hardcoded index.** `Booking/Index` and `Package/Index` both configure `order: [[n, …]]` and `columnDefs: [{ targets: … }]` by position — inserting the Code column meant bumping both. A missed bump silently sorts or freezes the wrong column.
- **Code immutability lives in the AutoMapper profile, not the view.** `.ForMember(d => d.Code, o => o.Ignore())` on the ViewModel → Entity map is what stops an edit or a tampered POST from overwriting an issued code; rendering it as static text is only the UI half.

### 2026-08-01 — Traps around Block Services

- **🔴 Block Services is a derived read model with no table of its own.** Anything that changes which `BookingStay` rows a query returns silently changes which bookings appear in a supplier's file — with no error and no test failing. A `HasQueryFilter` for soft delete on `Booking`/`BookingStay` would quietly drop bookings from blocks that were already sent out. Same warning as `LastCodedEntitySpec`, different blast radius.
- **A block's key is `(destinationId, checkIn)`, not an Id.** The link can go stale between the list and the click (a booking gets edited, the group drops to one, or empties). `GetBlockAsync` returns `null` only when **nothing** matches the key; a group that dropped to one booking renders as a single-arrival file. **This is the reverse of the original rule** (`null` below 2 bookings, 404) — see the 2026-08-06 entry below for why it changed.
- **🔴 A threshold that answers two questions at once will answer one of them wrong.** `MinimumBookings = 2` originally sat in `BuildSummary`, so it decided both *"does this group exist?"* and *"is this group interesting?"* Those are different questions: the **document** exists for every arrival (the supplier's rooming list), while the **block signal** — this date is busy, negotiate rates — is a filter over those documents. Conflating them silently removed the single-booking files from the product entirely. When a constant gates both existence and display, split it; here the guard moved down to `stays.Count == 0` and the `2` moved up into `GetArrivalsAsync`'s partition.
- **Dedupe stays by `BookingId` before rolling up.** One booking with two stays at the same destination on the same check-in would otherwise be counted twice in the block's pax and room totals. `DistinctByBooking` does this.
- **Grouping can't move into the specification.** `BaseSpecifications`/`SpecificationEvalutor` only do Where → OrderBy → Skip/Take → Includes, and `Booking.Pax` / `LeadGuestName` are computed C# properties EF can't translate. The `GroupBy` is deliberately in memory after materialisation.
- **Inserting a `PermissionCatalog` module mid-list means renumbering.** `Modules` is `.OrderBy(m => m.Order)`; adding BlockServices at `Order = 2` required bumping Packages/Destinations/Users/Roles to 3/4/5/6. Duplicate orders don't throw — the picker just renders in an arbitrary order.
- **The default `from = today` must be conditional.** Defaulting on every request makes it impossible to ever look at past blocks, because clearing the From box just refills it. The filter form posts a hidden `applied=true` and the default only applies when that's absent.
- **ClosedXML's `XLCellValue` has no common type with `int`.** `cell.Value = age is { } a ? a : Blank.Value` doesn't compile (no implicit conversion between `int` and `Blank`). Assign inside an `if` and leave the cell untouched instead.
- **A locked `bin` isn't a compile error.** With the app running, `dotnet build` fails at MSB3021 (file in use) *after* compiling successfully. Use `-t:Compile` (the Razor source generator runs in that target too, so `.cshtml` files are still checked) or `-p:BaseOutputPath=<scratch>` to verify without stopping the app.

### 2026-08-02 — Booking Attachments: Kestrel's default body-size cap silently killed real uploads

- **🔴 The per-file 10MB cap on Booking Attachments doesn't bound the *request*, and nothing did.** Kestrel's own default `MaxRequestBodySize` is 30,000,000 bytes (~28.6MB) — smaller than a handful of real phone photos across the three file categories (Flights/Passports/DomesticFlights each allow many files, no count cap). The request got killed by Kestrel **before MVC model-binds the form**, so `ValidateAttachments` never ran and no `ModelState` error ever reached the view — the user just saw the Edit form silently fail to save, with no indication why. This was reported as "I can't upload attachments using the edit form" and initially looked like a Dropzone/JS bug (it wasn't — the click-upload, drag/drop, and multipart POST paths all worked fine in isolation); it only reproduced when the *total* request body crossed ~28.6MB.
- **Symptom to recognize:** a `POST` that dies with an empty **400** and `Connection: close`, and the server log shows `Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException: Unable to read the antiforgery request token from the posted form.` wrapping `Kestrel.Core.BadHttpRequestException: Request body too large.` — the antiforgery error is a red herring; the real cause is upstream.
- **Fix:** `[RequestSizeLimit(...)]` + `[RequestFormLimits(MultipartBodyLengthLimit = ...)]` on both `BookingController.Create` and `Edit` POST actions (100MB — comfortably above the per-file cap × a realistic file count). Any future action that accepts file uploads needs the same pair of attributes; the framework default is not generous enough for multi-file forms. The constants now live in **`Helpers/AttachmentRules.cs`** (`MaxRequestBytes`, `MaxFileBytes`, `AllowedExtensions`), shared with `BlockServiceController.UploadDocuments` — which carries the same pair.
- **How this was actually caught:** the initial implementation was "verified" with curl posting tiny (<1KB) test files, which stayed well under every limit and proved nothing about real-world payloads. Only posting several ~6MB files (realistic phone-photo size) reproduced it. When testing an upload feature, at least one pass must use file sizes representative of what users will actually attach — small placeholder files can make a broken upload path look fine.

### 2026-08-02 — Edit silently fails (and drops picked files) on any booking whose arrival date has passed

- **🔴 `ValidateBooking`'s "arrival date can't be in the past" check is a Create-only rule that leaked into Edit** via the shared `ValidateBooking(model, package)` helper called from both actions. Any booking whose `ArrivalDate` is now in the past (trivially true for older bookings, since real time keeps advancing past a fixed date) fails this check on **every** Edit save — including edits that have nothing to do with the date, like attaching a passport scan after the trip. Editing a historical booking is a legitimate, common operation; blocking it wasn't intentional, it was collateral from reusing Create's validator.
- **🔴 Compounding bug: `Edit.cshtml`'s `ArrivalDate`/`DepartureDate` inputs had no `<span asp-validation-for="...">`** (unlike `Create.cshtml`, which does). So when this check fired, the error existed in `ModelState` but had **no rendering surface anywhere** — not on the field (no span), not in the model-level summary (it's a field-level error, and `asp-validation-summary="ModelOnly"` only shows empty-key errors). The POST returned 200 with the same Edit view, `ModelState.IsValid == false`, and **zero visible indication why**. Since `<input type="file">` can never be repopulated from a server response (browser security), the attachment(s) the user had just picked in the Dropzone were silently wiped on this round-trip too — from the user's chair this looked exactly like "I can't upload attachments," when the actual failure was an unrelated, invisible date check.
- **Fix:** `ValidateBooking` now takes an `isCreate` bool; the past-date check only runs when `isCreate: true`. Added the missing `asp-validation-for` spans to `Edit.cshtml` for both date fields as defense-in-depth, so any *future* error on these fields is at least visible instead of silently eating the user's in-progress edits.
- **How this was actually caught:** automated testing (Playwright driving a real headless browser, including the exact click → file-picker → Save flow) kept succeeding on freshly-created bookings with today's date, while the user's real report was against an old booking. The repro only appeared when testing against an **existing, aged booking** rather than one just created in the same session — a reminder that "it works in my test" can still miss production data whose state has drifted from what a fresh fixture looks like. Root cause was found by temporarily logging `ModelState.IsValid` + all error keys/messages (`ModelState.Where(kv => kv.Value?.Errors.Count > 0)`) directly in the action — worth reaching for immediately next time a POST silently 200s back to the same view with no visible errors, rather than trying to infer it from rendered HTML.
- **⚠️ Process note:** diagnosing this involved running `dotnet build`/`dotnet run` from a second terminal against the *same* `bin`/`obj` output directories as the user's own live `dotnet watch` session (a separate port). That contention coincided with the user's session going down and not recovering. When a dev server is already running for this solution, prefer attaching diagnostics without a parallel build against the same project — or ask before building/running in a way that touches shared output paths.

### 2026-08-05 — Traps around the editable Create schedule & flight dates

- **The posted `DepartureDate` is stale on Edit — validate against the *computed* departure.** On Edit, `DepartureDate` is rendered `readonly` and only kept in sync by JS as the operator edits the stay rows, so after a hand-edit that hasn't triggered a resync the posted value is wrong. Recompute `ArrivalDate + Σ(Stays.Nights)` when `Stays` were posted, the same formula `Create` uses to derive the real `DepartureDate` before saving. *(The flight-departure check this was originally written for was deleted on 2026-08-11 — see Booking Schedule & Flights. The underlying warning still applies to `ValidateStaySchedule` and anything else that reads the posted `DepartureDate`.)*
- **A blank stay-destination `<select>` posts `DestinationId=0`**, which used to reach `SaveChanges` and die as an opaque FK violation instead of a `ModelState` error. `ValidateStaySchedule` now checks `stay.DestinationId <= 0` explicitly, before the nights/date checks.
- **Making flight fields optional means `TimeOnly`/string must go nullable, not just drop `[Required]`.** A `[Required]` removed from a non-nullable `TimeOnly` still defaults to `00:00` on an empty post — a real-looking time that's wrong. Nullable `TimeOnly?`/`string?` all the way from the entity through the ViewModel is what makes "unknown" distinguishable from "midnight."
- **The cruise embark day must be derived from the schedule *after* it's finalized, not trusted from the posted radio.** Whichever code path rebuilds `booking.Stays` (Create with posted stays, Create generating server-side, Edit, and `Regenerate`) must call `DeriveEmbarkFromStays(booking.Stays)` afterward and fall back to the previous logic only when it returns `null` (non-cruise). Setting `EmbarkDay` from `model.EmbarkDay` before the schedule is final is exactly the bug this derivation exists to prevent.
- **The `Preview` JSON's `embarkOptions` shape is a public contract with the JS, not an implementation detail.** It's built via `EmbarkOptionViewModel.Build(...)` specifically so the fields (`value`/`text`/`extraNights`) match what `_BookingEmbarkOptions.cshtml` renders server-side — MVC's default camelCase JSON policy is what lets the C# `Value`/`Text`/`ExtraNights` properties line up with what `renderEmbark()` reads without an explicit contract test.

### 2026-08-06 — Single Arrivals: a hidden feature nobody could see was missing

- **🔴 The `MinimumBookings = 2` decision was made on an unverified premise about the operator's workflow.** *"A lone booking isn't a block"* is true as a matter of vocabulary and false as a matter of operations: the file is a **rooming list the supplier expects for every arrival**, not a report about group size. Because the guard sat in `BuildSummary`, a one-booking group produced `null` — so it had no list row, no `Details` page, and no `Export`, and typing the URL by hand 404'd. The operator's only recourse was to build that file by hand outside the app.
- **This class of bug is invisible from inside the code.** Nothing errored, no test failed, no log line appeared; the feature looked complete and the hidden rows simply didn't exist as far as the UI was concerned. The only signal was asking the user *"what happens when there's only one booking?"* — which is worth doing for **any** threshold, filter, or `Where` clause that removes rows from a user-facing list. A row that's filtered out has no way to complain.
- **Fix (this change):** `BuildSummary` guards on `stays.Count == 0`; `GetArrivalsAsync` returns `ArrivalGroups(Blocks, Singles)` partitioned at `MinimumBookings`; `SingleArrivals` is a second list in the same controller sharing `BuildListModelAsync`; `Details`/`Export` were **not touched** — deleting the one guard was enough, because the file format was never actually block-specific.
- **The alert on `Index` is expected to become wallpaper.** Single arrivals exist in almost every date range, so a banner that shows on nearly every load stops registering. It's dismissible per page load (Bootstrap's own `alert-dismissible`, no session flag). If it stops being read, the drop-in replacement is a segmented toggle (`Blocks (7) | Single arrivals (23)`) — same actions, same views, only the widget changes. Chosen deliberately by the user over the toggle; noted here so the trade-off isn't rediscovered from scratch.
- **A second table means a second set of DataTables indices.** `SingleArrivals.cshtml` drops the "Bookings" column (always 1), so its `order`/`columnDefs` targets are one lower than `Index.cshtml`'s — the same positional-index trap already documented for `Booking/Index` and `Package/Index`, now with two tables that look nearly identical and must **not** share a config.

### 2026-08-06 — Traps around Block & Booking Status

- **🔴 `BlockStaysSpec` now narrows what reaches a supplier file, deliberately.** Both ctors exclude `Booking.Status == Cancelled`. This is exactly the class of change the Block Services entry flags in red — it silently changes which bookings appear in a file — and it's correct here (a cancelled booking must not inflate PAX or rooms). The knock-on: a group whose **only** booking is cancelled now has zero stays, so `BuildSummary` returns `null`, it vanishes from both lists, and its `Details`/`Export` URLs 404. That's intended; verify it's still intended before adding any further filter.
- **🔴 A threshold that answers two questions will answer one wrong — again.** The same shape as the `MinimumBookings = 2` bug: `RecalculateAsync` must answer *"what do the blocks say?"* without answering *"what did the human decide?"* Hence the `Status is InProgress or Confirmed` guard — recalculation skips `Completed`/`Cancelled` entirely. Drop that guard and confirming a block silently resurrects cancelled bookings.
- **The scaffolder emitted `defaultValue: 0` for `Bookings.Status`, which is not a member of `BookingStatus` (it starts at 1).** Every pre-existing booking would have gotten an out-of-range value that renders as the fallback label and can never be reached again. The migration is **hand-edited to `defaultValue: 1`** (`InProgress`). This is the documented enum-default trap; check it on every future enum column. *(The other predicted hazard — EF proposing a `RenameColumn` because `Bookings.Status` existed and was dropped once before — did **not** materialise, but read the generated migration anyway.)*
- **The `BlockServiceStatus` unique index must not be named with `_Code` in it.** `UnitOfWork.IsCodeUniqueViolation` string-matches `_Code` to raise `DuplicateCodeException`; a `(DestinationId, CheckInDate)` collision must surface as itself. Hence the explicit `HasDatabaseName("IX_BlockServiceStatuses_Destination_CheckIn")`.
- **EF can't translate a composite-key `Contains`.** There's no way to ask for "these exact (destination, check-in) pairs" in one query. `BlockStatusSpec`'s multi-key ctor narrows to the destination set and the min/max date span, and `LoadStatusesAsync` does the exact match in memory — one query instead of one per group.
- **Three more DataTables positional bumps, and the two block tables still must not share a config.** `Booking/Index` actions 6 → **7**; `BlockService/Index` `targets: [5,6]` → **`[5,7]`**; `SingleArrivals` `targets: [4,5]` → **`[4,6]`** (still one lower — it has no "Bookings" column). The Status columns are deliberately left sortable via `data-order="@((int)status)"`, so they sort by workflow order rather than alphabetically ("Confirmed" < "In progress" < "Not booked" is wrong).
- **`Booking/Details` changed model** from the `Booking` entity to `BookingDetailsViewModel`. A `@model` swap plus a global `Model.` → `booking.` rename — and the rename will happily rewrite the `var booking = Model.Booking;` line you just added into a self-reference. Fix that line after the replace.
- **A `@section` can't live inside a `<permission>` tag helper body.** Razor requires sections at the top level of the view; nesting one silently changes nothing or fails to compile. Render the section unconditionally — the handler is harmless when the permission-gated buttons aren't there.
- **`/BlockService` 404s; `/BlockService/Index` doesn't.** The default route is `{controller=Account}/{action=Login}/{id?}` — the default *action* is `Login`, not `Index`. Worth remembering when testing a controller by URL.
- **Razor views ARE compiled at build time here**, despite `AddRazorRuntimeCompilation`. Verified by deliberately breaking a `.cshtml` and watching `dotnet build` fail with `CS0103` pointing at the view. So a green build genuinely covers the views — but it still won't catch a `@model` / controller mismatch, which is a runtime `InvalidOperationException`.

### 2026-08-11 — 🔴 `dotnet run` without an environment hits the **production** database

- **`dotnet run --project Travel.Presentation --no-launch-profile` runs in the `Production` environment**, because `--no-launch-profile` skips `launchSettings.json` (which is what sets `ASPNETCORE_ENVIRONMENT=Development`) and ASP.NET Core's default when the variable is unset is **Production**. That loads `appsettings.Production.json` — whose `DefaultConnection` points at the live **`TravelDB.mssql.somee.com`** server, not `Server=.`. The file is gitignored (commit 8cc6602 "Remove production configuration from source control") so it doesn't show up in `git status` and is easy to forget exists.
- **`Program.cs` calls `MigrateAsync()` on startup**, so merely *starting* the app in that mode applied a pending migration to production — including its `DropColumn`. Nothing prompts, and the only visible signal is that every `Executed DbCommand` in the log takes **~160–210 ms instead of 3–5 ms**. That latency gap is the tell: local SQL Server is single-digit milliseconds.
- **What actually happened:** while verifying the Nationalities feature, the app was started this way. It migrated the remote DB (dropping `Bookings.Nationality` and its data there), and the verification steps then created two nationality rows and set booking `MUSB-000001`'s nationality. The test rows were removed afterwards; the dropped column is gone on both databases either way, which was the intended (user-approved) change — but it landed on production without anyone choosing the moment.
- **Rules for next time:**
  - Always pass the environment explicitly when running or migrating: `$env:ASPNETCORE_ENVIRONMENT="Development"` before `dotnet run` / `dotnet ef database update`, or use `--launch-profile`. Never `--no-launch-profile` on this solution.
  - Before any `database update` or app start that could migrate, **check which server the connection string resolves to** — `appsettings.Production.json` existing on disk means the wrong default is one flag away.
  - `dotnet ef database update` (run separately) *did* correctly target the local DB, because the EF tooling defaults `ASPNETCORE_ENVIRONMENT` to `Development`. The two commands disagree about the default — don't assume that because the migration went local, the app run will too.

### 2026-08-11 — Moving domestic flight docs to the arrival group, and `NeedsAmendment`

- **🔴 "The booking is Confirmed once every block it belongs to is Confirmed" was quietly wrong the moment a block's membership could change.** A booking created into an already-confirmed set of blocks landed `Confirmed` immediately — CLAUDE.md even called that "intended". It isn't: the supplier confirmed a group that didn't include it, and nothing anywhere said so. The general shape to watch for: **a derived value that reads a status flag instead of the evidence behind it.** The fix was to stop asking "is the block Confirmed?" and start asking "does the confirmation still describe *this booking*?" — which needs a snapshot of what was confirmed, not a boolean.
- **Fingerprint the facts, don't timestamp the row.** The first design for detecting a change was a `Booking.UpdatedAt` column compared against the block's confirmed-at. Comparing a *fingerprint of the supplier-relevant fields* instead needs no migration on `Booking` and produces no false flags — an operator fixing a typo in the notes or filling in a flight number two weeks later doesn't re-open a settled block. Only add a timestamp column when you genuinely need "when", not "whether".
- **A derived enum member must be rejected on the write path explicitly.** `Enum.IsDefined(BlockStatus.NeedsAmendment)` is `true`, so the existing guard let it straight through — it needed its own check in `SetStatusAsync`. And the modal's `<select>` has no option for it, so `block-status.js` had to map a derived `4` back to `3`: jQuery's `.val(4)` on a select with no matching option silently selects **nothing**, the field is then omitted from `serialize()`, and the server sees `status=0` → "Unknown status." Any derived-only enum value needs both halves.
- **The `_Code` index-name trap, again.** `UnitOfWork.IsCodeUniqueViolation` string-matches `_Code`, so `IX_BlockConfirmedBookings_Block_Booking` is named deliberately — a `(block, booking)` collision must surface as itself, not as `DuplicateCodeException`.
- **Two FKs into one child table trip SQL Server's multiple-cascade-path rule (1785).** `BlockConfirmedBooking` points at both `BlockServiceStatus` and `Booking`; the group FK is `Cascade` (ownership) and the booking FK is `NoAction`, with `BookingController.Delete` clearing the rows by hand. Don't "fix" the manual delete by flipping the FK to Cascade.
- **The `AttachmentCategory` table is `BookingAttachment`, singular** — it has no `DbSet`, so EF named it after the entity. A hand-written `migrationBuilder.Sql("DELETE FROM BookingAttachments …")` would have silently failed at runtime. **Check the actual table name in `sys.tables` before writing raw SQL in a migration**, especially for entities reached only through a navigation.
- **Deleting rows in a migration cannot delete the files they point at.** The `Category = 3` rows were removed by the migration, but the PDFs under `wwwroot/Files/BookingAttachments/` had to be captured with a `SELECT FileUrl … WHERE Category = 3` **before** applying it — afterwards nothing distinguishes them from the Flight/Passport files. The same cleanup is still outstanding on any other database this migration is applied to.
- **`booking-attachments.js` was already generic and nobody noticed.** It drives every `.attachment-dropzone`, pairing `#dropzone-{key}` with `#input-{key}` — no booking knowledge at all. It was renamed `attachment-dropzones.js` and reused verbatim for the block upload rather than copied. Check whether a "feature-specific" script actually is one before writing a second copy.
- **A `<permission>` tag helper can't express "render differently".** The documents card must show its files to a `BlockServices.View`-only user while hiding the upload/delete controls, so `Details.cshtml` resolves `IAuthorizationService.AuthorizeAsync(User, …ManageDocuments)` once into a local, exactly as it already does for `canOpenBooking`.

### 2026-08-13 — Traps around the Flights report

- **🔴 `dotnet build -t:Compile` does NOT refresh `Travel.Presentation/bin`.** The `Compile` target compiles each project but skips the copy-to-output step, so `Travel.Presentation/bin/Debug/net9.0/Travel.Domain.dll` keeps the **previous** build. Starting the app with `--no-build` after a `-t:Compile` then loads the stale Domain assembly, and **runtime Razor compilation fails against it**: `_Sidebar.cshtml(71,64): error CS0117: 'Permissions' does not contain a definition for 'Reports'` — a 500 on every page, pointing at a `.cshtml` that is perfectly correct. The `-t:Compile` trick documented under Block Services is for **verifying** a change compiles while the app is running; it is not a build you can then run. To actually run, do a full `dotnet build` (which needs the app stopped, since a locked `bin` fails at MSB3021).
- **`$home` is a read-only PowerShell variable.** `$home = Invoke-WebRequest ...` in a verification script dies with `Cannot overwrite variable HOME because it is read-only or constant` — nothing to do with the app. Same for `$host`, `$pwd`, `$input`.
- **ClosedXML writes its sheet XML with the `x:` namespace prefix.** Inspecting a generated `.xlsx` by regex means matching `<x:row`, `<x:mergeCell`, `<x:t>` — searching for the unprefixed names returns zero hits on a file that is actually fine, which reads exactly like an empty sheet.
- **The `applied`-idiom default uses `DateTime.UtcNow`,** so for a UTC+2/+3 operator the From box defaults to *yesterday* for the first few hours after local midnight. Harmless here (a lower bound one day early only widens the range) and consistent with `BlockServiceController`, but don't copy the pattern anywhere the default is an **upper** bound.
- **`GetAllAsync(spec)` returns `IEnumerable<T>`, not `IReadOnlyList<T>`.** Worth remembering when writing a helper that takes the result — the plan-level signature has to be `IEnumerable`, or materialise first.

### Reusable guardrails (from the same architecture — apply as the domain is built)
- **SQL Server cascade cycles (error 1785):** don't set every FK to `Cascade`. Use `Cascade` only for ownership FKs, `NoAction` for reference FKs to break multiple cascade paths to the same table.
- **EF enum default value:** don't `HasDefaultValue(1)` (int) on an enum property — type mismatch at design time. Initialize the default in the C# model instead and omit the DB default.
- **Upload directory:** create the target folder before opening a `FileStream` (`Directory.CreateDirectory` is a safe no-op if it exists). `FileStorageService` already does this — keep it for any new upload folder.
- **Submit button "Saving…" stuck:** gate the disable-button/spinner on validity — `if (!$(form).valid()) return;` *before* disabling — so a client-side validation failure doesn't permanently disable the button.
- **CRLF vs LF edits:** if a targeted string replace silently fails to match, verify with a fresh read and fall back to overwriting the whole file.
