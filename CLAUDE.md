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
- **`AppUser`** : `IdentityUser` — `FirstName`, `LastName`, `FullName` (computed), `Gender` (enum), `IsActive` (default true), `ImageUrl?`, `Theme` (`AppTheme`, default Light — see Dark mode under UI/UX Guidelines). Configured in `AppUserConfig` (FirstName/LastName required max 100; ImageUrl max 500; unique index on UserName + Email).
- **`ICodedEntity`** (`Travel.Domain/Models/`) — marker interface, `string Code { get; set; }`, for entities carrying an auto-generated sequential code. Implemented by **`Booking`** and **`Package`** — see Code Generation.

---

## Enums

| Enum | Values | Location |
|---|---|---|
| `Gender` | `Male=1`, `Female=2` | `Travel.Domain/Enums/` |
| `AppTheme` | `Light=1`, `Dark=2` (per account, `AppUser.Theme`) | `Travel.Domain/Enums/` |
| `BlockStatus` | `NotBooked=1`, `InProgress=2`, `Confirmed=3`, `NeedsAmendment=4` (**derived, never stored** — reachable from `InProgress` *and* `Confirmed`) | `Travel.Domain/Enums/` |
| `BookingStatus` | `InProgress=1`, `Confirmed=2`, `Completed=3`, `Cancelled=4` | `Travel.Domain/Enums/` |
| `SafeEntryType` | `Deposit=1`, `Withdrawal=2`, `Expense=3` (direction is **derived** via `SignedAmount`) | `Travel.Domain/Enums/` |
| `ExpenseTarget` | `ArrivalGroups=1`, `Bookings=2` (fixed once an expense is saved) | `Travel.Domain/Enums/` |
| `ExpenseBasis` | `PerRoom=1`, `PerPax=2` | `Travel.Domain/Enums/` |

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

Operators block rooms **per property, per arrival date, per grade**, not per booking. `/BlockService` inverts the data: it groups **`BookingStay` rows by `DestinationId` + `CheckInDate` + `Booking.Category`** (the 4★/5★ grade the booking was sold at). A `Cairo + Cruise + Hurghada` booking therefore appears in three different groups, and a 4★ and a 5★ booking arriving in Cairo on the same day are **two independent blocks** — see Split by category below.

**Every group is a file, including a one-booking one** — the supplier needs the rooming list either way. The two-booking line is a **presentation split**, not an existence threshold:

| Page | Shows | Reached by |
|---|---|---|
| `/BlockService` | groups with **2+ bookings** | sidebar |
| `/BlockService/SingleArrivals` | groups with **exactly 1** | a dismissible alert on the Block Services list (no sidebar entry, by design) |

Both link to the **same** `Details` / `Export` — a single arrival gets an identical document, only the wording and filename differ (`Arrival_…` vs `Block_…`). `GetArrivalsAsync` returns both halves (`ArrivalGroups`) from one materialisation, so each page's list *and* the other page's alert count cost a single query.

**Nothing is persisted.** There is no `BlockService` entity, no code, no status, no workflow — every request re-derives from live booking data. A group's identity is its `(destinationId, checkIn, category)` triple — **`BlockKey`** in `BlockDtos.cs`, the one dictionary key every loader and service uses — passed as **query-string** parameters (`DateOnly` in a route template is awkward); links emit `asp-route-checkIn="@x.ToString("yyyy-MM-dd")"` + `asp-route-category="@x.Category"`, which the default binder round-trips and `<input type="date">` posts natively.

| Decision | Why |
|---|---|
| Key is destination + check-in + category, **not hotel** | `BookingStay.HotelName` is optional free text; the **category** is the reliable proxy for "which hotel" — see Split by category |
| `MinimumBookings = 2` splits the **two lists**, it doesn't gate existence | Originally it hid singletons entirely — wrong: every arrival needs a file sent, so they had no page and no export at all |
| Rooming comes from **`Booking.Rooms`** (trip-level) | There is no per-stay rooming; the same tally repeats in every group a booking joins |
| Grouping is **in-memory** | `BaseSpecifications` has no projection/GroupBy, and `Booking.Pax` / `LeadGuestName` are computed C# properties EF can't translate |
| `GetBlockAsync` returns **`null`** only on an **empty** group | A link to a destination + date no longer holding any booking 404s; one booking is a valid file |

`DistinctByBooking` keeps one stay per booking — guards a booking visiting the same destination twice on the same check-in, which would otherwise double-count its pax and rooms.

### Split by category (4★ / 5★)

A 4★ and a 5★ guest arriving the same day go to different hotels, so `Booking.Category` is the
third part of a group's identity **at every destination, cruise included**. Each half has its own
list row, `Details`, `.xlsx`, status, email links, domestic-flight documents, supplier record and
expenses. `BlockServiceStatus.Category` and `BlockExpenseGroup.Category` carry it on the two
persisted sides; their unique indexes are `IX_BlockServiceStatuses_Destination_CheckIn_Category` and
`IX_BlockExpenseGroups_Expense_Destination_CheckIn_Category`.

| Decision | Why |
|---|---|
| Category is part of the key, not a section inside one file | The user's call: the two grades are booked with different suppliers, so they need separate statuses and records, not a subtotal |
| Badge **inside the Destination cell**, plus a Category filter — no new column | No DataTables positional index moves on either list. The 4★ and 5★ rows of one date sort side by side (`ThenBy(Category)`) |
| `CategoryBadges` (`Helpers/`) owns the on-screen spelling (`4★`, `4 Stars`, badge classes, `GroupLabel`) | Same role as `StatusBadges`; `ExcelFormat.CategoryText` is the export twin |
| A two-part link (no `category`) **404s** | A bookmark from before the split can't know which half it meant; guessing would show the wrong supplier file |
| The expense picker token is `"{dest}\|{yyyy-MM-dd}\|{category}"` | `ExpenseGroupKey.Parse` requires all three parts. Tokens are form state only, never stored |
| Changing a booking's category on Edit **moves it between blocks** | `Edit` already calls `RecalculateAsync`; both affected blocks then read `Needs amendment` with no extra code |
| The missing-domestic-doc check keys on the **booking's** category | A 4★ booking is covered only by a document on a 4★ group, never by the 5★ group's ticket file |

**Migration `SplitBlocksByCategory` is hand-edited** — see the 2026-10-03 gotcha. It split every
mixed group's persisted records: statuses, links and documents copied to both halves; supplier
records and expense lines moved to the half their booking belongs to; each expense half's counts,
room tally and fingerprint rebuilt from its own lines. No amount, safe entry or expense total moved.

### Private bookings

**`Booking.IsPrivate`** (a switch on Create/Edit — `_BookingPrivateSwitch.cshtml`, shared by both,
gated by the ordinary `Bookings.Create`/`Edit` permissions) gives a booking **its own arrival file at
every stay on its itinerary**. It never joins or counts toward the shared block for that destination,
date and category, and its file always lists under Single Arrivals (it holds one booking).

The identity grows a fourth part: **`BlockKey.PrivateBookingId`** — the booking's id when private, null
for the shared block — mirrored by a nullable `PrivateBookingId` on `BlockServiceStatus` and
`BlockExpenseGroup`, and carried as the `privateBookingId` query-string value on every block link and
post. The expense picker token gains a fourth part (`"12|2026-08-01|5|"` shared, `"…|5|37"` private).

| Decision | Why |
|---|---|
| Every stay, not chosen legs | The user's call. One flag on the booking, no per-stay UI |
| Toggling **re-derives**, nothing is copied | The block it leaves reads `Needs amendment` through the existing record mismatch, its private file starts `Not booked`, and an expense already paid shows `Needs recalculating` — exactly what happens when any booking moves. Un-toggling makes the private links 404 (no stay matches the key any more) |
| The supplier file **says Private** | Title `… — PRIVATE ARRIVAL — CHECK-IN …`, filename `Private_{Dest}_{date}_{cat}_{Code}.xlsx`, and the Details badge prints: the hotel must not fold it into the shared block. The booking code in the filename stops two private arrivals on one date colliding |
| `PrivateBookingId` has **no FK** | A second path into `Bookings` would trip error 1785. `BookingController.Delete` removes the booking's private status rows by hand — and their document files, under the same shared-file guard as `DeleteDocument`. On an expense group it is a plain snapshot that must outlive the booking |
| 🔴 Both unique indexes are **unfiltered** — `HasFilter(null)` | See the 2026-10-03 private-bookings gotcha |
| Badges are inline (Destination cell, Booking Index Code cell) | No DataTables positional index moves |

**Files:** `Booking.IsPrivate`, `BlockKey` in `BlockDtos.cs`, `CategoryBadges.PrivateCss` /
`GroupLabel(…, privateBookingCode)`, `_BookingPrivateSwitch.cshtml`, migration `AddPrivateBookings`
(hand-edited `Down` that merges private expense groups back rather than dropping money).

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

**`BlockServiceStatus` is no longer just a status row** — it is the arrival group's persisted record: status, both email links, documents (`Attachments`) and the record of what the supplier was told (`RecordedBookings`). The groups themselves are still derived from live `BookingStay` rows on every request.

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
| | `NeedsAmendment` | **derived at read time** from `InProgress` *or* `Confirmed` — never stored, never settable |
| **`BookingStatus`** | `InProgress` / `Confirmed` | **derived** — never set by hand |
| | `Completed` / `Cancelled` | the operator, via `Booking/SetStatus` |

**The rule, in one line:** a booking is `Confirmed` exactly when **every** arrival group on its itinerary is stored as `Confirmed` *and* holds a record that **still matches** it, and drops back to `InProgress` the moment one doesn't. An `InProgress` group keeps a record too, but it is a record of what we *asked* for — it never confirms a booking. A Cairo + Cruise + Hurghada booking needs all three. `RecalculateAsync` (`BlockStatusService`) is the single owner of this and **never touches a booking whose status is `Completed` or `Cancelled`** — those are the human's.

**`BlockServiceStatus` is the first persisted row Block Services has ever had**, and now carries the group's whole record (status, both email links, `Attachments`, `RecordedBookings`). The groups themselves are still derived from live `BookingStay` rows on every request; this side table just hangs off the same identity a group already had, `(DestinationId, CheckInDate, Category)`. Rows are **created lazily** by a status change *or* a document upload — a group with no row reads as `NotBooked` (`BlockStatusInfo.NotBooked`), and an orphaned row (every booking moved off that date) is harmless: it simply re-applies if a booking returns.

### `NeedsAmendment` — what the supplier was told

> A block holds its status exactly while **what the supplier was told** still describes the group as it stands now. The moment a booking joins, leaves, or changes its **rooms / PAX / nights**, it reads **Needs amendment** — from `InProgress` (the request we sent is out of date) as much as from `Confirmed` (the confirmation we got back is). Only from `Confirmed` do *the bookings no longer covered* also lose their Confirmed badge.

Before this existed, a booking created into an already-confirmed set of blocks landed `Confirmed` immediately — claiming a confirmation no supplier had given, with nothing anywhere to say so. `InProgress` was added to the same rule later (2026-08-20) for the mirror-image gap: a booking joining a group whose request had already gone out left the operator with no signal to re-send an amended one.

**`BlockRecordedBooking`** is what makes it derivable: one row per booking per group, written whenever the operator saves `InProgress` or `Confirmed` and deleted when the block moves to `NotBooked`. It stores a **fingerprint of the supplier-relevant facts** (`Adults`/`Children`/`Infants`/`Nights` + a canonical `RoomsFingerprint` like `"1x2|2x1"`), not a timestamp — so no `Booking.UpdatedAt` column is needed, and fixing a typo in the notes or filling in a flight number two weeks later doesn't falsely re-open a settled block. **`BlockSnapshot`** (`Travel.Domain/Services/BlockServices/`) is the one pure owner of `RoomsFingerprint` / `Matches` / `Take` / `Resolve`, shared by the read and write sides so they can't drift.

| Decision | Why |
|---|---|
| Supplier-relevant = **rooms, PAX, nights** only | What the supplier actually blocks. Guest names, hotel, category and flight details deliberately don't flag — flights are filled in late and would make the status noise |
| `NeedsAmendment` is **derived, not stored** | Nothing can park a block in a state it can't leave; re-saving the status it already has rewrites the record and is exactly the "re-send the request" / "re-confirm" action. `SetStatusAsync` rejects it explicitly — `Enum.IsDefined` alone would let it through |
| The status button carries **`StoredStatus`**, not the displayed one | The modal's select has no option for a derived `4`, and a flagged block must reopen on whichever status went out of date. `block-status.js` used to map `4 → 3`; with two causes that would silently offer to mark an in-progress block *confirmed*. 🔴 `BlockStatusInfo.Status` is re-stamped by `WithStatus`; `StoredStatus` is the init-only property that survives it |
| 🔴 `Resolve` returns **no covered ids** unless the stored status is `Confirmed` | "Covered" is a *confirmation* fact — `Booking/Details` reads it for "N of M blocks confirmed" and the per-stay note. An In-progress block records a request, which confirms nothing. `RecalculateAsync`'s own `IsCovered` guards on the stored status independently, so booking status is safe either way; **the display path is not**, and would have counted in-progress blocks as confirmed |
| An **empty record set on `InProgress`** means "never recorded", not "changed" | Blocks saved before records were kept for that status would otherwise all flag on deploy day. They keep reading In progress and start flagging on the next save. Deliberately chosen over backfilling — a backfill needs hand-written SQL to rebuild the room fingerprint |
| Only the **changed** bookings drop | The bookings the supplier did confirm are still confirmed; dropping all of them would be a lie in the other direction. This is why `RecalculateAsync` reads the *record*, not the block's status |
| Booking FK on the record is **`NoAction`** | Breaks the second cascade path into that table (SQL Server error 1785). `BookingController.Delete` clears the rows by hand |

**Same badge, different wording.** One red `Needs amendment` badge everywhere (no fifth enum value, no new filter option, no new Excel label); the inline muted line beside it reads the request or the confirmation from `StoredStatus` — on `BlockService/Details` and on the `Booking/Details` schedule row. The modal carries one note per status for the same reason.

**Triggers:** `SetStatusAsync` (writes/clears the record) and `BookingController.Create`/`Edit`/`Regenerate` (recalculate). The block's own badge needs no trigger at all — it is resolved on every read.

**Two permanent, independent email links** — `RequestEmailLink` (the email that requested the block) and `ConfirmationEmailLink` (the supplier's reply). Named for the business fact, not the status. They are **only ever written, never cleared**: a blank field in the modal leaves the stored value alone, so confirming can't lose the request thread and moving back to `NotBooked` can't lose either. Validated server-side to absolute `http`/`https` (they render as anchors — a `javascript:` value must not survive).

**The links are screen-only.** They are internal references into the operator's own mailbox, and both the `.xlsx` and the printed sheet go **to the supplier** — so `BlockExcelExporter` doesn't write them and the Details block carries `d-print-none`. The block **status** does appear in both. Anything added to the supplier-facing file needs the same question asked of it: *is this ours, or theirs?*

| Decision | Why |
|---|---|
| Booking `Confirmed` requires **all** blocks, not the first | The booking isn't really confirmed while two hotels are still unbooked; a partial confirmation that reads "Confirmed" is a lie the operator would act on |
| Coverage is read from the **record**, not from the resolved `BlockStatus` | A block reading `NeedsAmendment` because a *different* booking changed must not un-confirm this one. The *stored* status must still be `Confirmed` — see the red-flag row above |
| Un-confirming a block **reverts** its bookings | Re-evaluation runs in both directions, so the badge always reflects current reality rather than a high-water mark |
| `InProgress`/`Confirmed` are **not** hand-settable | `BookingController.SetStatus` rejects them outright. Offering them would let a booking claim a confirmation no supplier gave |
| "Reactivate" posts **no** status | It hands the booking back to the derivation instead of guessing a value. A reactivated booking rejoins its groups, so any of them that were confirmed without it now read `NeedsAmendment` |
| Cancelled bookings are **excluded from Block Services entirely** | A supplier file that counts a cancelled booking over-states rooms. `BlockStaysSpec` filters them in **both** ctors — see the red-flag note below |
| `Status` is `Ignore()`d in `BookingProfile` (VM → entity) | Same reasoning as `Code`/`EmbarkDay`: an ordinary Edit save (or a tampered POST) must not overwrite a derived value |

**Recalculation triggers:** `SetStatusAsync` (both directions), and `BookingController.Create`/`Edit`/`Regenerate` — all three can change which blocks a booking belongs to, or the facts the record pins.

**Permissions:** `BlockServices.ChangeStatus`, `Bookings.ChangeStatus`, `BlockServices.ManageDocuments` (all added to existing modules, so no `PermissionCatalog` renumbering). 35 at the time; 37 once Reports landed, 39 with Booking Pricing — still well under the ~60 cookie ceiling.

**Presentation:** **`Travel.Presentation/Helpers/StatusBadges.cs`** is the single source of truth for label + badge class + icon (`ForBlock` / `ForBooking`), mirroring `PermissionIcons`. There is no `GetDisplayName()` helper in this project, which is why enum display strings otherwise get hand-written per view. `ExcelFormat.BlockStatusText`/`BookingStatusText` are its counterpart for exports. `Booking/Details` reads `BookingBlockLine.IsCovered` — not the block's status — for both the "N of M blocks confirmed" line and the per-row "this booking is / isn't covered" note.

**Files:** enums in `Travel.Domain/Enums/BlockStatus.cs` + `BookingStatus.cs`; entities `BlockServiceStatus.cs`, `BlockAttachment.cs`, `BlockRecordedBooking.cs`; configs of the same names; `IBlockStatusService` + `BlockStatusChange` + **`BlockSnapshot.cs`** in `Travel.Domain/Services/BlockServices/`; `BlockStatusInfo` (incl. `StoredStatus`)/`BookingBlockLine`/`BlockDocumentLine` in `BlockDtos.cs`; `BlockStatusSpec` + `BlockStaysSpec` + `BookingsForStatusSpec` in `Specifications/`; impls `Travel.Services/BlockServices/BlockStatusService.cs` + **`BlockGroupLoader.cs`** (the shared narrow-then-match-in-memory key loader); UI in `Views/BlockService/_BlockStatusCell.cshtml` + `_BlockStatusModal.cshtml`, `Views/Booking/_BookingStatusActions.cshtml`, and `wwwroot/back/assets/js/block-status.js`.

---

## Printable Documents & Excel Export

Two pages are documents an operator prints or sends out: **`BlockService/Details`** (a supplier's arrival file — a block or a single arrival, same document) and **`Booking/Details`** (a single booking). Both carry `Print` + `Excel` buttons, gated on `BlockServices.Export` / `Bookings.Export` respectively.

**Print** — put **`.print-sheet`** on the card that *is* the document. The `@media print` block in `app-extras.css` then hides `#page-topbar` / `.vertical-menu` / `.footer`, zeroes the content padding, strips the card border, keeps `thead` with its rows (`display: table-header-group`) and avoids splitting rows across pages. Buttons carry **`d-print-none`**. Adding print support to a new page is just those two classes — don't write a second `@media print` block.

**Excel** — one exporter per document in `Travel.Presentation/Services/`, sharing **`ExcelFormat`** (content type, room/guest/category/flight text, `FormatRooms`, `SheetName`, `SanitizeFileName`, `WriteHeaderRow`, `WriteHeaderCells`, `WriteGroupHeader`, `WriteField`). Put any new formatting **mechanic** there rather than in an exporter, so a booking sheet and a block file can't disagree on how a `RoomType` is spelled. A **palette** used by exactly one document stays on that exporter — see `FlightReportExcelExporter`.

| Exporter | Output | Filename |
|---|---|---|
| `BlockExcelExporter` | Arrival file — one row per booking + indented guest sub-rows, frozen header, TOTAL row. **Same layout at any booking count**; only the label changes | `Block_{Destination}_{yyyy-MM-dd}_{4Stars\|5Stars}.xlsx`, or `Arrival_…` when the group holds one booking |
| `BookingExcelExporter` | Booking — facts block, `GUESTS` table, `SCHEDULE` table with computed check-outs + total nights, and a `PRICING` block when `includePricing` (see Booking Pricing) | `Booking_{Code}.xlsx` |
| `FlightReportExcelExporter` | Flights report — one row per booking, guests stacked in one cell, under a two-row merged `ARRIVAL`/`DEPARTURE` header. See Reports | `Flights_{from}_{to}.xlsx` |
| `RevenueReportExcelExporter` | Revenue report — sale, cost and margin per booking; margin is a real percentage cell, unpriced rows leave their money cells empty rather than 0. See Reports | `Revenue_{from}_{to}.xlsx` |
| `TransactionExcelExporter` | The global ledger — one row per entry, signed amounts so the column sums to the NET footer. See Transactions | `Transactions_{from}_{to}.xlsx` |
| `SafeLedgerExcelExporter` | One safe's movements — signed amounts summing to a bold NET (filtered) and BALANCE (all time) footer; the currency in the header. See Money Safes & Block Expenses | `Safe_{Name}_{from}_{to}.xlsx` |
| `ExpenseExcelExporter` | The expenses list — one row per expense with its arrival groups indented beneath, and a TOTAL **per currency** (never one summed across them) | `Expenses_{from}_{to}.xlsx` |

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

## Booking Pricing

Three nullable decimals on `Booking` — `AdultPrice` / `ChildPrice` / `InfantPrice` — a **per-person
rate for each pax type**, entered beside the counters in the Guests card. This is the **pax
component** of what a booking is worth; see The Money Model for the other two and the grand total.

> **One currency app-wide (USD), and it is a label, not a column.** `Travel.Presentation/Helpers/Money.cs`
> is its single source of truth — `Code`, `Format(decimal?)`, `Amount(decimal)`, `ExcelNumberFormat` —
> mirroring how `StatusBadges` owns status presentation. Changing currency is one const. Amounts format
> under `InvariantCulture`, so a server locale that swaps `.` and `,` can't put "1.300,00" and "1,300.00"
> on the same booking.

| Decision | Why |
|---|---|
| Per-person rate × count, not a lump sum per type | What the operator actually quotes. Counts and money then stay in sync — changing the pax count re-totals instead of leaving a stale figure |
| **Nullable columns, required on save** | The migration adds no default and backfills nothing, so the bookings taken before pricing existed survive as unpriced. Every new or edited booking must price each type whose count > 0 — the rule is cross-field, so it's `ValidatePricing` in the controller, not a `[Required]` attribute |
| `PaxTotal` is **`decimal?`**, null while unpriced | 0 already means *free*. A booking nobody has priced and a complimentary one must not render the same, on screen or in the sheet — hence `IsPriced` as a separate flag |
| Own permission pair, `Bookings.ViewPricing` / `EditPricing` | Ops staff work rooming lists and airport pickups without seeing what the client paid. Appended **inside** the existing Bookings module, so no `PermissionCatalog` renumbering. 37 → **39** |
| Prices are **`Ignore()`d in `BookingProfile`** (VM → entity) | Same reasoning as `Code`/`Status`, and it is the load-bearing half of the gate: `ApplyPricing` runs only when the user holds `EditPricing`, so an ops coordinator's ordinary save — or a tampered POST — leaves the stored figures alone instead of nulling them |
| `ValidatePricing` runs on **Create and Edit** | Unlike the past-arrival-date rule (`isCreate`-only — see the 2026-08-02 gotcha), pricing is not a create-only concern |
| A price behind a **zero count is dropped** | `ApplyPricing` nulls it, so raising the count later can't silently resurrect a figure nobody re-entered |
| Both flags set in **`PopulateFormAsync`** | Every render path — Create/Edit GET and both POSTs' re-render — goes through it, so they can't be missed on one branch |

**Where money appears:** the booking form, `Booking/Details` (a Pricing table, no `d-print-none`),
`Booking/Index` (a **Total** column), and `Booking_{Code}.xlsx` (a `PRICING` block). Each is gated on
`ViewPricing`; the Excel block is a `bool includePricing` parameter, so the same Export button hands a
priced sheet to a sales agent and an unpriced one to everyone else.

**Deliberately absent from Block Services and the Flights report** — the *"is this ours, or theirs?"*
rule. The block `.xlsx` and printed sheet go to the **hotel supplier**; a sale price is ours. A booking
sheet goes to the operator or the client, which is why the Details pricing card **does** print.

> ⚠️ **The Index Total column is conditional, so the DataTables indices are too.** Columns run
> `0 Code · 1 Guest · 2 Package · 3 Arrival · 4 PAX · 5 Category · 6 Status · 7 Total · 8 Actions` — but
> only for a user holding `ViewPricing`; without it Actions is back at 7. The config is computed in Razor
> (`targets: @(Model.CanViewPricing ? 8 : 7)`). The cell carries `data-order="@(b.TotalPrice ?? 0m)"`
> because it renders `USD 1,300.00` / `—`, which sorts wrong as text.

**Files:** `Travel.Presentation/Helpers/Money.cs`, `Views/Booking/_BookingPaxCounts.cshtml` (the counts
+ prices, **shared by Create and Edit** so the two can't drift), the `totals` module in
`booking-form.js`, `WritePricing` in `BookingExcelExporter`, `ValidatePricing`/`ApplyPricing` in
`BookingController`, migration `AddBookingPricing`.

---

## The Money Model

What a booking is worth is **three components plus a ledger**, all computed, none stored:

```csharp
PaxTotal         = Adults*AdultPrice + Children*ChildPrice + Infants*InfantPrice   // null while unpriced
ExtraNightsTotal = Σ stays: ExtraNights × Σ rates(Quantity × Rate)                 // 0 if guest-arranged
ToursTotal       = Σ tours: Pax × UnitPrice
GrandTotal       = PaxTotal + ExtraNightsTotal + ToursTotal                        // null while unpriced
PaidTotal        = Σ transactions (Payment positive, Refund negative)
Balance          = GrandTotal − PaidTotal                                          // null while unpriced
```

> **`IsPriced` covers extra nights too, not just pax.** `Booking.HasUnpricedExtraNights` is part of it,
> so a stay with chargeable extra nights and no room rates makes the **whole** total read `—` rather
> than quietly contributing 0. Without that, the per-room-type migration would have left every
> historical booking showing a believable, wrong, smaller number.

> ⚠️ **`GrandTotal` is what every "total" surface must read** — the `Booking/Index` Total column, the
> `Booking/Details` pricing table, `BookingExcelExporter.WritePricing`, and the 10% first-payment rule.
> `PaxTotal` was called `TotalPrice` until Booking Finance landed; the rename was deliberate precisely
> because a call site left on the pax-only figure still compiles and just quietly under-charges.

`Balance` is **`decimal?`, not 0**, for the same reason `PaxTotal` is: "USD 0.00 remaining" on an
unpriced booking reads as *fully paid*, the exact opposite of the truth.

All of these are computed C# over loaded navigations, like `Pax` and `LeadGuestName` — **EF cannot
translate them, and a missing `AddInclude` makes them silently too small rather than throwing.**
`BookingWithDetailsSpec` therefore includes `Tours.Tour`, `Transactions` **and `Stays.Rates`**.

### Extra nights, priced per room type per stay

`BookingStay` carries **`BaselineNights`** (what the generator gave the row at the package's sold
length). `ExtraNights` is derived — `Nights − BaselineNights` — so the schedule stays the single source
of truth and the count is **never typed**.

> **The one subtlety that makes it correct:** the generator's own Saturday/cruise auto-extension lands
> on the **base-return leg**, where `Nights = package.BaseReturnNights` but the baseline is the
> un-extended `computedReturn`. `ItineraryGeneratorService` sets `BaselineNights = Nights` for every
> other stay and `returnBaseline` for that one. Without the exception, an extension the operator never
> asked for and *is* chargeable would read as included. A stay the operator adds by hand posts
> `BaselineNights = 0`, so all of its nights are extra.

**The rate is per room type, per stay** — `BookingStayRate` (`StayId`, `RoomType`, `Quantity`, `Rate`),
the `BlockExpenseRate` shape with a quantity. Hotels quote per room type, and Cairo and Hurghada are
different hotels, so a single blended figure per row was arithmetic the operator had to do in their
head and nobody could audit back to a supplier quote.

```
row total = ExtraNights × Σ over room types (Rate × Quantity)
```

| Decision | Why |
|---|---|
| A child table, not six columns on `BookingStay` | A fourth `RoomType` needs no migration, and the type stays the key it already is on `BookingRoom`, `BlockExpenseRate` and `BlockSnapshot.RoomsFingerprint`. The cost is `Stays.Rates` includes and an audit diff — both below |
| `Quantity` is its own figure, seeded from `Booking.Rooms` | "3 rooms on the trip, only 2 stay the extra night" is routine. Seeded **once**, as a row first becomes extra, and never silently overwritten; a divergence from the booking tally shows as a muted inline line, which never blocks the save (the `#rooms-summary` call) |
| Seeded **server-side** in `NormalizeStayRates`, not only in JS | Otherwise the form cannot be priced at all with scripting unavailable. `booking-form.js` applies the same rule to rows that become extra after load |
| `Rates` always renders **all three** types, in enum order | Razor indexes `Stays[i].Rates[j]` positionally, and MVC's collection binder stops at the first missing index — so disabling or omitting one tile silently yields an **empty** rate list and the operator's figures vanish with no error. `NormalizeStayRates` (in `PopulateFormAsync`, the one method every render path goes through) guarantees the set |
| No `BookingId` on `BookingStayRate` | A second cascade path into `Bookings` (SQL Server 1785). The audit interceptor reaches the booking through `rate.Stay` instead |
| Index named `IX_BookingStayRates_Stay_RoomType` | No `_Code` in it — `UnitOfWork.IsCodeUniqueViolation` string-matches that, and a duplicate room type must surface as itself |

`ValidatePricing` keys a per-room-type error on `Stays[i].Rates[j].Rate` (prefixed with the type —
`"DBL: enter a rate."` — because the messages stack in a narrow cell, away from the boxes) and a
row-level one on `Stays[i].Rates`. **Both have `asp-validation-for` spans, rendered unconditionally in
the row.** `BuildStaysFromInputsAsync` clears the rates *and* the guest-arranged flag on a row that
stopped being extra, so a stale value can't resurface unseen when the nights go back up.
`Regenerate` rewrites the baselines and drops the rates.

#### The rates are entered in a modal; the row owns the data

`#stay-rates-modal` (`_BookingStayRatesModal.cshtml`) is **one shared dialog**, populated from the
clicked stay row — the `_BlockStatusModal` pattern. The values that actually post are **hidden inputs
in the row's `.stay-cost` cell**; the modal edits nameless copies and writes straight back.

| Decision | Why |
|---|---|
| One shared dialog, never one per row | `addRow()` stamps `__INDEX__` with `rows().length`, so after deleting a middle row a new row's ids collide with a survivor's and `data-bs-target` silently cross-wires |
| Modal inputs carry **no `name`**, and it renders **after `</form>`** | They must never post. Outside the form also stops Enter in a number box from submitting the whole booking |
| 🔴 Rendered from `Create`/`Edit`, **never** from `_BookingStayRows` | On Create that partial sits inside `<div id="previewWrap" class="d-none">`, and a Bootstrap modal inside a `display:none` ancestor never appears — a dead button with no console error |
| 🔴 The hidden inputs sit inside the `<td>` | An `<input>` directly under a `<tr>` is foster-parented out of the table by the HTML parser and silently stops posting |
| Every validation span stays **in the row** | A message inside a closed dialog is the invisible-rule bug, three times over. Errors are now visible with nothing opened, and `totals.extraNights()` only reddens the trigger button |
| **Live write-through, one `Done` button** — no Apply/Cancel | Esc and a backdrop click would otherwise silently destroy typed rates. Nothing is persisted until the booking itself is saved, so a second level of undo buys nothing |
| 🔴 Written with a plain `.value` assignment, **never a dispatched event** | An `input` event would reach the container delegate's catch-all branch, `markEdited()`, and arm the destructive "Rebuild the schedule?" confirm from a pricing keystroke that touched no dates |
| `Quantity` coerced `|| 0` on write-back | It is a non-nullable `int`; a blank hidden posts `""` and the binder fails with "The value '' is invalid." |
| `new bootstrap.Modal(el)` **once** at init | This build (5.0.0-beta2) has no `getOrCreateInstance`, and `new bootstrap.Modal(...).show()` per click leaks the previous instance's handlers |

Because the hidden inputs are *inside* `.stay-row`, `recalc()`'s ordinary reindex covers them and the
earlier sibling-row pairing is gone entirely. `renderRows()` and `.stay-remove` call
`stayRatesModal.close()` first, so the dialog is never left bound to a detached row.

The trigger button carries the whole state — `—` / **`Set rates`** / the money / **`Not charged`**,
reddened when the cell holds an error — plus a compact always-visible `2 of 3 rooms` note when the
quantities diverge from the booking's tally (the full sentence is inside the modal).

### Extra nights the guest arranged themselves

A tourist sometimes turns up having booked an extra night on their own. The operator doesn't bill it,
and needs to be able to say so. **`BookingStay.ExtraNightsArrangedByGuest`** is one checkbox per stay
row, all-or-nothing over that row's extra nights.

> **The night stays part of the package.** The schedule, `Nights`, the departure-date maths, Block
> Services, the supplier rooming file and the per-room expense cost all read it **exactly as before** —
> this flag says only *we do not charge for it*, and records why. Nothing in
> `BlockStaysSpec` / `BlockServiceQueryService` / `ExpenseCalculator` changed.

| Decision | Why |
|---|---|
| All-or-nothing per row, not a count of nights | The operator's own call. A row that genuinely mixes can be split into two stays |
| Ticking it **disables** every rate box | You must not be able to edit a rate for nights you have just said aren't charged. The first cut left them live, which was contradictory — the operator's own observation |
| …but the figures are **kept** | They live in the row's hidden inputs, which are never disabled; only the modal's nameless copies are. So unticking a mis-click costs nothing, and the inline note says so. `ExtraNightsTotal` returns 0 regardless |
| It counts as **priced** | There is nothing left to enter, so it must not hold `GrandTotal` at `—` |
| Visible on Details, the booking list and the `.xlsx` | Details gets a schedule badge and a `0.00` pricing line (so the listed lines still reconcile with the TOTAL); `Booking/Index` gets a `guestArranged` filter and an **inline** `bx-user-check` in the Code cell — not a new column, since the Actions DataTables target is already computed from two optional columns |
| Deliberately **not** on any report | The client asked for tracking, not reporting. It is also a sale-side fact, and the Revenue report's costs are unaffected |

**Files:** `BookingStayRate.cs` + `BookingStayRateConfig.cs`, `BookingStayRateInput` in
`BookingViewModel.cs`, `NormalizeStayRates`/`ValidatePricing`/`BuildStaysFromInputsAsync` in
`BookingController.cs`, the `.stay-cost` cell in `_BookingStayRows.cshtml` +
`_BookingStayRatesModal.cshtml` (rendered after `</form>` by Create/Edit), the rate helpers and the
`stayRatesModal` module in `booking-form.js`, `DiffStayRates` in `BookingAuditInterceptor.cs`,
migration `AddStayRoomRates`.

### Extra Tours

**`Tour`** is a plain catalogue (`Name` unique, `Description?`, `PricePerPerson`, `IsActive`) — the
`Nationality` CRUD shape with a price and an active flag. **`BookingTour`** is the sale:
`TourId`, `Pax`, **`UnitPrice` (a snapshot)**, `SortOrder`, `Notes?`.

| Decision | Why |
|---|---|
| `UnitPrice` is snapshotted, not read through the FK | Re-pricing the catalogue must never restate a booking already sold. Same reasoning as `BookingStay.IsCruise` snapshotting the destination flag |
| `Pax` is its own field, not `Booking.Pax` | Not everyone takes every tour — the infant usually doesn't |
| Tour FK `Restrict`, booking FK `Cascade` | Reference vs ownership. `TourController.Delete` refuses an in-use tour with a readable message; `IsActive = false` is the soft retire, and the booking form only offers active tours |
| Rebuilt wholesale on Edit, like `Rooms` | Pure form state, no files to preserve |
| The card is gated on **`Bookings.EditPricing`**, not `Tours.View` | A tour row is inseparable from its price; there's no useful "tours without money" version |

#### Who is on each tour

**`BookingTourGuest`** (`BookingTourId`, `GuestId`) records which guests take a tour. The form shows
one checkbox chip per guest under the tour select; **Pax is not typed — it is the ticked count**, set
server-side in `BuildTours`. One rate for everyone (`UnitPrice × ticked`); *Add tour* starts with
everyone ticked.

| Decision | Why |
|---|---|
| `Pax` stays a **stored column** | Tours sold before names existed carry a number with no names. They keep their figure (and total) until next edited — Details/`.xlsx` say *Names not recorded*, and Edit shows *Saved as N pax* and requires ticking before save. No backfill, no money moved on deploy. It also means `Total`/`GrandTotal`/`RevenueReportSpec` needed no new include |
| Guest FK is **`ClientCascade`** (NO ACTION in the DB) | A second database cascade path from `Bookings` is SQL Server 1785. EF still deletes the tracked link rows before the guests Edit deletes in the same save — so `BookingWithDetailsSpec` **must** include `Tours.Guests.Guest` |
| Checkboxes post **guest positions** (`Tours[i].GuestIndexes`) | Guests are rebuilt with new ids on every save, so a posted id would be dead. Links are built by navigation onto the fresh `BookingGuest` entities |
| A guest's identity across a rebuild is **(Type, ordinal within type)** | The key `syncGuestRows()` carries names by. `renderTourGuests()` re-ticks by it (`data-guest-key`), and `BuildTours` re-points a **preserved** tour (user without `EditPricing`) by it — captured as `KeptTour.GuestKeys` *before* the guests are deleted. Adding an adult therefore can't move a tick from the child onto someone else |
| A preserved tour whose guests all left keeps its stored Pax with no names | It then reads *Names not recorded* for a pricing user to resolve, rather than silently going to 0 |
| Audit: `DiffTourGuests` logs only when names **and** identities differ | A rename is the Guest row's change; a position shift from adding an adult is no change. Unticking one guest writes Pax + Guests in one event |

The checkbox markup in `_BookingTourRows.cshtml` is mirrored by `renderTourGuests()` in
`booking-form.js` — change both together. Both render the error span for `Tours[i].GuestIndexes`
("Tick who is taking this tour."), and the submit handler checks it client-side first, since a
round-trip would also empty every dropzone.

**Files:** `Tour.cs` / `BookingTour.cs` / `BookingTourGuest.cs` + configs, `TourController` +
`Views/Tour/` (+ `_TourForm`), `Views/Booking/_BookingExtraTours.cshtml` + `_BookingTourRows.cshtml`,
`TourOptionViewModel` (a `SelectListItem` has nowhere to put the catalogue rate, which the form needs
for the `data-price` pre-fill), `.tour-guest-option` in `app-extras.css`, migration `AddTourGuests`.

---

## Transactions (the payment ledger)

**One table, one concept.** Installments are not separate from transactions — an installment *is* a
`BookingTransaction`, and the log is the filterable view over them.

`Type` (`Payment`/`Refund`) · `Method` (`Cash`/`BankTransfer`/`CreditCard`) · `Amount` (**always
positive**) · `PaidOn` · `Reference?` · `Notes?` · `CreatedById`/`CreatedByName` · `Attachments`.

| Decision | Why |
|---|---|
| **`CreatedById` is a plain string + a name snapshot — no FK to `AspNetUsers`** | A second FK into this table would be a cascade path (SQL Server 1785), `AppUser`'s string Id puts it out of `IGenericRepository`'s reach anyway, and the ledger must still name who logged an entry after that user is deleted |
| Positive `Amount` + a `Type` enum, not a signed amount | A minus sign in a list is easy to misread; the type is explicit and filterable. `SignedAmount` applies the direction |
| Editable and deletable, gated on their own permissions | An internal ops tool where figures get mistyped daily. Every change lands in the audit trail, so a corrected figure is never a silent one |
| `BookingId` is **not** read from the Edit post | An entry never moves between bookings; honouring a tampered value would move money off one balance onto another |
| Uploads carry `[RequestSizeLimit]` + `[RequestFormLimits]` | The 2026-08-02 Kestrel gotcha. Every upload path in this app needs both or a multi-file post dies at ~28.6MB **before** model binding, surfacing as a misleading antiforgery error |

**The first payment is taken on Create.** `ValidateFirstPayment` requires **at least 10% of
`GrandTotal`**, and runs only when the user holds **both** `Bookings.EditPricing` (there's a total to
take 10% of) and `Transactions.Create`.

> ⚠️ **Two permissions and one nullable total gate this, and any of them backwards makes bookings
> unsaveable for somebody.** A user who can't price produces no total, so there is no minimum — the
> rule is **skipped, not failed**. Same shape as the 2026-08-02 bug where a Create-only rule leaked
> into Edit.

**Where it shows:** a Payments card on `Booking/Details` (ledger + `TOTAL / PAID / REMAINING`, action
buttons `d-print-none` but the table prints), a global **`/Transaction`** list with the `applied`
filter idiom + Excel export, and a **Payment** column (`Paid`/`Partial`/`Unpaid`) on `Booking/Index`.

**Deleting a booking deletes its receipts, not just its rows.** `BookingWithDetailsSpec` doesn't reach
`Transactions.Attachments`, so `BookingController.Delete` loads them with their own spec before
deleting — an empty navigation there would leave orphaned files with nothing pointing at them.

**Files:** `BookingTransaction.cs` / `TransactionAttachment.cs` + configs, `PaymentMethod`/
`TransactionType` enums, `TransactionSpec`, `TransactionController`, `TransactionExcelExporter`,
`Views/Transaction/`, `Views/Booking/_BookingTransactions.cshtml` + `_BookingFirstPayment.cshtml`.
Receipts live in `wwwroot/Files/TransactionAttachments/`; the UI is the **third** reuse of
`attachment-dropzones.js` verbatim.

---

## Money Safes & Block Expenses (the cost side)

The app knew what a booking was **worth** and what the client had **paid**. It had no idea what an
operation **cost**. This is that half: the cash boxes the company pays out of, and what each arrival
group cost to run.

**Money Safes are a separate ledger from booking payments, deliberately.** `BookingTransaction` is
untouched — no `SafeId`, no migration on that table. What a client pays and what the company spends
are different books, and merging them would make a "balance" that answers neither question.

| | |
|---|---|
| **`MoneySafe`** | A cash box. User-created, `Name` unique, its own `Currency`, `IsActive` to retire it. |
| **`SafeEntry`** | One movement, and the **only** ledger row there is — `Deposit` / `Withdrawal` / `Expense`. |
| **`ExpenseType`** | A catalogue entry carrying the *rule*: `PerRoom` or `PerPax` (+ `IncludeChildren` / `IncludeInfants`). |
| **`BlockExpense`** | One cost, paid from one safe, applied to one or more arrival groups. |

### The exchange rate — the one place conversion exists

`BlockExpense.UnitsPerUsd` (`decimal(18,6)`, **nullable**) stores how many units of the paying safe's
currency equal one dollar — "1 USD = 48.50 EGP" is `48.50`, and any amount's USD value is
`amount / UnitsPerUsd`. Added when the **Revenue report** needed to subtract an EGP cost from a USD
sale price; the safes themselves still convert nothing.

| Decision | Why |
|---|---|
| Captured **on the expense**, not at report time or per safe | It is what the money was worth *on the day it was paid*. A report-time box would restate settled history every time the rate moved; a per-safe default is a second thing to maintain, and a stale one misprices silently |
| Shown and **required** only when the safe isn't USD | A USD safe needs no rate. The controller **forces 1** there rather than trusting the post — `expense-form.js` only hides the input |
| **Nullable, and the migration carries NO default** | 🔴 `defaultValue: 1` would declare *1 EGP = 1 USD* on every existing expense and understate cost ~48-fold with no error anywhere — the `BaselineNights: 0` trap in its most expensive form. Null means "never recorded", which the Revenue report flags and excludes; 1 is a claim |
| `IExpenseCalculator` never sees it | The rate changes no allocation — only how the result reads against a base-currency price. Keeping it out of the calculator kept the change to a column and a form field |
| `Recalculate` leaves it alone | It re-derives amounts, not what the money was worth on the day |

### Currency is per safe, and it is a label — there is no conversion anywhere

Each safe declares an ISO code; every figure in it is in that code. **No exchange rates, no base
currency, no total across safes** — the expenses list writes a total *per currency* and the `.xlsx`
does the same. `Money.Format(amount, currency)` was added beside the existing `Money.Format(amount)`
so formatting still has one owner; `Money.Code` remains the booking currency and is unchanged.

> **`MoneySafe.Currency` is immutable once the safe holds any movement.** The controller reads it back
> from the stored row instead of the post, and the profile `Ignore()`s it. Changing it would silently
> re-denominate every historical figure in the ledger — the same class of bug as
> `BookingTransaction.BookingId` being ignored on Edit.

### The two formulas

**Per room** — the operator enters a **rate per room, per night** for each `RoomType` at apply time
(never stored on the type; rates change per supplier and per season). Each booking is charged its
*own* stay length:

```
amount(booking) = Σ over its room tally of  rate[type] × quantity × THAT BOOKING'S nights
```

> 🔴 **The per-booking nights rule is the whole point, and a group-level nights figure would look
> right.** Bookings in one arrival group routinely stay different lengths. Verified against a real
> group (Cairo, 01 Aug 2026 — four bookings, three at 2 nights and one at 7): the correct answer is
> **1320.00**, where a `MaxNights` rule gives 2870 and a `MinNights` rule gives 820. None of them
> errors; only the arithmetic tells them apart.

**Per pax** — the operator enters **one lump-sum total for the whole expense**. It is divided across
the combined eligible pax of *every* selected group — one pool, not one division per group — then
allocated down. `eligible = Adults + (IncludeChildren ? Children : 0) + (IncludeInfants ? Infants : 0)`.

Allocation is **largest-remainder at 2 dp**: floor every line to whole cents, then hand the leftover
cents out by descending fractional part, tie-broken by eligible pax, then booking id, then position.
Group amounts are the **sum of their booking amounts**, never an independent rounding — which is what
makes both levels reconcile to the entered figure *exactly*. Verified on totals that cannot divide
evenly (1000.00, 100.01 and 0.07 across 15 passengers) — all allocate to the cent.

> 🔴 **Allocation must be keyed by (group, booking), never by booking id.** A booking legitimately
> belongs to several selected groups — a Cairo + Cruise booking is in both — and each is its own
> allocation line. Keying on the booking id alone collapses them; the first implementation did, and
> threw `An item with the same key has already been added` on the duplicate. It is indexed by position
> now. The same shape would silently *under*-charge if the dictionary had been built with an
> overwrite instead of an `Add`.

### One expense, several groups, one withdrawal

`SafeEntry.ExpenseId` is a nullable cascading FK to `BlockExpense`, and **`BlockExpense` carries no
`SafeId`** — the safe is reached through the entry.

| Decision | Why |
|---|---|
| One ledger table, not a union of manual entries and expenses | The balance stays one formula over one table. A union means every balance, filter, page and export joins two shapes and keeps two sort paths agreeing — two places to get the sign wrong |
| The FK lives on the **entry** | A `SafeId` on the expense would give SQL Server two cascade paths from `MoneySafes` into `SafeEntries` (direct, and via `BlockExpense`) — **error 1785**. It also removes any chance of an expense and its own withdrawal disagreeing about which safe paid |
| `MoneySafeController.Delete` refuses a safe holding movements | The cascade would otherwise take the withdrawals behind real expenses with it. `IsActive = false` is the retire; the cascade is only a backstop |
| Expense-backed entries refuse Edit/Delete **from the ledger** | They are owned by the expense. Same call `SetStatusAsync` makes rejecting a derived `BlockStatus`: a value only one path may produce needs the other paths to say no out loud |

### Frozen snapshot, and what Recalculate means

Every amount is **frozen** — the money genuinely left the safe at that figure. When the blocks later
change, the expense *says so* rather than re-deriving itself, which would move a balance nobody
touched. **`ExpenseFingerprint`** (`Travel.Domain/Services/Expenses/`) is the pure owner of that
comparison, mirroring `BlockSnapshot`, and canonicalises exactly what the two formulas read:
booking id, pax breakdown, that booking's nights in this group, and its room tally. Hotel, flights,
guest names and notes deliberately don't flag.

**`BlockSnapshot.RoomsFingerprint` gained a `RoomTally` overload** rather than a second copy of
the `"1x2|2x1"` spelling — it stays the one owner of that format.

> **Recalculate means two different things, and the confirm dialog must say which.** Verified both:

| Basis | What it does | The safe |
|---|---|---|
| `PerRoom` | Re-applies the stored rates to the group's **current** rooms and nights | The withdrawal changes — balance moves |
| `PerPax` | **Opens a modal pre-filled with the current total.** The operator confirms it or types the real invoiced figure, then it re-splits | Moves only if they changed the figure |

A lump sum is **negotiated, not derived**. Holding the old per-person rate would turn a negotiated
total into a rate nobody agreed; silently holding the old total would hide a supplier invoice that
went up. Asking is the only honest option — this was the user's explicit call.

The flag costs **two queries per page**: one for the expenses, then one `ExpenseChangeDetector` call
for every group on the page, which reuses the existing `BlockGroupLoader.LoadGroupStaysAsync` (the
composite-key loader — EF can't translate a composite `Contains`). A group that has emptied entirely
has no entry, so its live fingerprint is `""`, never matches, and correctly reads "changed".

### Applied to: arrival groups **or** bookings

An expense targets one of two things, chosen by a switch on Create and **fixed afterwards** like the
safe and the type — **`BlockExpense.Target`** (`ExpenseTarget`: `ArrivalGroups = 1`, `Bookings = 2`).
A booking expense is for a cost that belongs to the trip itself — a transfer, a guide, a cancellation
fee — rather than to a hotel arrival. One expense never mixes the two (the user's call).

**Same two formulas, over bookings.** Per room = Σ(rate × qty) × the booking's **whole-trip nights**
(`Booking.TripNights` = Σ `Stays.Nights`); per pax = one lump sum split across the eligible pax of every
ticked booking, by the same largest-remainder pass. `ExpenseCalculator` now runs both targets through
one private core over flat `ChargeLine`s (still indexed by **position**), so the arithmetic exists once.

| Decision | Why |
|---|---|
| A separate table, **`BlockExpenseDirectBooking`** (`ExpenseId`, `BookingId?`, the `BlockExpenseBooking` snapshot + its own `AppliedFingerprint`) | `BlockExpenseBooking` hangs off a group, which needs a destination + check-in. A second, direct `ExpenseId` FK on it would be two cascade paths from `BlockExpenses` (1785) |
| Booking FK `NoAction`, **NULLed** by `BookingController.Delete` | Same as its sibling: the line records money that already left a safe. Verified the raw delete is blocked by `FK_BlockExpenseDirectBookings_Bookings_BookingId` |
| Fingerprint per line, `ExpenseFingerprint.ForBooking` (pax + trip nights + rooms) | `IExpenseChangeDetector.CurrentBookingFingerprintsAsync` answers a whole page in one query. A deleted booking reads `""` and flags; Recalculate then drops it, the mirror of an emptied group |
| **No status filter** in `BookingsForExpenseSpec` | The operator ticked the booking by hand; a cancelled one is how a cancellation fee is recorded. Cancelling changes no fingerprinted fact, so it doesn't flag |
| Picker hides cancelled bookings **unless "Show cancelled"** | They're listed only for real costs. The `bookingId` shortcut turns the toggle on for a cancelled booking, or its own pre-selection would be hidden |
| Destination filter matches booking expenses through `DirectBookings.Booking.Stays` | Otherwise filtering the list by Cairo would hide every booking expense |
| `Target` migration hand-edited to **`defaultValue: 1`** | The scaffolder emitted 0 — the enum-default trap again |

**Where it shows:** the Create form's *Applied to* switch + booking picker (filter by arrival window,
destination on any stay, code/guest search — `Expense/Bookings` JSON, gated on **View**); a **Record
expense** button on `Booking/Details` (`/Expense/Create?bookingId=`); booking chips on the list cards
and an `Applied to` filter; a flat bookings table on `Expense/Details`; `{n} booking(s)` + indented
booking sub-rows in the `.xlsx`; and the Revenue report, whose `LoadCostsAsync` now reads **both**
allocation tables (3 queries for the whole report).

**`Booking/Details` Expenses card** (`_BookingExpenses.cshtml`, gated on `Expenses.View`,
`d-print-none`) lists the booking's direct expenses **and** its share of every arrival-group expense,
each with its *Needs recalculating* flag, and a TOTAL **per currency**. One query —
`BlockExpenseSpec.ForBooking` matches either kind — plus one detector call per kind.

**Fixed along the way** (pre-existing): the server-rendered group tokens dropped `PrivateBookingId`, so
a private group pre-selected from `BlockService/Details` never ticked; Edit's live preview never ran
(no selection inputs rendered — the locked selection now posts as `.js-locked-selection` hiddens);
`Preview` read the type's **live** rule, now the expense's snapshot when `Id > 0`; the Edit form had no
`SelectedGroupKeys` span; `GroupsTouched` ignored `PrivateBookingId`. The page is now **Expenses**,
not "Block Expenses", in the sidebar and title.

### Screen-only, by the "is this ours, or theirs?" rule

`BlockService/Details` gained a read-only expenses card, `d-print-none`, and **nothing reaches
`BlockExcelExporter`**. Verified by unzipping a real export: the file contains the block data and zero
trace of the cost. The block file goes to the **hotel supplier**; what we paid is ours.

That page's `@model` changed from `BlockFile` to **`BlockDetailsViewModel`** — only two `Model.`
usages existed, so the documented rename trap was avoided, but check `var file = Model.File;` after
any future global replace.

**Permissions:** `MoneySafes` (Order 11, View/Create/Edit/Delete/Export), `ExpenseTypes` (12,
View/Create/Edit/Delete), `Expenses` (13, View/Create/Edit/Delete/Export) — **+14, to 64**, which is
what forced the claim packing above. Recording or correcting a manual movement rides on
`MoneySafes.Edit`, deleting one on `MoneySafes.Delete`; the catalog descriptions say so explicitly
rather than burying it.

**UI:** `MoneySafe/Index` and `Expense/Index` are **card grids**, not tables — the client asked for it,
and it also means neither page has positional DataTables indices to keep in step. The only new table
is the ledger on `MoneySafe/Details`, which carries a **running-balance column** so an operator can see
*when* a safe went negative, not just that it is. Its Actions `<th>` always renders (empty without the
permission) so the indices stay fixed for every user.

**Files:** models `MoneySafe`/`SafeEntry`/`SafeEntryAttachment`/`ExpenseType`/`BlockExpense`/
`BlockExpenseRate`/`BlockExpenseGroup`/`BlockExpenseBooking` + `MoneySafeConfig.cs` and
`BlockExpenseConfig.cs`; enums `SafeEntryType`/`ExpenseBasis`; `Services/Expenses/`
(`IExpenseCalculator`, `ExpenseDtos`, `ExpenseFingerprint`, `IExpenseChangeDetector`) with impls in
`Travel.Services/Expenses/`; `Specifications/Expenses/`; `MoneySafeController` /`ExpenseTypeController`/
`ExpenseController`; `Views/{MoneySafe,ExpenseType,Expense}/`;
`Views/BlockService/_BlockExpensesCard.cshtml`; `SafeLedgerExcelExporter` + `ExpenseExcelExporter`;
`expense-form.js` + `expense-type-form.js`; migration `AddMoneySafesAndBlockExpenses`. Receipts live in
`wwwroot/Files/SafeAttachments/` — the **fourth** verbatim reuse of `attachment-dropzones.js`.

---

## Booking Audit Trail

Every change to a booking, **derived automatically from the EF ChangeTracker** by
`BookingAuditInterceptor` (a `SaveChangesInterceptor`) — so a field added next month is covered with
nothing to remember to log.

Two tables: **`BookingAuditEvent`** (one save: when, who, action, summary) and
**`BookingAuditChange`** (one field: entity type, label, field, old → new, kind). Grouped by the
event row rather than by timestamp — two saves in the same second are two events.

> 🔴 **The wholesale delete-and-recreate is the whole difficulty, and getting it wrong looks like
> working code.** `BookingController.Edit` deletes every `BookingStay`/`BookingGuest`/`BookingRoom`/
> `BookingTour` and adds fresh ones on **every** save. A naive ChangeTracker dump reports *"3 stays
> removed, 3 stays added"* when the operator only fixed a phone number — noise that makes the trail
> useless within a week. `DiffChildren` **pairs Deleted with Added rows of the same parent and
> `SortOrder`, compares them by value, and emits nothing when they match**; unpaired leftovers are
> genuine additions or removals. Verified: a phone-only edit logs exactly **one** change.

| Decision | Why |
|---|---|
| An interceptor, not per-action logging | Per-action logging goes stale the first time someone adds a field |
| `ICurrentUserService` (Domain interface, web impl) | The interceptor lives in `Travel.Repository`, which must not see `IHttpContextAccessor` — same rule that keeps `IFileStorageService` taking a `Stream` |
| The **Created** event is written by the controller, not the interceptor | A new booking has no id until `SaveChanges` returns, so the interceptor has nothing to key its changes to and skips `Added` bookings entirely |
| Values stored as **strings**, rendered like the UI (`Yes/No`, `dd MMM yyyy`, `#,##0.00`) | An audit row records what was *displayed*, not a typed value to compute with — and it spares the table a column per CLR type |
| `Id`/`BookingId`/`CreatedAt`/`SortOrder` excluded | Otherwise every entry is buried in noise nobody asked for |
| Booking FK is `Cascade` | Deleting a booking takes its trail with it. Preserving orphaned history is a different feature ("who deleted what") and wasn't asked for |
| Card is `d-print-none` | Internal history, not part of the booking document — the same call made for the block email links |

Transactions are diffed **straight, without pairing** — each is its own deliberate action, never
rebuilt wholesale.

> 🔴 **`DiffStayRates` exists because a grandchild drops out of the trail silently.** Extra-night
> rates were audited for free while the rate was a column on `BookingStay`; as `BookingStayRate` rows
> they leave `entry.Properties`, so a save that only re-priced an extra night wrote **no change and
> therefore no audit event at all** — money moving silently. It walks the **stays**, not the rate
> entries, for two reasons: an Added rate's `StayId` is still 0 (and `Property("StayId").CurrentValue`
> is a **negative temporary key**, which `CompareByValue` would happily log as `42 → -2147482647`
> because `"StayId"` is not in `IgnoredFields`); and it emits only for a **paired** stay slot, since a
> genuinely added or removed stay is already reported by `DiffChildren` and re-listing its rate rows
> underneath is exactly the noise the pairing exists to kill. Verified both ways: a phone-only edit
> still writes **zero** events, a DBL-rate edit writes **exactly one**.

**`DiffTourGuests`** is the same shape for `BookingTourGuest` (who is on a tour): paired tour slots
only, never through `CompareByValue` (an Added link's `GuestId` is a temporary key). It reads the old
side from the **Deleted link entries**, not `oldTour.Guests`, because Edit also deletes the guests
and their client cascade runs over those rows. Verified: a phone-only edit writes only the phone
change; adding an adult writes no tour change; unticking a guest writes `Pax 3 → 2` + `Guests`.

**Files:** `BookingAuditEvent.cs` / `BookingAuditChange.cs` + `BookingAuditConfig.cs`, `AuditAction`/
`AuditChangeKind` enums, `Travel.Repository/Data/Auditing/BookingAuditInterceptor.cs`,
`Travel.Domain/Services/ICurrentUserService.cs` + `Travel.Presentation/Services/CurrentUserService.cs`,
`Views/Booking/_BookingAuditTrail.cshtml`. Registered via `options.AddInterceptors(...)` inside
`AddDbContext`'s `(sp, options)` overload, because it needs the scoped `ICurrentUserService`.

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

Each report is a flat item under the sidebar's **REPORTS** title (see Sidebar under UI/UX Guidelines). A new report is a new `<li>` there, with `ActiveItem("Report", "<Action>")`, and its View permission added to the REPORTS title's `mode="any"` list, or a user holding only that report sees an item with no heading.

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

### Revenue (`/Report/Revenue`)

One row per booking — **Booking no · Lead guest · Arrival · Package · PAX · Sale · Expenses · Net ·
Margin %** — with summary tiles and a reconciling TOTAL row. The first screen that puts what a trip
**earned** next to what it **cost**.

| Decision | Why |
|---|---|
| The range is on **`ArrivalDate`** | Revenue is recognised when the trip ran. A booking sold in March for an August arrival belongs to August, alongside the costs of running it — which is what makes Sale − Expenses like-for-like. Creation date would be a *sales* report and would pair this month's sales with next month's costs |
| Sale price is **`GrandTotal`** | Pax + extra nights + tours: the full contracted value, the figure The Money Model says every "total" surface must read. Not `PaidTotal` — a delivered-but-unpaid trip still earned its revenue |
| **Costs are NOT filtered by the report's date range** | The booking is the unit. A trip that ran in August whose hotel was settled in July still cost that money; filtering costs by `SpentOn` would flatter August's margin and orphan July's spend |
| A booking in three arrival groups sums **all three** shares | That is what the trip cost — three suppliers. The per-group split is `Expense/Details`' job |
| Unpriced bookings are **listed, not hidden** | `—` in Sale/Net/Margin and a note under the table. A filtered-out row can't complain — the fifth time that rule has applied |
| Own permission pair, `Reports.ViewRevenue` / `ExportRevenue` | The per-report pattern. ⚠️ It shows **sale prices without `Bookings.ViewPricing`** — a second gate would leave a holder of this permission staring at an empty report, which is worse. The catalog descriptor says so explicitly |

> 🔴 **`RevenueReportSpec`'s includes are load-bearing and their absence is silent.**
> `Booking.GrandTotal` is computed in C# and, in its own words, "reads 0 for the components whose
> navigation wasn't included" — so dropping `Stays` loses every extra-night charge and dropping `Tours`
> loses every tour. The report still renders, still totals, and is simply **too low**. Verified against
> `MUSB-000014`, chosen because it has **both**: pax 400 + extra nights 50 + tours 200 = **650.00**,
> where a missing include would read 600 or 450. A test booking without both proves nothing.

**Currency** — this is what forced `BlockExpense.UnitsPerUsd` (see Money Safes & Block Expenses).
Every allocation is converted with the rate stored on its own expense; an allocation whose expense has
**no** rate is **excluded and counted**, never folded in at face value — treating EGP as USD would
understate a cost ~48-fold and flatter the margin. The count surfaces per row, as a banner, and in the
`.xlsx`.

> **The Expenses column sums every listed row, including unpriced ones.** `TotalSale` skips them
> (their revenue is unknown), but their **cost is real and is shown on their row** — so a footer that
> skipped it would not equal the column above it. A total that doesn't reconcile with its own column
> is worse than one needing a footnote, and the unpriced note is that footnote: net and margin read
> *lower than reality* until those bookings are priced. Both directions are stated on screen and in
> the sheet.

`RevenueReportRow` (in `RevenueReportViewModel.cs`) is the single shape the table and the `.xlsx`
render, built once by `ReportController.BuildRevenueAsync`. `NetRevenue` and `MarginPercent` are
**`decimal?`** for the same reason `Balance` is: "0.00 net" on an unpriced booking reads as
break-even, the opposite of "we don't know yet".

**Cost lookup** — `Booking` has no allocations navigation (the FK is `NoAction` with no inverse, per
the 1785 rule), so `LoadCostsAsync` queries `BlockExpenseBooking` directly with
`AddInclude("ExpenseGroup.Expense.SafeEntry.Safe")` and groups in memory. **Two queries for the whole
report**, regardless of row count.

**Excel** — `RevenueReportExcelExporter`: money as numbers with `Money.ExcelNumberFormat`, **margin as
a real percentage cell** (`margin / 100` with format `0.0%`, not the number 42.1), bold TOTAL, and both
exclusion notes written into the sheet. Unpriced rows leave Sale/Net/Margin genuinely **empty** rather
than 0.

**Files:** `Specifications/Bookings/RevenueReportSpec.cs`, `ViewModels/RevenueReportViewModel.cs`,
`Services/RevenueReportExcelExporter.cs`, `Views/Report/Revenue.cshtml`, the Revenue actions in
`ReportController`.

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
| `ICurrentUserService` | `CurrentUserService` (Travel.Presentation/Services) | Current user id + name for the audit trail and payment ledger — see Booking Audit Trail |
| `IExpenseCalculator` | `ExpenseCalculator` (Travel.Services/Expenses) | The one owner of both expense formulas, shared by the preview, Create, Edit and Recalculate — see Money Safes & Block Expenses |
| `IExpenseChangeDetector` | `ExpenseChangeDetector` (Travel.Services/Expenses) | "Have these arrival groups changed since we paid for them?" for a whole page in one query |

Services return **`OperationResult` / `OperationResult<T>`** (`Travel.Domain/Services/OperationResult.cs`) with `From(IdentityResult)`; controllers turn that into `ModelState` errors or the `{ success, message }` JSON the AJAX flows expect.

> Business-logic services go in `Travel.Services` (interface in Domain, impl in Services). Keep them free of **web** types — `IFormFile`, `SignInManager`, controllers, `HttpContext` — so a future `Travel.Api` could reuse them. `UserManager`/`RoleManager` **are** allowed: they live in `Microsoft.Extensions.Identity.Core` (no HTTP coupling), an API would inject the same types, and `AppUser : IdentityUser` already puts Identity on Domain's public surface. `SignInManager.RefreshSignInAsync` stays in controllers.

---

## Database

**`TravelContext` : `IdentityDbContext<AppUser>`** — applies configs via `modelBuilder.ApplyConfigurationsFromAssembly(...)`. It also carries **`BookingAuditInterceptor`**, registered through `AddDbContext`'s `(sp, options)` overload; every `SaveChanges` on this context writes the booking audit trail. Add DbSets + `Config` classes as entities are introduced.

> **Nullable columns in a unique index need `.HasFilter("[Col] IS NOT NULL")`.** SQL Server compares
> NULLs as *equal*, so an unfiltered unique index over a nullable column rejects the second NULL row —
> see the 2026-08-20 gotcha, where it would have broken the second manual safe deposit ever created.

**FK cascade note (SQL Server):** SQL Server forbids multiple cascade paths to the same table (error 1785). As relations are added, use `DeleteBehavior.Cascade` only for true ownership FKs and `NoAction` for reference FKs to break cycles.

**Seeding:** `AppIdentityDbContextSeed.SeedUserAsync` seeds one admin (`MustafaAdmin`) on first run, called from `Program.cs` after `MigrateAsync()`. No roles seeded yet.

**Notion import (done — no longer called).** `Travel.Repository/Data/NotionBookingSeed.cs` imported
`Data/SeedData/notion-bookings.json` (93 bookings from Notion's "Reservas de Viagem", arrivals from
2026-09-01, exported 2026-10-18) into **production** on startup, once, on 2026-10-18. Its call has been
removed from `Program.cs`; the seeder and the JSON stay in the repo as a record. It ensures the Luxor
destination and the two Cairo + Luxor packages, then creates each booking with a generated itinerary, a
Created audit event and a final `RecalculateAsync`. A row is skipped when its Notion URL is already in
some booking's Notes or a lead guest with the same (accent/case-insensitive) name exists. Imported
bookings carry `Imported from Notion: <url>` in their Notes.

> 🔴 **Don't re-wire it without re-checking the package codes.** The JSON's `packageCode` values are
> production's, not the local DB's — locally `MUSP-000001` is "Cairo" and `MUSP-000002` is
> "Cairo + Luxor", so a local run would put 56 cruise bookings on the Luxor package, silently. And on
> production, a re-run would re-import any imported booking deleted since.

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
| `SetTheme` | POST | Authorize (no permission) | Saves `AppUser.Theme` via `IUserAccountService.SetThemeAsync`. JSON for the fetch call (`X-Requested-With`), otherwise `LocalRedirect(returnUrl)` so the topbar form works with scripting off |

> A profile view/update-image flow is stubbed out (commented) in `AccountController` — reactivate it once the profile view + `ProfileViewModel` exist.

### HomeController `[Authorize]`
`Index` (dashboard placeholder), `Privacy`, `Error` (**`[AllowAnonymous]`** — it's the `UseExceptionHandler` target; requiring auth there would swallow real errors behind a login redirect).

### BookingController `[Authorize]`
`Index(arrivalFrom, arrivalTo, packageId, status, guestArranged)` / `Details` / `Create` / `Edit` / `Delete` (JSON) / `SetStatus` (JSON — Completed/Cancelled/reactivate only, see Block & Booking Status) / `Preview` (JSON — regenerates the itinerary + embark options for the AJAX schedule editor, shared by Create's live editor and Edit's "Regenerate" button). `Index`'s filters are opt-in GET params bound to `BookingListViewModel` (`HasFilters` distinguishes "no matches" from "no bookings yet") — a first visit with no query string still shows everything, and the `Package` dropdown deliberately includes **inactive** packages, since an old booking commonly points at a retired one and would otherwise be unfilterable. The arrival column carries `data-order` in ISO (`yyyy-MM-dd`) so DataTables sorts it correctly — the visible cell is a display string (`"01 Apr 2026"`), which sorts wrong lexicographically. `Create`/`Edit` carry `[RequestSizeLimit]`/`[RequestFormLimits]` for attachment uploads — see Booking Attachments — and share the schedule/flight-date handling in Booking Schedule & Flights.

### BlockServiceController `[Authorize]`
`Index(from, to, destinationId, status, category, applied)` / `SingleArrivals(same args)` / `Details(destinationId, checkIn, category)` / `Export(destinationId, checkIn, category)` / **`SetStatus`** (JSON POST) / **`UploadDocuments`** (multipart POST → redirect; carries `[RequestSizeLimit]` + `[RequestFormLimits]`) / **`DeleteDocument`** (JSON POST). The two lists share **`BuildListModelAsync`** (and one `BlockServiceListViewModel` carrying both `Blocks` and `Singles`), so their filter semantics can't drift: it defaults `from` to today **only when `applied == false`**, and the filter form posts a hidden `applied=true` so a deliberately cleared From box isn't silently re-filled. `Details`/`Export` return `NotFound()` only when **no** stay matches the key.

### NationalityController `[Authorize]`
`Index` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable` — the `DestinationController` shape with nothing added. See Nationalities.

### RoleController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable`, each `[Authorize(Policy = Permissions.Roles.X)]`. Permissions are edited inline on the form, so `Roles.ManagePermissions` is enforced **inside** the POST and via `<permission>` around the picker.

### UserController `[Authorize]`
`Index` / `Details` / `Create` / `Edit` / `Delete` / `ToggleActive` / `ResetPassword` (all JSON for the AJAX ones) / `Permissions` GET+POST / `IsUserNameAvailable` / `IsEmailAvailable`.
The C# methods behind `/User/Permissions` are named **`DirectPermissions`/`DirectPermissionsPost`** with `[ActionName("Permissions")]` — a member called `Permissions` would shadow the `Permissions` static class and break every `[Authorize(Policy = Permissions.X)]` in the file.

### TourController `[Authorize]`
`Index` / `Create` / `Edit` / `Delete` (JSON) / `IsNameAvailable` — the `NationalityController` shape with a price and an active flag. `Delete` refuses a tour sold on any booking and points at `IsActive = false` instead. See Extra Tours.

### TransactionController `[Authorize]`
`Index(from, to, bookingId, method, type, applied)` / `Export(same args)` / `Create(bookingId)` GET+POST / `Edit(id)` GET+POST / `Delete` (JSON). Both POSTs carry `[RequestSizeLimit]` + `[RequestFormLimits]` for receipt uploads. `Create`/`Edit` redirect back to `Booking/Details`. See Transactions.

### ReportController `[Authorize]`
`Flights(from, to, applied)` / `ExportFlights(same args)` and `Revenue(from, to, applied)` / `ExportRevenue(same args)`, each `[Authorize(Policy = Permissions.Reports.X)]`. Each report goes through one private builder (`BuildAsync` / `BuildRevenueAsync`), so its table and its sheet share rows. `BuildRevenueAsync` also owns `LoadCostsAsync`, which reads the expense allocations for every listed booking in a single query. See Reports.

### MoneySafeController `[Authorize]`
`Index` (card grid — one safes query plus **one** entries query grouped in memory, never one balance query per card) / `Details(id, from, to, type, applied)` (the ledger) / `Export(same args)` / `Create`/`Edit` GET+POST / `Delete` (JSON, refuses a safe holding movements) / `IsNameAvailable` (gated on **View**, not Create) / `CreateEntry(safeId)`/`EditEntry(id)` GET+POST / `DeleteEntry` (JSON). The entry actions refuse expense-backed rows, never read `SafeId` from the post, and delete receipt files by hand. Both entry POSTs carry `[RequestSizeLimit]` + `[RequestFormLimits]`. See Money Safes & Block Expenses.

### ExpenseTypeController `[Authorize]`
`Index` / `Create` / `Edit` / `Delete` (JSON, refuses a type used by any expense) / `IsNameAvailable` — the `TourController` shape. Both POSTs force `IncludeChildren`/`IncludeInfants` false when the basis is `PerRoom`.

### ExpenseController `[Authorize]`
`Index(from, to, safeId, expenseTypeId, destinationId, changedOnly, applied)` (card grid) / `Export(same args)` / `Details(id)` / `Create` GET+POST / `Edit(id)` GET+POST / `Recalculate(id)` (JSON POST) / `RecalculatePreview(id)` (JSON GET) / `Delete(id)` (JSON POST) / **`Preview`** (JSON POST) / `Groups(from, to, destinationId)` (JSON GET). `Preview` and `Groups` are gated on **View, not Create** — the Edit form calls them too. `Edit` re-reads the safe, the type and the group selection from the stored rows rather than the post: re-scoping money already paid is delete-and-re-add. See Money Safes & Block Expenses.

### Views
`Account/Login`+`AccessDenied`, `Home/Index`+`Privacy`, `Booking/`, `BlockService/` (`Index`+`SingleArrivals`+`Details`+`_ArrivalFilters`), `Package/`, `Destination/`, `Nationality/`, `Report/Flights`, `Role/`, `User/`, and `Shared/` partials (`_Layout`, `_AuthLayout`, `_Sidebar`, `_Nav`, `_Head`, `_Scripts`, `_Footer`, `_Notifications`, `_ValidationScriptsPartial`, **`_PermissionModules`**, `Error`).
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

Modules: **Bookings** (+`Regenerate`, `Export`, `ChangeStatus`, `ViewPricing`, `EditPricing`, `ViewAudit`), **BlockServices** (`View`, `Export`, `ChangeStatus`, `ManageDocuments`), **Packages**, **Destinations**, **Nationalities**, **Users** (+`ToggleActive`, `ResetPassword`, `AssignRoles`, `ManagePermissions`), **Roles** (+`ManagePermissions`), **Reports** (`ViewFlights`/`ExportFlights`, `ViewRevenue`/`ExportRevenue` — one pair per report), **Tours** (Order 9), **Transactions** (Order 10, +`Export`), **MoneySafes** (Order 11, +`Export`), **ExpenseTypes** (Order 12), **Expenses** (Order 13, +`Export`). **66 total.**

> **The ~60 cookie ceiling has been crossed, and the documented remedy has shipped** — the principal
> now carries **one comma-joined `permission` claim** instead of one claim each. See Claim packing
> below. The count is no longer the constraint it was.

**Adding a permission:** add the `const` to `Permissions.cs` **and** an entry to `PermissionCatalog.Modules`. `FindCatalogGaps()` logs a Serilog warning at startup if the two drift. A whole new **module** also needs an icon in `Travel.Presentation/Helpers/PermissionIcons.cs` keyed by the same module `Key`, and inserting one mid-list means **renumbering every later module's `Order`**.

### `Travel.Presentation/Authorization/`

- `PermissionRequirement` / `PermissionAuthorizationHandler` — **SuperAdmin short-circuits**; everyone else needs the matching claim.
- `PermissionPolicyProvider` (**singleton**) — builds a policy on demand when `PermissionCatalog.IsValid(policyName)`; anything else falls through to the default provider, so a typo throws loudly instead of silently 403ing.
- `AppUserClaimsPrincipalFactory` — the base `UserClaimsPrincipalFactory<AppUser, IdentityRole>` **already** unions role claims + user claims, so this only de-duplicates, drops claims for permissions removed from the catalog, packs the survivors (below), and emits **none** for SuperAdmin (keeps the cookie small).

### Claim packing — one claim, not sixty-four

The **database** still stores one row per permission in `AspNetRoleClaims` / `AspNetUserClaims`; only
the **cookie** is compacted. `AppUserClaimsPrincipalFactory` emits a single `permission` claim whose
value is the comma-joined list, and `PermissionAuthorizationHandler` splits it back.

**`Travel.Domain/Authorization/PermissionClaims.cs`** is the one owner of that format — `Pack` /
`Unpack` live together so the write and read sides can't drift, the same rule that keeps
`BlockSnapshot` the sole owner of the room fingerprint.

| Decision | Why |
|---|---|
| Only the cookie is packed; the DB keeps one row each | `RoleService` / `UserAccountService` query, add and remove individual claims. Packing the stored rows would break every one of those, and the picker with them |
| `Unpack` also reads the **un-packed** shape | A lone value contains no separator and splits to itself, so cookies issued before this shipped keep authorising until the 2-minute `SecurityStampValidator` tick re-issues them. **Nobody is signed out by the change** |
| `,` is safe as the separator | Every permission value is shaped `Module.Action` and can never contain one |
| The handler is the **only** cookie reader | Verified by grep before the change: the other four call sites all read from the database. That is what made this a two-file change |

> Verified with a non-SuperAdmin holding four permissions: granted pages returned 200, ungranted ones
> landed on `/Account/AccessDenied`, and the sidebar hid what wasn't granted. **SuperAdmin proves
> nothing here** — it bypasses the handler entirely and carries no permission claims, so any test of
> this path must use a plain user.

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
- **`DestinationViewModel`**, **`PackageViewModel`**, **`BookingViewModel`** (+ nested inputs; carries the pricing fields and the `CanEditPricing`/`CanViewPricing` form-context flags).
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
│   ├── Authorization/  AppClaimTypes.cs, Permissions.cs, AppRoles.cs, PermissionCatalog.cs,
│   │                   PermissionClaims.cs
│   ├── Enums/          Gender, GuestType, PackageCategory, RoomType, WeekDays, AttachmentCategory,
│   │                   BlockStatus, BookingStatus, PaymentMethod, TransactionType,
│   │                   AuditAction + AuditChangeKind, SafeEntryType + ExpenseBasis
│   ├── Exceptions/     DuplicateCodeException.cs
│   ├── Models/         BaseModel, ICodedEntity, Booking*, BookingStayRate, BlockServiceStatus,
│   │                   BlockAttachment, BlockRecordedBooking, BookingTour, BookingTourGuest, BookingTransaction,
│   │                   TransactionAttachment, BookingAuditEvent, BookingAuditChange,
│   │                   Destination, Nationality, Package*, Tour,
│   │                   MoneySafe, SafeEntry, SafeEntryAttachment, ExpenseType,
│   │                   BlockExpense, BlockExpenseRate, BlockExpenseGroup, BlockExpenseBooking,
│   │                   BlockExpenseDirectBooking;
│   │                   Models/Identity/AppUser.cs
│   ├── Repositories/IGenericRepository.cs
│   ├── Services/       IFileStorageService.cs, OperationResult.cs, Itinerary/,
│   │                   CodeGeneration/ICodeGeneratorService.cs + CodePrefixes.cs,
│   │                   BlockServices/IBlockServiceQuery.cs + IBlockStatusService.cs + BlockDtos.cs
│   │                                + BlockSnapshot.cs,
│   │                   Expenses/IExpenseCalculator.cs + ExpenseDtos.cs + ExpenseFingerprint.cs
│   │                            + IExpenseChangeDetector.cs,
│   │                   Identity/IRoleService.cs, Identity/IUserAccountService.cs
│   ├── Services/       … ICurrentUserService.cs (who is making the request — the audit trail's
│   │                      only route to a user name without dragging ASP.NET Core into Repository)
│   └── Specifications/ BaseSpecifications.cs, ISpecifications.cs, Packages/, Transactions/,
│                       Bookings/BookingWithDetailsSpec.cs + BookingsForStatusSpec.cs
│                                + BookingDocsSpec.cs + FlightReportSpec.cs
│                                + RevenueReportSpec.cs,
│                       BlockServices/BlockStaysSpec.cs + BlockStatusSpec.cs,
│                       Expenses/SafeEntrySpec.cs + BlockExpenseSpec.cs + BookingsForExpenseSpec.cs
│                                + ExpensePickerBookingsSpec.cs,
│                       CodeGeneration/LastCodedEntitySpec.cs
├── Travel.Repository/
│   ├── GenericRepository.cs, SpecificationEvalutor.cs, UnitOfWork.cs
│   ├── Storage/FileStorageService.cs
│   └── Data/TravelContext.cs, AppIdentityDbContextSeed.cs,
│       Auditing/BookingAuditInterceptor.cs   (the ChangeTracker → audit trail translation)
│       Configrations/…Config.cs, Migrations/
├── Travel.Services/
│   ├── ItineraryGeneratorService.cs
│   ├── BlockServices/BlockServiceQueryService.cs + BlockStatusService.cs + BlockGroupLoader.cs
│   ├── Expenses/ExpenseCalculator.cs + ExpenseChangeDetector.cs
│   ├── CodeGeneration/CodeGeneratorService.cs
│   └── Identity/RoleService.cs, UserAccountService.cs
└── Travel.Presentation/
    ├── Program.cs, appsettings.json
    ├── Authorization/  PermissionRequirement, PermissionAuthorizationHandler,
    │                   PermissionPolicyProvider, AppUserClaimsPrincipalFactory
    ├── TagHelpers/PermissionTagHelper.cs
    ├── Helpers/        AvatarHelper.cs, PermissionIcons.cs, StatusBadges.cs, AttachmentRules.cs,
    │                   Money.cs, ThemeAssets.cs
    ├── Services/        ExcelFormat.cs (shared), BlockExcelExporter.cs, BookingExcelExporter.cs,
    │                    FlightReportExcelExporter.cs, TransactionExcelExporter.cs,
    │                    SafeLedgerExcelExporter.cs, ExpenseExcelExporter.cs,
    │                    RevenueReportExcelExporter.cs,
    │                    CurrentUserService.cs
    │                    (ClosedXML — web project keeps the NuGet out of Travel.Services)
    ├── Controllers/    Account, Home, Booking, BlockService, Package, Destination, Nationality,
    │                   Tour, Transaction, Report, Role, User, MoneySafe, ExpenseType, Expense
    ├── ViewModels/, MappingProfiles/
    ├── Views/ (Account, Home, Booking, BlockService, Package, Destination, Nationality, Tour,
    │           Transaction, Report, Role, User, MoneySafe, ExpenseType, Expense, Shared)
    └── wwwroot/
        ├── css/app-extras.css   (ONLY place for project CSS — tokens + components, see UI/UX Guidelines)
        ├── js/theme-sync.js     (localStorage theme cache + the topbar toggle, loaded in <head>)
        ├── back/                (Skote template: css/, js/, libs/, images/)
        ├── Files/               (uploads: Packages/, Users/, BookingAttachments/,
        │                         BlockAttachments/, TransactionAttachments/, SafeAttachments/)
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

- **Template:** Skote Admin & Dashboard (Bootstrap 5.0.0-beta2 + jQuery), **re-skinned** (2026-10-21) to a
  shadcn-style minimal look entirely in CSS — see The design-token layer. Skote's markup, JS and vendor CSS
  are untouched; DataTables, metisMenu, Dropzone, SweetAlert2, Toastr and the beta2 Modal API all still run.
- **Look:** zinc neutrals, 1px borders + a hairline card shadow, hierarchy from type and spacing, **one
  accent (indigo `#4f46e5`)**, Poppins, dark zinc sidebar in **both** themes, compact tables. Each was the
  client's pick from side-by-side screenshots — don't drift from them without asking.
- **Notifications:** Toastr via `TempData`. **Tables:** DataTables. **Delete:** SweetAlert2 confirm → AJAX POST → Toastr.
- **Forms:** Bootstrap card forms with `jquery.validate` + unobtrusive validation.
- **Layouts:** `_AuthLayout` (login, no sidebar) vs `_Layout` (admin: sidebar + topnav + footer + theme sidebar).
- **Branding:** Musta Travel logo at `wwwroot/back/assets/images/Logo_MustaTravel.png` — use it in the auth + admin layouts.

### Sidebar (`Views/Shared/_Sidebar.cshtml`)

Grouped **by job**, and every item is **flat** (one click, its own icon, no `has-arrow` dropdowns). Each
group was the client's call (2026-10-21):

| Title | Items |
|---|---|
| — | Dashboard |
| **Operations** (daily work) | Bookings, Block Services |
| **Finance** (the cost side) | Money Safes, Expenses |
| **Reports** (read-only, filterable, exportable) | Flights, Revenue, **Payments Received** (`/Transaction` — was "Income Transactions") |
| **Setup** (catalogues) | Packages, Destinations, Extra Tours, Nationalities, Expense Types |
| **Administration** | Users, Roles |

- **Each title is its own `<permission mode="any">` listing exactly its items' View permissions.** A new
  item needs its permission added to its title's list too, or a user holding only that permission sees an
  item with no heading.
- **The active item is set server-side** by `ActiveItem` / `ActiveLink` in the partial's `@functions`,
  matching on the **controller** (and the action for `ReportController`, which hosts several reports).
  Skote's `app.js` only highlights an exact-URL match, so before this, Details/Create/Edit pages and
  `SingleArrivals` highlighted nothing. Both scripts can run on a page; they add the same classes.
- No count badges, by choice. Single Arrivals still has no entry (see Block Services).

### ⚠️ The template is Bootstrap **5.0.0-beta2**, not 5.3

Before using any Bootstrap utility class, check it exists in `wwwroot/back/assets/css/bootstrap.min.css`. Notably **missing** from this build:
- `bg-*-subtle`, `text-*-emphasis`, `border-*-subtle` (all arrived in 5.3)
- `--bs-primary-rgb` and every other `--bs-*-rgb` variable — so `rgba(var(--bs-primary-rgb), .1)` silently fails
- `btn-soft-*` — Skote's own spelling is the two-class `bg-primary bg-soft`
- `fw-semibold`, `bootstrap.Modal.getOrCreateInstance`

`bg-*-subtle`, `btn-soft-*` and the `bg-* bg-soft` pairs are used across the views, so rather than rewrite
them they are **defined in `app-extras.css` from the status tokens** (section 2 and 11c).

### `wwwroot/css/app-extras.css` — the only place for project CSS

Never edit `bootstrap.min.css` / `app.min.css` / `app-dark.min.css` or anything under `wwwroot/back/` (a Skote update replaces them). Linked last in `_Head.cshtml` **and** `_AuthLayout.cshtml` (with `asp-append-version`), deliberately **without an `id`**: `theme-sync.js` switches light/dark by rewriting the `href` of `#bootstrap-style` and `#app-style`, so an id-less link survives the swap and always wins the cascade.
> `wwwroot/css/site.css` and `Views/Shared/_Layout.cshtml.css` exist but are **not referenced by anything** — don't put styles there. (`_Layout.cshtml.css` would actually override Skote's primary color if CSS isolation were ever wired up.)

### The design-token layer

Section 0 of `app-extras.css` defines every colour, radius, shadow, size and glyph as a `--mt-*` custom
property on `:root` (light) and again on `:root[data-theme="dark"]`. **Every rule below section 0 reads
only those tokens** — no literal colour anywhere else, the `@media print` block excepted (paper is white).

| Group | Tokens |
|---|---|
| Surfaces | `--mt-bg` (page), `--mt-surface` (cards, inputs, menus), `--mt-surface-muted` (table header, input-group, inactive), `--mt-surface-hover` |
| Lines | `--mt-border`, `--mt-border-strong`, `--mt-input-border` |
| Text | `--mt-text`, `--mt-text-muted`, `--mt-text-subtle`, `--mt-text-on-accent` |
| Accent | `--mt-accent`, `--mt-accent-hover`, `--mt-accent-text` (links / `text-primary`), `--mt-accent-soft`, `--mt-ring` (focus) |
| Status | `--mt-{success,warning,danger,info,neutral}` + `-soft` (badge/alert fill) + `-text` (small text); `--mt-ink`, `--mt-ink-soft`, `--mt-on-ink` (the "dark" utilities, 5★ tag) |
| Sidebar | `--mt-sidebar-{bg,border,text,text-strong,icon,label,hover-bg,active-bg,active-text}` — dark zinc in both themes |
| Shape / elevation | `--mt-radius-{sm,md,lg,pill}`, `--mt-card-shadow`, `--mt-shadow-xs`, `--mt-shadow-overlay`, `--mt-backdrop` |
| Type / space | `--mt-font-sans`, `--mt-font-mono`, `--mt-fs-{xs,sm,base,md,lg,xl}`, `--mt-space-1…6` |
| Density | `--mt-cell-y`, `--mt-cell-x` — one value for every table (compact) |
| Glyphs | `--mt-select-arrow`, `--mt-search-glyph`, `--mt-switch-knob(-on)`, `--mt-check(-success)`, `--mt-toast-icon-*` (SVG data URIs) |

The tokens also feed Bootstrap's few live variables: `--bs-font-sans-serif`, `--bs-primary`, `--bs-blue`.

### Dark mode — per account in the DB, cached in localStorage

**The account's `AppUser.Theme` is the source of truth; `localStorage["mt-theme"]` is only a cache.**
When they disagree, the database wins, and the user picked that. A theme chosen on the laptop shows on the
phone's next page load.

| Piece | Role |
|---|---|
| `_Layout.cshtml` | Reads the user and renders `<html data-theme="…" data-theme-source="account">`, then passes the theme to `_Head` (`@model AppTheme`), which emits the matching `#bootstrap-style` / `#app-style` hrefs. **The first paint is right with no script.** It costs no query: `_Nav` loads the same user, and EF's `FindAsync` serves the second lookup from the change tracker |
| `Helpers/ThemeAssets.cs` | The one owner of the spelling: `DataTheme`, `BootstrapCss`/`AppCss`, `Opposite`, `ToggleLabel`. `theme-sync.js` repeats the file names (it swaps them client-side), so change both together |
| `_Nav.cshtml` `#theme-form` | The topbar sun/moon. It is a **real form** (`Account/SetTheme`, antiforgery from the form tag helper), so it works with scripting off. Both icons render, and `app-extras.css` shows the one that matches `data-theme` |
| `wwwroot/js/theme-sync.js` | Sync in `<head>`, both layouts. With `data-theme-source="account"` it only refreshes the cache. With no source (the login page, an anonymous error page) it paints the cached theme before first paint. It intercepts the form: apply at once, `fetch` with `X-Requested-With`, one save in flight, and on failure revert plus `toastr`. A `storage` listener moves other open tabs |
| `AccountController.SetTheme` → `IUserAccountService.SetThemeAsync` | `[Authorize]` only, because it is a personal preference with no permission. `UpdateAsync` doesn't rotate the security stamp, so nobody is signed out |

Reading the DB per request, rather than putting the theme in a cookie claim, is what keeps "DB wins" exact.
A claim would lag up to the 2-minute `SecurityStampValidator` tick on other devices and need a `RefreshSignInAsync`.

**The right-bar Settings panel (`_RightSideBar.cshtml`) is gone.** The theme was its only setting.
Vendor `app.js` still contains Skote's switcher, but it is now **inert**. It acts only when
`#light-/#dark-mode-switch` is checked, and those elements no longer exist. Its stray
`sessionStorage["is_visited"]` write is harmless. Don't re-add those ids, or `app.js` starts rewriting the hrefs again.

Migration `AddUserTheme` is **hand-edited to `defaultValue: 1`**, because the scaffolder emitted 0 (the enum-default trap).

So **`:root[data-theme="dark"]` is the dark-mode hook** — redefine tokens there, not in component rules.
Skote's own dark stylesheet still loads underneath; anything a token doesn't reach (e.g. bootstrap-dark's
navy `.list-group-item`) needs an override in `app-extras.css` that reads tokens.

### Rules for new components

- **Read tokens only.** A new colour is a new token in section 0 with a light *and* a dark value.
- **The accent is scarce:** active nav item, primary buttons, links, focus rings, `bg-primary-subtle` counts.
  Pagination's current page is deliberately neutral. Never use it for a state.
- **Statuses come from `StatusBadges`, categories from `CategoryBadges.Css`** — never a hand-written badge
  class (Booking Index/Details both hardcoded amber categories until the re-skin). Category and Private
  are colourless **tags** (`badge-tag` / `.bg-dark-subtle`), so they can never read as a status: 5★ solid
  ink, 4★ outline, Private a dark ring. `.bg-dark-subtle` **is** the Private class (also built in
  `expense-form.js`) — don't reuse it for anything else.
- **Vendor CSS linked in a view body** (`dataTables.bootstrap4`, `dropzone`, `bootstrap-timepicker`) loads
  *after* `app-extras.css` — beat it with specificity (`html …` prefix), not position.
- **Skote chrome rules are keyed on `body[data-sidebar=dark]`**, and `.mm-active` colours are `!important`;
  match them with `body[data-sidebar] …` (and `!important` only for the active item).
- **SweetAlert2 button colours are inline styles** (`confirmButtonColor: '#4f46e5'` accent / `'#f46a6a'`
  danger / `'#74788d'` cancel in ~5 views). CSS can't reach them — change them with `--mt-accent`.
- **Motion:** animate only `transform` / `opacity`; no `backdrop-filter`; no transition or box-shadow on `<tr>`.
- **Money figures never wrap** (`.safe-balance`, `.expense-total`); a no-wrap header button group needs a
  phone-width wrap (see the `.print-sheet` header rule). Check 375px — no page may scroll sideways.
- **Print:** extend the one `@media print` block. It resets the tokens to ink-on-paper for **both** themes,
  and gives tags a real border because browsers drop backgrounds and box-shadows on paper.
- `color-mix()` is used for the badge/alert hairline rings — evergreen browsers only, fine for this app.
- **Font:** Poppins arrives via an `@import` in vendor `app.min.css` (and its `&amp;display=swap` means
  swap is *not* in effect). Pointing `--mt-font-sans` elsewhere stops the woff2 downloads but not that
  CSS request; a second font would be loaded in `_Head` + `_AuthLayout` with preconnect + `display=swap`.

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
- **Cookie size:** no longer a per-permission concern — the principal carries **one packed `permission` claim** (see Claim packing). Adding permissions now grows that single value by a few bytes each rather than adding a claim, so the next ceiling is far away. An `ITicketStore` remains the fallback if it is ever reached.

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
- **A block's key is `(destinationId, checkIn, category)`, not an Id.** The link can go stale between the list and the click (a booking gets edited, the group drops to one, or empties). `GetBlockAsync` returns `null` only when **nothing** matches the key; a group that dropped to one booking renders as a single-arrival file. **This is the reverse of the original rule** (`null` below 2 bookings, 404) — see the 2026-08-06 entry below for why it changed.
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
- **The `_Code` index-name trap, again.** `UnitOfWork.IsCodeUniqueViolation` string-matches `_Code`, so `IX_BlockRecordedBookings_Block_Booking` is named deliberately — a `(block, booking)` collision must surface as itself, not as `DuplicateCodeException`.
- **Two FKs into one child table trip SQL Server's multiple-cascade-path rule (1785).** `BlockRecordedBooking` points at both `BlockServiceStatus` and `Booking`; the group FK is `Cascade` (ownership) and the booking FK is `NoAction`, with `BookingController.Delete` clearing the rows by hand. Don't "fix" the manual delete by flipping the FK to Cascade.
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

### 2026-08-14 — Traps around Booking Pricing

- **🔴 A conditionally-rendered column makes the DataTables positional indices conditional too.** The
  positional-index trap is documented three times above, but every previous case was a fixed shift. The
  `Total` column on `Booking/Index` only renders for a user holding `Bookings.ViewPricing`, so `Actions`
  is at **8 for them and 7 for everyone else** — a hardcoded `targets: 8` would silently make the Actions
  column sortable for half the users, and `targets: 7` would do it for the other half. The config is
  computed in Razor: `targets: @(Model.CanViewPricing ? 8 : 7)`. Any future permission-gated column needs
  the same treatment; a second DataTables config would be the alternative and is worse.
- **`dotnet ef migrations add --no-build` followed by `database update --no-build` fails.** The `add` step
  writes the migration and the snapshot to disk but the `update` step reads the **compiled** assembly,
  which still has neither — so it dies with `PendingModelChangesWarning: The model for context
  'TravelContext' has pending changes. Add a new migration before updating the database.` The message
  points at the migration you just created, which is the confusing part. **Rebuild between the two
  commands.**
- **`Invoke-WebRequest` in Windows PowerShell 5.1 needs `-UseBasicParsing`** or it dies with *"Windows
  PowerShell is in NonInteractive mode. Read and Prompt functionality is not available"* — it's trying to
  run Internet Explorer's first-launch configuration to parse the DOM. Nothing to do with the app.
- **A PowerShell function `return`ing an `ArrayList` hands back a fixed-size `Object[]`.** The pipeline
  unrolls it, so the first `.Add(...)` on the result throws *"Collection was of a fixed size."* Return
  `,$list` (leading comma) when a verification script needs to keep appending.
- **Money goes into a `.xlsx` as a number with `Style.NumberFormat.Format`, never as a formatted string.**
  A pre-formatted `"USD 1,300.00"` cell can't be summed by whoever receives the file, which defeats the
  point of exporting a total. `Money.ExcelNumberFormat` is the shared format; `Money.Format` is for screen
  text only.
- **Verifying against real data means putting it back.** The Edit round-trip was run against booking
  `MUSB-000012`, which also required filling in its (unrelated, pre-existing) missing nationality before
  the save would go through at all. Both were reverted with SQL afterwards, as was the throwaway user
  created for the permission test. Note `sqlcmd` needs `SET QUOTED_IDENTIFIER ON;` before any `DELETE`
  against the Identity tables — the filtered/computed indexes reject the default OFF with error 1934.

### 2026-08-14 — Traps around Booking Finance (extra nights, tours, transactions, audit)

- **🔴 A validation rule with no `<span asp-validation-for>` is an invisible rule — for the third
  time.** `ValidatePricing` keys its extra-night error on `Stays[i].ExtraNightPrice` and the tour rules
  on `Tours[i].TourId`, but the row markup had no spans, so the POST returned 200 with the form
  re-rendered and **nothing on screen saying why**. Caught only because the verification script printed
  the status and found no rendered error text to match. **Any `ModelState.AddModelError` key that names
  a collection index needs a matching span in the row partial** — `asp-validation-summary="ModelOnly"`
  will never show it, and on the booking form it also silently wipes whatever files were in the
  dropzones. Same failure as 2026-08-02, now in two more places.
- **🔴 The scaffolder's `defaultValue: 0` on `BookingStays.BaselineNights` would have made every
  historical night chargeable.** `ExtraNights` is `Nights − BaselineNights`, so a zero baseline reads
  the *entire* itinerary of all 13 existing bookings as extra — silently, with no error, surfacing only
  as a demand for a rate the next time anyone opened the form. The migration is **hand-edited** with
  `migrationBuilder.Sql("UPDATE [BookingStays] SET [BaselineNights] = [Nights];")` between the
  `AddColumn` and the rest, exactly as `AddEntityCodes` does. **Every new non-nullable column on an
  existing table needs its default questioned, not just enum ones.**
- **🔴 The `Booking/Index` DataTables index is now decided by two independent permissions.** Total is
  gated on `Bookings.ViewPricing`, Payment on `Transactions.View`, so Actions sits at 7, 8 *or* 9
  depending on who is signed in. The old `@(Model.CanViewPricing ? 8 : 7)` was already wrong the moment
  the second column landed. It is now **counted**, not nested-ternary'd:
  `targets: @(7 + (CanViewPricing ? 1 : 0) + (CanViewTransactions ? 1 : 0))`. A third optional column is
  one more term.
- **Preserve what a user can't see, or their ordinary save deletes it.** Edit rebuilds `Stays` and
  `Tours` wholesale, and a user without `Bookings.EditPricing` never renders the price inputs — so
  their post carries none, and the rebuild would wipe every extra-night rate and tour price on the
  booking. `BuildStaysFromInputsAsync`/`BuildTours` take a `preservedPrices` map (keyed by index,
  captured from the stored rows before deletion) that is null only when the user may price. The
  `.Ignore()`-in-the-profile trick works for scalars on the booking; it can't help a collection that is
  deleted and recreated.
- **A get-only computed property is not mapped by EF, but `builder.Ignore(...)` it anyway when it looks
  like a column.** `BookingTour.Total` and `BookingTransaction.SignedAmount` are explicitly ignored,
  matching `Pax`/`LeadGuestName`. (Confirmed by the snapshot: `RequiresDomesticFlight` was never
  ignored and never mapped either — but the explicit form is what the rest of the configs do.)
- **Registering an interceptor that needs a scoped service means the `(sp, options)` overload of
  `AddDbContext`.** `options.AddInterceptors(new BookingAuditInterceptor(...))` can't reach
  `ICurrentUserService`; `AddDbContext<T>((sp, options) => ... sp.GetRequiredService<...>())` can.
- **`PackageCategory` is `FourStars = 4` / `FiveStars = 5`, not 1-based**, and `RoomType` is 1-based.
  A verification script posting `Category=1` gets the raw binder message *"The value '1' is invalid"*,
  which reads like a framework problem and isn't.
- **PowerShell scripts containing em dashes fail to parse** when written to disk with the wrong
  encoding — the error is a cascade of *"Unexpected token"* / *"missing the terminator"* pointing at
  perfectly balanced quotes. Keep verification scripts ASCII-only.
- **A `$var` captured from one parsed form and reused after re-reading the form is stale.** A test that
  did `SetF $form 'Stays[0].Nights' ($firstNights.Value + 2)` twice compounded to +4 and made a correct
  total look wrong. Re-read the value from the new form, not the old object.
- **🔴 A one-level `AddInclude` on a collection whose children you render silently shows "none".**
  `BookingWithDetailsSpec` had `AddInclude("Transactions")` but not `"Transactions.Attachments"`, so
  `Booking/Details` printed `—` in the Receipts column for a payment that **did** have a receipt. No
  error, no empty page — and `/Transaction/Index` showed the same receipt correctly, because
  `TransactionSpec` includes `Attachments`. **Two pages disagreeing about the same row is the symptom
  to look for.** Same class as the `Nationality` include that made every booking render `—`; the rule
  is that an include must reach as deep as the view reads. Reported by the user from a screenshot,
  which is the only way it surfaces.
- **Two things then depend on that include, and only one should.** `BookingController.Delete` loads
  transaction attachments through **their own spec** rather than off `booking.Transactions`, so
  deleting a booking still removes the receipt files if the include is ever trimmed. Don't "simplify"
  it to use the navigation.

### 2026-08-20 — Traps around Money Safes & Block Expenses

- **🔴 A per-entity dictionary breaks the moment that entity can appear twice — and Block Services is
  built on exactly that.** The per-pax allocator keyed its amounts by `BookingId`, which is wrong the
  instant an expense spans two groups that share a booking (a Cairo + Cruise booking is in both, and
  each is a genuine, separately-payable line). It threw *"An item with the same key has already been
  added. Key: 9"* on the first two-group test. It is indexed by **position** now. The loud crash was
  luck: `ToDictionary` throws, but `dict[key] = value` would have silently kept one line and
  under-charged. **Whenever a collection is keyed by a domain id, ask whether that id can repeat in
  the set being keyed.**
- **🔴 A unique index on a nullable column needs a filter, or the second NULL row fails.** SQL Server
  treats NULLs as *equal* in a unique index. `IX_SafeEntries_Expense` (`ExpenseId` null on every
  manual entry) and `IX_BlockExpenseBookings_Group_Booking` (`BookingId` null once a booking is
  deleted) both carry `.HasFilter("[…] IS NOT NULL")`. Without it, **the second manual deposit ever
  created** dies on a unique violation — and with no `_Code` in the index name it doesn't even get the
  friendly `DuplicateCodeException` translation, just a raw `DbUpdateException` 500.
- **Those filtered indexes make `sqlcmd` need `SET QUOTED_IDENTIFIER ON`** for *any* write touching
  those tables — the documented 1934 error, previously only hit on the Identity tables. It now applies
  to the expense tables too.
- **A `NoAction` FK into `Booking` means the delete path must NULL, not delete.**
  `BlockExpenseBooking.BookingId` is nullable and `NoAction` (the 1785 rule), and
  `BookingController.Delete` sets it to null rather than removing the row: the line records money that
  already left a safe, and deleting it would make its group's stored `Amount` stop matching the sum of
  its own visible lines. Verified in a rolled-back transaction — the raw delete is genuinely blocked
  by `FK_BlockExpenseBookings_Bookings_BookingId`, and NULLing is what releases it.
- **Verifying an authorization change requires a NON-SuperAdmin.** SuperAdmin short-circuits the
  handler and carries no permission claims, so it exercises none of the claim path. The packed-claim
  work looked fine under SuperAdmin and proved nothing; it was only real once a plain user with four
  grants was driven through allow *and* deny.
- **A duplicate `Name` on the new catalogues surfaces as a raw 500, not a validation message.** The
  `[Remote]` check is client-side only and there is no server-side uniqueness guard — the DB index
  catches it. This is **pre-existing behaviour shared with `TourController` / `NationalityController`**,
  not something these controllers do differently; noted so the next reader doesn't think it's new. A
  script that re-runs fixtures must create idempotently.
- **PowerShell: `,@(a), ,@(b)` on one line nests wrongly and posts garbage.** A verification script
  built its form body that way and the request arrived without `ExpenseTypeId`, producing a misleading
  *"Choose an expense type"* that looked like a server bug. One entry per line. (`$home` is still
  read-only, and `Get-Content -Raw` misbehaved under one invocation — read files with
  `[System.IO.File]::ReadAllText` when the result is load-bearing.)
- **`Expand-Archive` refuses a `.xlsx`.** Copy it to `.zip` first when inspecting an export. Remember
  ClosedXML writes `<x:row>` with the namespace prefix — searching for `<row` returns zero hits on a
  perfectly good file.

### 2026-08-20 — Traps around the Revenue report

- **🔴 A computed total that reads 0 for a missing include is the quietest bug in this codebase, and
  the Revenue report is its worst case.** `Booking.GrandTotal` sums `Stays` and `Tours` in C#; without
  those includes it returns a smaller number rather than throwing, so the whole report is silently
  under-stated and every margin looks better than it is. `RevenueReportSpec` includes them
  deliberately. **Verify with a booking that has extra nights AND a tour** — the only one in the
  database was `MUSB-000014` (400 + 50 + 200 = 650), and against any other booking a missing include
  is invisible.
- **🔴 A footer must sum the column printed above it, even when that costs a footnote.** The first cut
  excluded unpriced bookings from `TotalExpenses` (their revenue is unknown) while still printing
  their cost on their own row — so the Expenses column didn't add up to its own total. Fixed by
  totalling **every** row and stating plainly that net and margin read *lower than reality* until
  those bookings are priced. Where a total must omit something, say so; never let it silently
  disagree with its column.
- **An expense with no exchange rate must be excluded and COUNTED, never folded in at face value.**
  Treating an EGP amount as USD understates a cost ~48-fold and inflates the margin — the most
  flattering possible direction for a number someone will act on. The count surfaces per row, as a
  banner and in the `.xlsx`.
- **ClosedXML 0.105 has no `IXLCell.Comment`.** `cell.Comment.AddText(...)` does not compile. Use cell
  formatting plus a written note; don't guess at the replacement API.
- **A percentage in Excel is the fraction, not the number.** Write `margin / 100` with format `0.0%`
  — writing `42.1` with a percent format renders *4210%*.
- **Predicting a verification figure without re-reading the data will fail the test, not the code.**
  The first revenue check looked for `74.23` and failed, because a second expense had been created
  against the *same* booking through a different arrival group; the correct answer was `104.23` and
  the report had been right all along. Re-derive expectations from the data as it stands, especially
  once a booking can belong to several groups.
- **Check whose data you are about to delete.** The database had an `accommodation` expense the *user*
  had created while trying the feature. Cleanup matched a fixture prefix rather than "everything in
  the table", and that row survived. Query before a bulk delete on a shared dev database.

### 2026-08-20 — Traps around widening `NeedsAmendment` to In-progress blocks

- **🔴 `dotnet ef migrations add` scaffolds a table rename as `DropTable` + `CreateTable`.** Renaming
  `BlockConfirmedBooking` → `BlockRecordedBooking` produced exactly that, with only *"An operation was
  scaffolded that may result in the loss of data"* as the warning. Applied as generated it would have
  deleted every confirmation record — and since a `Confirmed` block with no record resolves to
  **Needs amendment**, every confirmed block in the system would have flagged on deploy with nothing
  anywhere to say why. The migration is **hand-edited** to `RenameTable`/`RenameColumn`/`RenameIndex`.
  **Always `dotnet ef migrations script <prev> <new>` and read the SQL before `database update`** —
  the scaffolder never infers a rename.
- **`RenameTable` does not rename the PK or the FK constraints.** They keep the old names in the
  database while EF keeps generating the new ones from the model, so a future `DropForeignKey` targets
  a name that doesn't exist. Add `EXEC sp_rename N'FK_old', N'FK_new', N'OBJECT'` for each.
- **🔴 A derived value that starts reading a second stored status must be re-asked what its *other*
  outputs mean.** `BlockSnapshot.Resolve` returns a status **and** `CoveredBookingIds`. Widening the
  status to `InProgress` silently widened the covered ids too — and "covered" means *the supplier
  confirmed this booking*, which an in-progress request never does. `Booking/Details` would have
  counted in-progress blocks in "N of M blocks confirmed". `RecalculateAsync` was safe only by
  accident, because its private `IsCovered` re-checks the stored status itself. When a function
  returns more than one fact, widening one input widens all of them.
- **A "map the derived value back" hack stops working the moment there are two causes.**
  `block-status.js` mapped a derived `4` to `3` when opening the modal. With `InProgress` also
  reaching `4`, that would have offered to mark an unconfirmed block **Confirmed** on one careless
  Save. Fixed by carrying `BlockStatusInfo.StoredStatus` on the button instead of the displayed
  status — the general form being *don't reconstruct a stored value from a derived one, pass it*.
- **Widening a rule needs an explicit answer for the rows that predate it.** Blocks already stored as
  `InProgress` have no record, and "no record" compares as "everything changed" — all of them would
  have flagged at once. `Resolve` returns early on `InProgress && snapshots.Count == 0`. Verified
  against the one real legacy row in the database; a fixture created after the change proves nothing.
- **Razor HTML-encodes apostrophes, so `-match "supplier's confirmation"` fails on correct output.**
  It renders as `supplier&#x27;s`. A verification regex over rendered HTML must avoid `'`, `"`, `&`
  and `<` — the failing assertion looked like a missing feature and wasn't.
- **A group that drops below 2 bookings moves to `/BlockService/SingleArrivals`**, so a check that
  only reads `/BlockService/Index` reports "row missing" when a booking is cancelled out of it. Read
  both lists when a test changes group membership.
- **`dotnet run --launch-profile http` still bound to `:5000`, not the profile's `:5145`.** The
  environment *was* `Development` (confirmed in the log, and the DB timings stayed at 2–13 ms), but
  don't assume the port — read `Now listening on:` from the log before driving the app.

### 2026-10-02 — Traps around per-room-type extra nights

- **🔴 Moving a priced value into a child table silently removes it from the audit trail.** See the
  `DiffStayRates` note under Booking Audit Trail. The general shape: **anything a child-to-grandchild
  move takes out of `entry.Properties` stops being audited, with no error and no failing test.** The
  near-miss on the way in was reusing `CompareByValue` for the new rows, which would have logged a
  junk `StayId: 42 → -2147482647` on **every** save (EF keeps the temporary key in the entry, not on
  the instance) and manufactured a spurious event on the phone-number-only edit the whole design
  protects.
- **🔴 "Preserve what they can't see" has to preserve the ROW, not just the field.** Found while
  testing the above, pre-existing: `BuildTours` carried a user-without-`EditPricing`'s stored tour
  **prices** forward keyed by index — but `_BookingExtraTours` renders *nothing at all* for that user,
  so their post contains no `Tours[...]` fields and the wholesale rebuild **deleted every extra tour
  on the booking**. Preserving the price of a row that was never posted protects nothing. It now
  carries the whole rows (`IReadOnlyList<BookingTour>`), as the stay rates do. **Before writing a
  preservation map, check whether the hidden thing is a field inside a posted row or the row itself.**
  The audit trail is what made the loss recoverable — it had logged `Tour / Sphinx / Removed`.
- **🔴 A one-level include that was right yesterday is silently too small today.** `RevenueReportSpec`
  had `AddInclude("Stays")` with a comment saying it was for `ExtraNightsTotal`; the moment the money
  moved to `Stays.Rates` that include lost every extra-night charge **and** made
  `HasUnpricedExtraNights` read true for every booking, so the whole report would have shown `—`.
  Fourth occurrence of this family (`Nationality`, `Transactions.Attachments`, `GrandTotal`'s
  navigations, now this). **When a value moves one level deeper, grep every `AddInclude` that names
  its old level.**
- **A conditionally-rendered collection must render ALL of its indices or none.** MVC's collection
  binder stops at the first missing index, so disabling or omitting one of the three room-type tiles
  yields an **empty** `Rates` list — the operator's other rates gone, with no error. Hence
  `NormalizeStayRates` in `PopulateFormAsync` and the rule never to `disabled` a subset. `d-none` is
  safe precisely because a hidden-but-enabled input still posts.
- **The reindex regex has no `/g` flag, and that is load-bearing.** `recalc()` does
  `name.replace(/Stays\[\d+\]/, 'Stays[' + i + ']')`, so the nested `Stays[0].Rates[1].Rate` becomes
  `Stays[3].Rates[1].Rate` with the inner index intact. It reads as a bug and isn't — don't "fix" it.
- **A sibling `<tr>` is not reachable from the row.** Hit while the rates lived in a `.stay-detail`
  sibling row: `closest('.stay-row')` is null inside it, and `previousElementSibling` on a stay lands
  on a panel rather than the neighbouring stay, so every traversal and the reindex had to handle the
  **pair**. That shape is gone (see the modal note below) — the lesson stands for the next person
  tempted by a detail row.
- **Pricing inputs must not `markEdited()`.** The schedule's dirty flag drives the destructive
  "Rebuild the schedule?" confirm; letting a rate keystroke set it fires that prompt over an edit that
  touched no dates. It now applies to a **programmatic** write too: the modal assigns the row's hidden
  inputs with a plain `.value` and must never dispatch an `input` event, or it lands in the container
  delegate's catch-all branch and arms the confirm.
- **A per-row Bootstrap Collapse or modal is still wrong; ONE shared modal is right.** `addRow()`
  stamps `__INDEX__` with `rows().length`, so after removing a middle row a new row's id **collides**
  with a surviving one and `data-bs-target` silently cross-wires. The extra-night rates do now use a
  modal — and it is safe only because both of the original objections were defused: it is **one shared
  dialog** populated from the clicked row (no per-row ids at all), and **every validation span stays
  in the row**, so an error is never rendered inside a closed dialog. Copy that shape or neither.
- **🔴 Form inputs must sit inside a `<td>`, never directly under a `<tr>`.** The HTML parser
  foster-parents them out of the table entirely, and they stop posting with no error anywhere.
- **A modal inside a `display:none` ancestor never appears.** `_BookingStayRatesModal` is rendered by
  `Create`/`Edit` after `</form>`, not from `_BookingStayRows` — on Create that partial lives inside
  `<div id="previewWrap" class="d-none">`, and putting the modal there yields a dead button with
  nothing in the console. Outside the form also stops Enter in a number input from submitting.
- **`bootstrap.Modal.getOrCreateInstance` does not exist in this 5.0.0-beta2 bundle** (grepped).
  Use `getInstance`, or create one instance at init — `new bootstrap.Modal(...).show()` per click
  leaks the previous instance's handlers. Nor does beta2 restore focus to the trigger on hide; do it
  in `hidden.bs.modal`, guarded on `isConnected` so a row deleted meanwhile doesn't strand focus.
- **`fw-semibold` does not exist in this Skote build** — not in `bootstrap.min.css`, `app.min.css` or
  `app-extras.css`. It is already used in ~32 views and silently renders at normal weight. Use
  `fw-bold`; don't propagate it. (Pre-existing, not introduced here.)
- **Verification-harness traps, all of which produced convincing false results:**
  - A checked checkbox and its companion hidden post `true,false` and the binder takes the **first**.
    A scripted client that lets the hidden overwrite the box silently **un-ticks every checkbox**, so
    a "no-op" save looks like an audit regression.
  - `Invoke-WebRequest -Body @{ k = @('a','b') }` flattens an array into one comma-joined field. A
    checkbox list binds from **repeated keys** — build the body as a raw urlencoded string. Without
    this the permission grant silently did nothing and every later assertion "passed" against an
    AccessDenied page. **Assert that the privileged page actually loaded before trusting the rest.**
  - Razor HTML-encodes `+` as `&#x2B;` (as it does `'` → `&#x27;`). A regex over rendered HTML that
    contains one fails against perfectly correct output.
  - Razor emits `selected="selected"` on either side of `value=` — a regex requiring one order misses
    the selection and the post then fails on unrelated required fields.
  - `<template>` contents never post in a browser. A scripted client that scrapes the whole document
    will post `Stays[__INDEX__].*` placeholders.
  - **A half-stubbed jQuery is worse than none.** Bootstrap's `defineJQueryPlugin` writes to `$.fn`
    and `EventHandler.trigger` calls `$.Event` — a Proxy whose `.fn` differs per access throws
    *"Cannot set properties of undefined"*, and a stub without `$.Event` throws *"o.Event is not a
    function"*. Either leaves `Modal` half-wired, which looks exactly like "the click handler isn't
    attached". `booking-form.js` uses no jQuery, so the harness simply deletes `window.jQuery` and
    Bootstrap's `getjQuery()` skips both paths.
  - **A silent `VirtualConsole` hides a throw inside an event handler**, turning a real error into an
    apparently inert feature. Forward `jsdomError` and filter only the asset 404s.
  - **Bootstrap's `show()` is asynchronous** — the backdrop first, the dialog via a transition
    callback (a `setTimeout` even at zero duration). Asserting `.show` synchronously after a click
    fails on a modal that works.
  - **Check the live data before predicting a count.** A filter assertion of "exactly 1 booking"
    failed because the *user* had created another booking using the feature between sessions. Second
    occurrence of this family; re-derive expectations from the data as it stands.
  - **Stay ids change on every save** — `Edit` deletes and recreates them. A diagnostic that reuses an
    id captured before a save updates 0 rows and reads like a feature failure.

### 2026-10-03 — Traps around splitting blocks by category

- **🔴 Widening a derived group's key means splitting every row already keyed on the old one.**
  `BlockServiceStatus` and `BlockExpenseGroup` are the persisted halves of a key Block Services
  otherwise derives. Adding `Category` to the key without splitting the data would have left each
  mixed group's status, documents and expenses on the 4★ half only — the 5★ half silently reading
  `Not booked` with no documents, and its money attributed to the wrong grade. `SplitBlocksByCategory`
  carries the data SQL; any future key component needs the same.
- **The scaffolder emitted `defaultValue: 0` for both `Category` columns — the enum-default trap
  again.** 0 is not a `PackageCategory` member (4 / 5). Hand-edited to 4.
- **🔴 A rebuilt fingerprint must sort exactly as the C# does, or every row reads "changed".**
  `ExpenseFingerprint.Of` orders its segments with `StringComparer.Ordinal`. The migration rebuilds
  each half with `STRING_AGG … WITHIN GROUP (ORDER BY value COLLATE Latin1_General_BIN2)`; the
  database's default collation would order `"12:…"`, `"9:…"` differently and flag all 31 groups.
  Verified: 0 of 31 groups flagged after the migration, and every expense total, line sum and safe
  balance was unchanged (816,000.00 before and after).
- **Two rows can now share one physical file.** The split copied each document row onto both halves
  rather than duplicating the file on disk, so `BlockServiceController.DeleteDocument` deletes the
  file **only when no other `BlockAttachment` row points at it**. Don't "simplify" that check away.
- **Git Bash rewrites a leading-slash argument into a Windows path** (`/Expense/Details/6` became
  `C:/Program Files/Git/Expense/…`). Export `MSYS_NO_PATHCONV=1` before passing URL paths to a
  verification script. Python on this machine also needs `sys.stdout.reconfigure(encoding="utf-8")`
  to print `★`, and reading a heredoc through `sys.stdin` defaults to cp1252 — decode the bytes as
  UTF-8 or the star turns into `â˜…` in whatever file you write.

### 2026-10-03 — Traps around private bookings

- **🔴 EF silently filters a unique index over a nullable column.** The SQL Server provider adds
  `WHERE [PrivateBookingId] IS NOT NULL` on its own, which would have stopped the index constraining
  the **shared** rows at all — two shared status rows for one block could then coexist. Here the
  NULL-equal behaviour is the rule (one shared row per group), so both configs say `HasFilter(null)`.
  This is the mirror image of the 2026-08-20 `HasFilter("[…] IS NOT NULL")` gotcha: decide which one
  you want **every** time a nullable column enters a unique index, and read the generated SQL.
- **A route value added by regex hit the wrong anchors.** `asp-route-category="@Model.Category"` (the
  filter cross-links) matched the same pattern as the per-row `@b.Category` links and received an
  `@b.PrivateBookingId` that doesn't exist in that scope. Razor would have failed the build — but grep
  the result of any bulk view edit before trusting it.
- **Older bookings can't be saved through Edit at all** if they predate a required field (MUSB-000003
  has no nationality and no adult price), so a verification that drives the real form must pick a
  complete booking. That is pre-existing behaviour, not something this feature introduced.

### 2026-10-21 — Traps around recording who takes each tour

- **🔴 A navigation read after a sibling delete may already have been touched by the client cascade.**
  Edit deletes the guests *before* it captures the preserved tours, and `BookingTourGuest.Guest` is
  `ClientCascade` — so reading `tour.Guests` afterwards relies on EF not having fixed up the deleted
  links. `KeptTour` snapshots each tour's guest keys **before** the guest delete, and
  `DiffTourGuests` reads the Deleted link *entries*. Capture what you need from an aggregate before
  you start deleting parts of it.
- **Typing into a booking's count boxes from a test harness can append to them.** `page.fill` on
  `Infants` followed by a fill on a guest name left `Infants = 10` and spawned rows on every keystroke
  — reproduced identically against the committed JS, so not an app bug. Set the counts with
  `$eval(e => { e.value = …; dispatch input + change })`.
- **A Create test on the `Cairo` package needs a departure date.** It has no fixed total, the field
  defaults to `0001-01-01`, `Preview` returns nothing, and the save fails with *"Departure must be
  after arrival"* — read as a tours bug, it isn't one.
- **Playwright without the package:** the browsers are already under `ms-playwright/chromium-1234`;
  `npm i playwright-core` in the scratchpad plus `executablePath` drives them.

### 2026-10-21 — Traps around applying expenses to bookings

- **A validation span inside an `@if (!IsEdit)` branch is invisible on Edit.** `SelectedGroupKeys`'s
  span only rendered on Create, so a group that emptied before an Edit save failed with nothing on
  screen. Spans now sit outside the branch. Check every span against **every** render mode of a form.
- **A filter checkbox inside the expense form is a plain `name="IncludeCancelled" value="true"`
  input, not `asp-for`.** The whole form is `serialize()`d into the Preview post, and `asp-for`'s
  companion hidden `false` would post the flag twice.
- **Seed data names can carry trailing spaces** (`"Cairo "`). A verification regex anchored on
  `>Cairo</option>` found nothing and read like a missing filter. Match with `\s*`.
- **`PRINT` takes no subquery in T-SQL** (Msg 1046) — assign to a variable first when a rolled-back
  verification script needs to report a count.

### 2026-10-21 — Traps around the UI re-skin

- **🔴 A same-specificity override silently undoes a vendor modifier.** The token rule
  `.table > :not(caption) > * > * { padding: var(--mt-cell-y) … }` has exactly the specificity of
  Bootstrap's `.table-sm > :not(caption) > * > *` and comes later, so every `table-sm` quietly grew.
  It is restored right after. When overriding a base component, grep for its size/variant modifiers.
- **🔴 Dark tokens leak onto paper.** Printing from dark mode would have used light-grey text, because
  `@media print` doesn't change `data-theme`. The print block redefines the tokens for
  `:root, :root[data-theme="dark"]`. And a solid fill (the 5★ tag) prints **white on white** — browsers
  drop backgrounds by default — so print tags get a real `border`.
- **Vendor CSS linked in the view body wins on position.** DataTables/Dropzone/timepicker `<link>`s sit
  inside `RenderBody()`, after `app-extras.css`; an equal-specificity rule there beats ours.
- **`bx-eraser` doesn't exist** — the Clear buttons on `MoneySafe/Details` and `Expense/Index` had been
  empty squares since they were written (now `bxs-eraser`). A visual sweep of every page found it;
  nothing else would have. Same family as the documented missing-class/missing-icon trap.
- **Two views hardcoded the category badge class** (`badge bg-warning-subtle text-warning` on
  Booking Index/Details) instead of `CategoryBadges.Css`, so 4★ and 5★ looked identical there and
  changing the helper didn't reach them. Grep for the literal before trusting a helper owns a look.
- **Toastr's icons are white PNGs baked into the toast background** (with `!important`), so a light
  toast needs replacement icons — `--mt-toast-icon-*` SVG tokens.
- **bootstrap-dark paints some components a literal navy** (`.list-group-item` `#2a3042`) that no token
  reaches; they surface only on a dark-mode sweep.
- **Verification-harness traps:** Playwright's `evaluate` awaits a returned promise, so
  `Swal.fire(...)` hangs it until the dialog closes — wrap in `setTimeout`. `page.setContent` runs on
  `about:blank`, which can't load a `file://` image — write a temp HTML file and `goto` it. Python
  reading a heredoc from stdin decodes it as cp1252, so an `assert old in s` with an em dash fails on
  a correct file — use the Edit tool for non-ASCII replacements.
- **A background `dotnet run` holds port 5145.** The user's Visual Studio F5 then died with
  `IOException: Failed to bind to address http://127.0.0.1:5145: address already in use`. Stop any
  instance you started before handing back, and ask before starting one.

### Reusable guardrails (from the same architecture — apply as the domain is built)
- **SQL Server cascade cycles (error 1785):** don't set every FK to `Cascade`. Use `Cascade` only for ownership FKs, `NoAction` for reference FKs to break multiple cascade paths to the same table.
- **EF enum default value:** don't `HasDefaultValue(1)` (int) on an enum property — type mismatch at design time. Initialize the default in the C# model instead and omit the DB default.
- **Upload directory:** create the target folder before opening a `FileStream` (`Directory.CreateDirectory` is a safe no-op if it exists). `FileStorageService` already does this — keep it for any new upload folder.
- **Submit button "Saving…" stuck:** gate the disable-button/spinner on validity — `if (!$(form).valid()) return;` *before* disabling — so a client-side validation failure doesn't permanently disable the button.
- **CRLF vs LF edits:** if a targeted string replace silently fails to match, verify with a fresh read and fall back to overwriting the whole file.
