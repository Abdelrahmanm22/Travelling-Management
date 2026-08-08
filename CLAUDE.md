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
| `BlockStatus` | `NotBooked=1`, `InProgress=2`, `Confirmed=3` | `Travel.Domain/Enums/` |
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

### Missing domestic-flight document flag

An operator working an arrival file needs to see which bookings still have no **Domestic flight** attachment. The rule, on `Booking` as two computed properties beside `Pax`/`LeadGuestName`:

> **`RequiresDomesticFlight`** — the booking has a cruise stay **or** its itinerary touches 2+ distinct destinations. **`MissingDomesticFlightDoc`** — requires one and has zero `AttachmentCategory.DomesticFlight` attachments.

**🔴 The flag cannot be computed from `BlockStaysSpec`'s own includes, and adding `Booking.Stays` there would look like it works.** That spec is windowed by date and optionally narrowed to one destination, so its `Booking.Stays` is the *filtered* itinerary — a Cairo-only filter hides the Hurghada leg and the "2+ destinations" half silently reads `false`, flag gone, no error. `BookingDocsSpec` (`Specifications/Bookings/`) loads the **whole** itinerary + attachments for the booking ids in play; `LoadMissingDomesticDocsAsync` runs it once per request and returns a `HashSet<int>` that both `BuildSummary` and `BuildBookingLine` read. It's also cheaper — `BlockStaysSpec` already carries `Guests` + `Rooms`, and EF JOINs multiple collection includes into one cartesian result set.

**Screen-only, by the "is this ours, or theirs?" rule.** A missing *internal* document is the operator's concern, not the supplier's, so `BlockExcelExporter` doesn't write it and the Details badge carries `d-print-none` — same reasoning as the email links.

`BlockSummary.MissingDomesticFlightCount` (a count, so the icon's `title` can say "2 of 5") renders as an inline `bx-error-circle` in the **Destination cell** of `Index`/`SingleArrivals` — deliberately **not a new column**, so no DataTables positional index moves. `BlockBookingLine.MissingDomesticFlightDoc` renders as a badge on the Details booking card. No filter, no alert banner, no permission, no migration.

---

## Block & Booking Status

Two statuses that are **linked, not independent**.

| | Values | Owned by |
|---|---|---|
| **`BlockStatus`** (per arrival group) | `NotBooked` → `InProgress` → `Confirmed` | the operator, via `/BlockService` |
| **`BookingStatus`** | `InProgress` / `Confirmed` | **derived** — never set by hand |
| | `Completed` / `Cancelled` | the operator, via `Booking/SetStatus` |

**The rule, in one line:** a booking is `Confirmed` exactly when **every** arrival group it belongs to is `Confirmed`, and drops back to `InProgress` the moment one isn't. A Cairo + Cruise + Hurghada booking needs all three. `RecalculateAsync` (`BlockStatusService`) is the single owner of this and **never touches a booking whose status is `Completed` or `Cancelled`** — those are the human's.

**`BlockServiceStatus` is the first persisted row Block Services has ever had.** The groups themselves are still derived from live `BookingStay` rows on every request; this side table just hangs off the same identity a group already had, `(DestinationId, CheckInDate)`. Rows are **created lazily** on the first status change — a group with no row reads as `NotBooked` (`BlockStatusInfo.NotBooked`), and an orphaned row (every booking moved off that date) is harmless: it simply re-applies if a booking returns.

**Two permanent, independent email links** — `RequestEmailLink` (the email that requested the block) and `ConfirmationEmailLink` (the supplier's reply). Named for the business fact, not the status. They are **only ever written, never cleared**: a blank field in the modal leaves the stored value alone, so confirming can't lose the request thread and moving back to `NotBooked` can't lose either. Validated server-side to absolute `http`/`https` (they render as anchors — a `javascript:` value must not survive).

**The links are screen-only.** They are internal references into the operator's own mailbox, and both the `.xlsx` and the printed sheet go **to the supplier** — so `BlockExcelExporter` doesn't write them and the Details block carries `d-print-none`. The block **status** does appear in both. Anything added to the supplier-facing file needs the same question asked of it: *is this ours, or theirs?*

| Decision | Why |
|---|---|
| Booking `Confirmed` requires **all** blocks, not the first | The booking isn't really confirmed while two hotels are still unbooked; a partial confirmation that reads "Confirmed" is a lie the operator would act on |
| Un-confirming a block **reverts** its bookings | Re-evaluation runs in both directions, so the badge always reflects current reality rather than a high-water mark |
| `InProgress`/`Confirmed` are **not** hand-settable | `BookingController.SetStatus` rejects them outright. Offering them would let a booking claim a confirmation no supplier gave |
| "Reactivate" posts **no** status | It hands the booking back to the derivation instead of guessing a value |
| Cancelled bookings are **excluded from Block Services entirely** | A supplier file that counts a cancelled booking over-states rooms. `BlockStaysSpec` filters them in **both** ctors — see the red-flag note below |
| `Status` is `Ignore()`d in `BookingProfile` (VM → entity) | Same reasoning as `Code`/`EmbarkDay`: an ordinary Edit save (or a tampered POST) must not overwrite a derived value |

**Recalculation triggers:** `SetStatusAsync` (both directions), and `BookingController.Create`/`Edit`/`Regenerate` — all three can change which blocks a booking belongs to. A booking created into an already-confirmed set of blocks therefore lands `Confirmed` immediately; that follows from the rule and is intended.

**Permissions:** `BlockServices.ChangeStatus`, `Bookings.ChangeStatus` (both added to existing modules, so no `PermissionCatalog` renumbering). 30 total — still well under the ~60 cookie ceiling.

**Presentation:** **`Travel.Presentation/Helpers/StatusBadges.cs`** is the single source of truth for label + badge class + icon (`ForBlock` / `ForBooking`), mirroring `PermissionIcons`. There is no `GetDisplayName()` helper in this project, which is why enum display strings otherwise get hand-written per view. `ExcelFormat.BlockStatusText`/`BookingStatusText` are its counterpart for exports.

**Files:** enums in `Travel.Domain/Enums/BlockStatus.cs` + `BookingStatus.cs`; entity `Travel.Domain/Models/BlockServiceStatus.cs`; config `BlockServiceStatusConfig.cs`; `IBlockStatusService` + `BlockStatusChange` in `Travel.Domain/Services/BlockServices/`; `BlockStatusInfo`/`BookingBlockLine` in `BlockDtos.cs`; `BlockStatusSpec` + `BookingsForStatusSpec` in `Specifications/`; impl `Travel.Services/BlockServices/BlockStatusService.cs`; UI in `Views/BlockService/_BlockStatusCell.cshtml` + `_BlockStatusModal.cshtml`, `Views/Booking/_BookingStatusActions.cshtml`, and `wwwroot/back/assets/js/block-status.js`.

---

## Printable Documents & Excel Export

Two pages are documents an operator prints or sends out: **`BlockService/Details`** (a supplier's arrival file — a block or a single arrival, same document) and **`Booking/Details`** (a single booking). Both carry `Print` + `Excel` buttons, gated on `BlockServices.Export` / `Bookings.Export` respectively.

**Print** — put **`.print-sheet`** on the card that *is* the document. The `@media print` block in `app-extras.css` then hides `#page-topbar` / `.vertical-menu` / `.footer` / `.right-bar`, zeroes the content padding, strips the card border, keeps `thead` with its rows (`display: table-header-group`) and avoids splitting rows across pages. Buttons carry **`d-print-none`**. Adding print support to a new page is just those two classes — don't write a second `@media print` block.

**Excel** — one exporter per document in `Travel.Presentation/Services/`, sharing **`ExcelFormat`** (content type, room/guest/category/flight text, `FormatRooms`, `SheetName`, `SanitizeFileName`, `WriteHeaderRow`, `WriteField`). Put any new formatting there rather than in an exporter, so a booking sheet and a block file can't disagree on how a `RoomType` is spelled.

| Exporter | Output | Filename |
|---|---|---|
| `BlockExcelExporter` | Arrival file — one row per booking + indented guest sub-rows, frozen header, TOTAL row. **Same layout at any booking count**; only the label changes | `Block_{Destination}_{yyyy-MM-dd}.xlsx`, or `Arrival_…` when the group holds one booking |
| `BookingExcelExporter` | Booking — facts block, `GUESTS` table, `SCHEDULE` table with computed check-outs + total nights | `Booking_{Code}.xlsx` |

ClosedXML lives **only** in `Travel.Presentation` so `Travel.Services` stays NuGet-free.

---

## Booking Attachments

Bookings carry supporting documents: **Flights**, **Passports**, **Domestic flights** (each many files) and a **Contract** (a link, not a file). All four live in one table, **`BookingAttachment`** (`Travel.Domain/Models/`) — `Category` (`AttachmentCategory` enum), `FileUrl`/`OriginalFileName` for the three file categories, `LinkUrl` for `Category = Contract`. One entity instead of a separate scalar column on `Booking` keeps the Create/Edit/Details code uniform across all four categories.

| Decision | Why |
|---|---|
| Contract is a row in the same table (`LinkUrl` set, `FileUrl` null), not a `Booking.ContractLink` column | Same CRUD path as the file categories — no special-cased scalar to keep in sync |
| Attachments are **additive on Edit**, not wholesale-rebuilt like `Stays`/`Guests`/`Rooms` | Files can't be regenerated from form state — `RemoveAttachmentIds` (checkbox list, posted ids) deletes specific rows; new uploads are appended; the Contract row is upserted by `Category` |
| Files saved via the existing `IFileStorageService` into a `BookingAttachments` folder — no new storage code | Same contract as `PackageController`'s cover image (`SaveFileAsync` → GUID-prefixed name → public `/Files/{folder}/{name}` URL stored on the entity) |
| Validation is server-side only (`BookingController.ValidateAttachments`): `.pdf/.jpg/.jpeg/.png`, 10MB/file | Client-side `acceptedFiles`/`maxFilesize` on the Dropzone instances mirror this but aren't trusted |
| `Create`/`Edit` POST carry `[RequestSizeLimit]` + `[RequestFormLimits(MultipartBodyLengthLimit = ...)]` (100MB) | Kestrel's own default request-body cap (~28.6MB) is smaller than a handful of real photos across three unlimited-count categories — see the 2026-08-02 Gotcha below, this silently killed real uploads before any of our validation ran |
| No new permissions | Gated by the existing `Bookings.Create`/`Edit`/`View` policies — attachments are just part of a booking, not a separately-grantable capability |

**Deleting a booking must delete its files, not just its rows.** The DB cascade (`BookingAttachmentConfig`, ownership FK) removes the `BookingAttachment` rows, but not the physical files on disk — `BookingController.Delete` loops `booking.Attachments` and calls `_fileStorage.DeleteFile(...)` before deleting the booking, same as `PackageController.Delete` does for the cover image.

**Dropzone.js UI (`Views/Booking/_BookingAttachments.cshtml` + `wwwroot/back/assets/js/booking-attachments.js`)** — the Skote template ships Dropzone.js unused; this is its first use. Each category renders a `.dropzone` div plus a hidden native `<input type="file" multiple name="FlightFiles">` (etc.) — Dropzone never uploads anything itself (`autoProcessQueue: false`, `url: '#'`); instead, on every `addedfile`/`removedfile`/`error` event the script rebuilds the hidden input's `FileList` via the `DataTransfer` API, filtering out files still in `Dropzone.ERROR` status, so the files travel with the ordinary form POST (`enctype="multipart/form-data"`, added to both `Create.cshtml` and `Edit.cshtml`). `Dropzone.autoDiscover = false` is required at the top of the script — the vendor file auto-scans for `.dropzone` elements on `DOMContentLoaded` otherwise and double-inits.

**Files:** enum in `Travel.Domain/Enums/AttachmentCategory.cs`, entity in `Travel.Domain/Models/BookingAttachment.cs`, config in `Travel.Repository/Data/Configrations/BookingAttachmentConfig.cs`, form fields + display DTO in `BookingViewModel.cs`, all controller logic in `BookingController.cs` (`ValidateAttachments`, `BuildNewAttachmentsAsync`, `ToAttachmentViewModels`), shared partial `Views/Booking/_BookingAttachments.cshtml` used by both Create and Edit, Details renders its own grouped read-only list.

---

## Booking Schedule & Flights

`Booking.ArrivalDate`/`DepartureDate` are the **package** dates — they drive the itinerary generator and Block Services grouping. Flight details are a separate, optional, operator-entered fact: `ArrivalFlightNo?`/`ArrivalTime?`/`DepartureFlightNo?`/`DepartureTime?` (all nullable — a blank form field mapping onto a non-nullable `TimeOnly` would silently store, and display, a real-looking `00:00`) plus `FlightArrivalDate?`/`FlightDepartureDate?`. A guest routinely flies in the night before the package starts and out after it ends, so the flight date is never assumed to equal the package date; a null flight date **displays** as a fallback to the package date but is stored separately. `ValidateFlightDates` (`BookingController`) requires `FlightArrivalDate <= ArrivalDate` and `FlightDepartureDate >= departure`, where `departure` is **computed from the posted `Stays`** (`ArrivalDate + Σnights`) whenever stays were posted — not the `DepartureDate` field itself, which is `readonly` on Edit and only resynced by JS, so it can be stale relative to a hand-edited schedule.

**Create posts a client-built schedule instead of always regenerating server-side.** `Create.cshtml` calls the same `Preview` JSON endpoint Edit's "Regenerate" button uses, renders the stay rows in an editable table, and posts them as `model.Stays`. `BookingController.Create` uses them verbatim (`BuildStaysFromInputsAsync`, the same path `Edit` already used) whenever `model.Stays` is non-empty; an empty list means the client script never ran (JS blocked, stale page) and Create falls back to generating server-side from the package, same as before this change.

**The stored `EmbarkDay` is derived from the final schedule, not trusted from the posted radio.** `DeriveEmbarkFromStays` reads the actual `CheckInDate` of the cruise stay in the final `Stays` list and converts it to a `WeekDays` flag (`1 << (int)DayOfWeek` — see the `[Flags]` layout in `WeekDays.cs`: Sunday=1 … Saturday=64; returns `null` for a non-cruise package). This runs after `Stays` is rebuilt in **both** Create and Edit (and after `Regenerate`), so a hand-edited schedule and the stored `EmbarkDay` can never disagree — the posted `EmbarkDay` value only matters as the *first draft* the schedule table is built from.

**The embark-sailing picker has one server-side source of truth, `EmbarkOptionViewModel.Build`, rendered by one partial, `_BookingEmbarkOptions.cshtml`, shared by Create and Edit.** Edit renders its options server-side (not via AJAX) specifically so a page load doesn't call `Preview` and silently regenerate the saved schedule. `booking-form.js`'s `renderEmbark()` rebuilds the same markup client-side from the `<template id="embark-option-template">` in that partial whenever the package/arrival/sailing changes after load — a markup change to the partial must be mirrored in `renderEmbark()`, and vice versa.

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
`Index(from, to, destinationId, status, applied)` / `SingleArrivals(same args)` / `Details(destinationId, checkIn)` / `Export(destinationId, checkIn)` / **`SetStatus`** (JSON POST — the controller's only write; see Block & Booking Status). The two lists share **`BuildListModelAsync`** (and one `BlockServiceListViewModel` carrying both `Blocks` and `Singles`), so their filter semantics can't drift: it defaults `from` to today **only when `applied == false`**, and the filter form posts a hidden `applied=true` so a deliberately cleared From box isn't silently re-filled. `Details`/`Export` return `NotFound()` only when **no** stay matches the key.

### RoleController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable`, each `[Authorize(Policy = Permissions.Roles.X)]`. Permissions are edited inline on the form, so `Roles.ManagePermissions` is enforced **inside** the POST and via `<permission>` around the picker.

### UserController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` / `ToggleActive` / `ResetPassword` (all JSON for the AJAX ones) / `Permissions` GET+POST / `IsUserNameAvailable` / `IsEmailAvailable`.
The C# methods behind `/User/Permissions` are named **`DirectPermissions`/`DirectPermissionsPost`** with `[ActionName("Permissions")]` — a member called `Permissions` would shadow the `Permissions` static class and break every `[Authorize(Policy = Permissions.X)]` in the file.

### Views
`Account/Login`+`AccessDenied`, `Home/Index`+`Privacy`, `Booking/`, `BlockService/` (`Index`+`SingleArrivals`+`Details`+`_ArrivalFilters`), `Package/`, `Destination/`, `Role/`, `User/`, and `Shared/` partials (`_Layout`, `_AuthLayout`, `_Sidebar`, `_Nav`, `_Head`, `_Scripts`, `_Footer`, `_RightSideBar`, `_Notifications`, `_ValidationScriptsPartial`, **`_PermissionModules`**, `Error`).
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

Modules: **Bookings** (+`Regenerate`, `Export`, `ChangeStatus`), **BlockServices** (`View`, `Export`, `ChangeStatus`), **Packages**, **Destinations**, **Users** (+`ToggleActive`, `ResetPassword`, `AssignRoles`, `ManagePermissions`), **Roles** (+`ManagePermissions`).

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
├── Dockerfile                          (build → final [prod] / dev — see Docker)
├── docker-compose.yml                  (prod: sqlserver + web)
├── docker-compose.override.yml         (dev: dotnet watch + bind mount, applied by default)
├── .dockerignore, .env.example         (.env is gitignored)
├── .claude/commands/commit.md          (custom /commit workflow)
├── Travel.Domain/
│   ├── IUnitOfWork.cs
│   ├── Authorization/  AppClaimTypes.cs, Permissions.cs, AppRoles.cs, PermissionCatalog.cs
│   ├── Enums/          Gender, GuestType, PackageCategory, RoomType, WeekDays, AttachmentCategory,
│   │                   BlockStatus, BookingStatus
│   ├── Exceptions/     DuplicateCodeException.cs
│   ├── Models/         BaseModel, ICodedEntity, Booking*, BlockServiceStatus, Destination,
│   │                   Package*; Models/Identity/AppUser.cs
│   ├── Repositories/IGenericRepository.cs
│   ├── Services/       IFileStorageService.cs, OperationResult.cs, Itinerary/,
│   │                   CodeGeneration/ICodeGeneratorService.cs + CodePrefixes.cs,
│   │                   BlockServices/IBlockServiceQuery.cs + IBlockStatusService.cs + BlockDtos.cs,
│   │                   Identity/IRoleService.cs, Identity/IUserAccountService.cs
│   └── Specifications/ BaseSpecifications.cs, ISpecifications.cs, Packages/,
│                       Bookings/BookingWithDetailsSpec.cs + BookingsForStatusSpec.cs
│                                + BookingDocsSpec.cs,
│                       BlockServices/BlockStaysSpec.cs + BlockStatusSpec.cs,
│                       CodeGeneration/LastCodedEntitySpec.cs
├── Travel.Repository/
│   ├── GenericRepository.cs, SpecificationEvalutor.cs, UnitOfWork.cs
│   ├── Storage/FileStorageService.cs
│   └── Data/TravelContext.cs, AppIdentityDbContextSeed.cs,
│       Configrations/…Config.cs, Migrations/
├── Travel.Services/
│   ├── ItineraryGeneratorService.cs
│   ├── BlockServices/BlockServiceQueryService.cs + BlockStatusService.cs
│   ├── CodeGeneration/CodeGeneratorService.cs
│   └── Identity/RoleService.cs, UserAccountService.cs
└── Travel.Presentation/
    ├── Program.cs, appsettings.json
    ├── Authorization/  PermissionRequirement, PermissionAuthorizationHandler,
    │                   PermissionPolicyProvider, AppUserClaimsPrincipalFactory
    ├── TagHelpers/PermissionTagHelper.cs
    ├── Helpers/        AvatarHelper.cs, PermissionIcons.cs, StatusBadges.cs
    ├── Services/        ExcelFormat.cs (shared), BlockExcelExporter.cs, BookingExcelExporter.cs
    │                    (ClosedXML — web project keeps the NuGet out of Travel.Services)
    ├── Controllers/    Account, Home, Booking, BlockService, Package, Destination, Role, User
    ├── ViewModels/, MappingProfiles/
    ├── Views/ (Account, Home, Booking, BlockService, Package, Destination, Role, User, Shared)
    └── wwwroot/
        ├── css/app-extras.css   (ONLY place for project CSS — see UI/UX Guidelines)
        ├── back/                (Skote template: css/, js/, libs/, images/)
        ├── Files/               (uploads: Packages/, Users/)
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

## Docker

Two targets from one multi-stage `Dockerfile`, both driven by compose. `docker compose up` gives **dev**; `docker compose -f docker-compose.yml up` gives **prod** (the override file is what makes dev the default).

| | dev | prod |
|---|---|---|
| Stage / image tag | `dev` — `travel-management-web:dev` (857 MB) | `final` — `travel-management-web:prod` (354 MB) |
| Base | `sdk:9.0`, runs **root** | `aspnet:9.0`, runs **non-root** (`$APP_UID` = 1654) |
| Entry | `dotnet watch run --no-launch-profile` | `dotnet Travel.Presentation.dll` |
| Source | repo bind-mounted at `/src` | published output baked into `/app` |
| Log path | `/var/log/travel` (via `TRAVEL_LOG_PATH`) | `/app/Logs` (Serilog default, relative to WORKDIR) |

App on **`localhost:5145`** (matching `launchSettings.json`), SQL Server on **1434** (1433 is taken by another project's container). `MSSQL_SA_PASSWORD` comes from `.env` (gitignored; `.env.example` is the committed template).

**Both targets share one database and one `uploads` volume**, so switching between them keeps DB rows and files in agreement. Four named volumes: `sqldata`, `uploads`, `logs`, `dpkeys`.

Key decisions:

| Decision | Why |
|---|---|
| `appsettings.json` is **not** edited | `ConnectionStrings__DefaultConnection` overrides it via env, so the Windows-auth string stays valid for a native `dotnet run` |
| SQL Server gets a **healthcheck** + `depends_on: service_healthy` | `Program.cs` only *logs* a failed `MigrateAsync`, so without ordering the app boots against an unmigrated DB and looks broken with no error |
| Startup migrate/seed retries 10× / 3s | Belt-and-braces on top of the healthcheck. **Deliberately not `EnableRetryOnFailure`** — see the Gotchas entry |
| `fontconfig` + `fonts-liberation` in **both** stages | ClosedXML's `AdjustToContents()` measures text; the base image ships no fonts and the `.xlsx` exports throw |
| Distinct image tag per target | Same tag means the dev build silently overwrites the prod image |
| Dev masks each project's `bin`/`obj` with anonymous volumes | `project.assets.json` carries host NuGet paths, and a shared `bin` is the output contention noted below |
| Dev stays root | A bind-mounted Windows volume plus a non-root uid is a permissions fight with no upside |

---

## Important Notes

- **`SpecificationEvalutor.cs`** and **`Data/Configrations/`** have intentional typos — do NOT rename them.
- `SuperAdmin` is seeded and assigned on startup; see Authorization System.
- File upload is `IFileStorageService` (Domain) → `FileStorageService` (Repository); `Stream`-based so a future `Travel.Api` can reuse it.
- **`Account/Profile` is still linked from `_Nav` but the action is commented out** — the link 404s. Reviving it needs a `ProfileViewModel` + view.
- **Cookie size:** ~28 permissions ≈ a 2–3 KB encrypted cookie (chunked automatically past 4 KB). Past **~60 permissions**, switch to one comma-joined claim decoded in the handler, or an `ITicketStore`.

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
- **Fix:** `[RequestSizeLimit(MaxAttachmentsRequestBytes)]` + `[RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentsRequestBytes)]` on both `BookingController.Create` and `Edit` POST actions (100MB — comfortably above `MaxAttachmentBytes` × a realistic file count). Any future action that accepts file uploads needs the same pair of attributes; the framework default is not generous enough for multi-file forms.
- **How this was actually caught:** the initial implementation was "verified" with curl posting tiny (<1KB) test files, which stayed well under every limit and proved nothing about real-world payloads. Only posting several ~6MB files (realistic phone-photo size) reproduced it. When testing an upload feature, at least one pass must use file sizes representative of what users will actually attach — small placeholder files can make a broken upload path look fine.

### 2026-08-02 — Edit silently fails (and drops picked files) on any booking whose arrival date has passed

- **🔴 `ValidateBooking`'s "arrival date can't be in the past" check is a Create-only rule that leaked into Edit** via the shared `ValidateBooking(model, package)` helper called from both actions. Any booking whose `ArrivalDate` is now in the past (trivially true for older bookings, since real time keeps advancing past a fixed date) fails this check on **every** Edit save — including edits that have nothing to do with the date, like attaching a passport scan after the trip. Editing a historical booking is a legitimate, common operation; blocking it wasn't intentional, it was collateral from reusing Create's validator.
- **🔴 Compounding bug: `Edit.cshtml`'s `ArrivalDate`/`DepartureDate` inputs had no `<span asp-validation-for="...">`** (unlike `Create.cshtml`, which does). So when this check fired, the error existed in `ModelState` but had **no rendering surface anywhere** — not on the field (no span), not in the model-level summary (it's a field-level error, and `asp-validation-summary="ModelOnly"` only shows empty-key errors). The POST returned 200 with the same Edit view, `ModelState.IsValid == false`, and **zero visible indication why**. Since `<input type="file">` can never be repopulated from a server response (browser security), the attachment(s) the user had just picked in the Dropzone were silently wiped on this round-trip too — from the user's chair this looked exactly like "I can't upload attachments," when the actual failure was an unrelated, invisible date check.
- **Fix:** `ValidateBooking` now takes an `isCreate` bool; the past-date check only runs when `isCreate: true`. Added the missing `asp-validation-for` spans to `Edit.cshtml` for both date fields as defense-in-depth, so any *future* error on these fields is at least visible instead of silently eating the user's in-progress edits.
- **How this was actually caught:** automated testing (Playwright driving a real headless browser, including the exact click → file-picker → Save flow) kept succeeding on freshly-created bookings with today's date, while the user's real report was against an old booking. The repro only appeared when testing against an **existing, aged booking** rather than one just created in the same session — a reminder that "it works in my test" can still miss production data whose state has drifted from what a fresh fixture looks like. Root cause was found by temporarily logging `ModelState.IsValid` + all error keys/messages (`ModelState.Where(kv => kv.Value?.Errors.Count > 0)`) directly in the action — worth reaching for immediately next time a POST silently 200s back to the same view with no visible errors, rather than trying to infer it from rendered HTML.
- **⚠️ Process note:** diagnosing this involved running `dotnet build`/`dotnet run` from a second terminal against the *same* `bin`/`obj` output directories as the user's own live `dotnet watch` session (a separate port). That contention coincided with the user's session going down and not recovering. When a dev server is already running for this solution, prefer attaching diagnostics without a parallel build against the same project — or ask before building/running in a way that touches shared output paths.

### 2026-08-05 — Traps around the editable Create schedule & flight dates

- **🔴 Validate the flight departure date against the *computed* departure, not the posted `DepartureDate` field.** On Edit, `DepartureDate` is rendered `readonly` and only kept in sync by JS as the operator edits the stay rows — after a hand-edit that hasn't triggered a resync, the posted value is stale. `ValidateFlightDates` instead recomputes `ArrivalDate + Σ(Stays.Nights)` when `Stays` were posted, the same formula `Create` uses to derive the real `DepartureDate` before saving. Validating against the stale field would reject (or silently accept) against the wrong date.
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

### 2026-08-08 — Traps hit while dockerizing

- **🔴 `MapStaticAssets()` does not serve runtime uploads, and the failure is a silent 404.** `Program.cs` called `app.MapStaticAssets()` with **no `app.UseStaticFiles()`**. `MapStaticAssets` serves only what's in the build-time static-web-assets manifest, so every file `IFileStorageService` writes — package covers, user avatars, **booking attachments** — 404'd in *every* environment, Windows included. This was a **pre-existing bug**, not caused by Docker; containerizing just made it visible because the container started with an empty uploads volume and the first upload was tested end-to-end. Build-time assets (Skote CSS/JS, the logo) kept returning 200 the whole time, which is exactly why it went unnoticed — the site *looks* fine. **Fix:** `app.UseStaticFiles()` before `UseRouting()`. ⚠️ Note this serves `/Files/**` **anonymously** (it sits before `UseAuthentication`) — passport scans under `BookingAttachments` are reachable by anyone holding the GUID URL. That matches the existing design (the URLs are rendered as plain links), but if that's ever unacceptable, serve attachments from an authorized controller action instead of static files.
- **🔴 `EnableRetryOnFailure` would break six write paths.** The obvious fix for "app starts before SQL Server is ready" is a retrying execution strategy — but `SqlServerRetryingExecutionStrategy` throws `InvalidOperationException: The configured execution strategy ... does not support user-initiated transactions` on every explicit transaction. This project has six: `BlockStatusService:75`, `RoleService:101/147`, `UserAccountService:95/154/298`. Using it would have broken block status changes and all role/user permission saves. **Solved instead** with a compose healthcheck plus a bounded retry loop around only the startup migrate/seed block, leaving the runtime strategy untouched. If retries on transient SQL errors are ever genuinely wanted, every `BeginTransactionAsync` call site must first be wrapped in `CreateExecutionStrategy().ExecuteAsync(...)`.
- **`ExcludeAssets=runtime` on `Microsoft.VisualStudio.Web.CodeGeneration.Design` breaks restore.** The intent was to keep MSBuild/Roslyn/scaffolding DLLs out of the published image. It fails restore with 48 × **NU1202**, because the package's transitive `Humanizer.Core.*` satellite packages then fail framework-compatibility resolution against net9.0. `PrivateAssets=all` alone is what works. (Removing the reference outright is still the stronger trim — nothing in this project is scaffolded.)
- **Serilog writing inside the project tree retriggers `dotnet watch` on every log line.** In the dev container Serilog's relative `Logs/log-.txt` resolved under the bind-mounted `/src`, so each request's EF SQL logging looked like a source edit — watch woke on every write. **`<Watch Remove="Logs\**" />` in the csproj did NOT fix it** (watch still reported the file). The fix is to keep logs out of the tree entirely: Serilog's path now reads `TRAVEL_LOG_PATH` (defaulting to the old `Logs/log-.txt`), and the dev compose points it at `/var/log/travel`.
- **Two compose targets sharing one image name silently clobber each other.** `docker compose build` (dev) overwrote the image `docker compose -f docker-compose.yml up` (prod) then ran — so "prod" was actually running `dotnet watch`. Give each target an explicit `image:` tag.
- **Dev and prod must share the uploads volume, because they share the database.** Pointing dev's `wwwroot/Files` at the bind-mounted repo folder seemed friendlier (files visible in Explorer) but left DB rows referencing files the other target couldn't see — a package's cover image 200s in prod and 404s in dev. One `uploads` volume for both.
- **The non-root prod image needs an explicit `chown`, and Serilog fails *silently* without it.** `COPY` leaves files root-owned; `USER $APP_UID` then can't create `/app/Logs`. Serilog's file sink swallows construction errors (falls back to a no-op sink), so the symptom is an empty `Logs` directory, not an exception. `mkdir -p` + `chown -R $APP_UID` for `Logs`, `wwwroot/Files`, and the Data Protection keyring before switching user.
- **Git Bash mangles container paths.** `docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd` becomes `C:/Program Files/Git/opt/...`. Prefix with `MSYS_NO_PATHCONV=1` and wrap in `sh -c`. Also pass **`-I`** (QUOTED_IDENTIFIER ON) — sqlcmd defaults it off, and any write to the Identity tables then fails with *"DELETE failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'"* (they carry filtered/unique indexes).

### Reusable guardrails (from the same architecture — apply as the domain is built)
- **SQL Server cascade cycles (error 1785):** don't set every FK to `Cascade`. Use `Cascade` only for ownership FKs, `NoAction` for reference FKs to break multiple cascade paths to the same table.
- **EF enum default value:** don't `HasDefaultValue(1)` (int) on an enum property — type mismatch at design time. Initialize the default in the C# model instead and omit the DB default.
- **Upload directory:** create the target folder before opening a `FileStream` (`Directory.CreateDirectory` is a safe no-op if it exists). `FileStorageService` already does this — keep it for any new upload folder.
- **Submit button "Saving…" stuck:** gate the disable-button/spinner on validity — `if (!$(form).valid()) return;` *before* disabling — so a client-side validation failure doesn't permanently disable the button.
- **CRLF vs LF edits:** if a targeted string replace silently fails to match, verify with a fresh read and fall back to overwriting the whole file.
