# Feature spec — Internal Movements (التحركات الداخلية)

> **For Claude Code:** this is an agreed design, drawn out with the user one question at a time.
> The decisions below are **settled — don't re-ask them.** Read `CLAUDE.md` first (architecture,
> gotchas, UI rules) and follow it everywhere this spec is silent. Ask the user only about something
> this spec genuinely doesn't cover. Work on a `feature/internal-movements` branch cut from `develop`.

---

## 1. Goal

Operators dispatch drivers for guests **inside one destination** — usually Cairo: airport pickups,
airport drop-offs, day tours like "Pyramids + Museum". They need:

1. A **day board** per destination: every booking in Cairo on a given day, plus the bookings that are
   elsewhere that day (greyed out, so it's obvious they aren't in Cairo).
2. A way to **assign a movement** to a booking, by drag and drop, with a pickup time and a driver.
3. A **report** of those movements (lead guest, pax, flight number + time when it's a flight, pickup
   time, driver, hotel) that can be printed or exported and handed to drivers.

Example movement types (user data, entered in Setup): `يومية هرم + متحف`, `وصول خارجي`, `سفر خارجي`,
`وصول داخلي`, `سفر داخلي`.

---

## 2. Settled decisions (the user's calls)

| # | Decision | Rejected alternative |
|---|---|---|
| 1 | **All movements are dragged by hand.** The system never auto-creates or proposes movements. Each booking card only shows a passive **hint** like `✈ Arriving today 23:40` / `✈ Domestic departure today 06:15` | System-proposed (ghost) flight movements to confirm |
| 2 | **Flight number and time are read live from the booking data**, never stored on the movement. If the booking's flight changes, the board and the report follow it. If no flight is recorded, show a warning with a link to the booking | Pre-filled into the movement and editable (goes stale) |
| 3 | A booking is **in the destination on day D** if it has a stay there covering D, **or** its arrival/departure flight into/out of that destination is on D, even when that's outside the stay (e.g. lands 23:40 the night before the package starts) | Stay days only |
| 4 | **Multi-select:** tick several booking cards and drag once. **One popup**, one pickup time, one driver, but **one `InternalMovement` row per booking**, so each can be edited or deleted on its own later | One by one only |
| 5 | **Pax per movement:** defaults to the booking's pax, **editable** (only 2 of 4 guests may go on the day tour) | Always the booking's pax |
| 6 | Report columns: what the user asked for **plus the hotel** (pickup/drop-off location). No phone numbers, no notes field | Lead phone, driver phone, notes |
| 7 | Report covers a **from–to range**, rows **ordered by day, then pickup time**; filters destination + driver (optional). One day + one driver = tomorrow's driver sheet | One day, grouped by driver |
| 8 | When a booking changes after a movement was recorded (moved off that day, cancelled, flight moved), the movement **stays and is flagged "Needs review"** on the board, the calendar and the report. The operator deletes or fixes it by hand | Auto-delete |

The movement type form asks **"Is it a flight?"**. If yes, it asks **International or Domestic** and
then **Arrival or Departure**. The user's original flow only asked the first two, but `وصول خارجي` and
`سفر خارجي` are both international and read different flights, so the direction is required. A
non-flight type (`يومية هرم + متحف`) asks neither.

---

## 3. Data model

All entities inherit `BaseModel`. Configs in `Travel.Repository/Data/Configrations/`.

### `Destination` — one new column
- `bool HasInternalMovements` (default `false`). Checkbox on the Destination form (`_DestinationForm`),
  label "Has internal movements". The migration default `false` is correct, but read the migration anyway.

### `MovementType` (new) — Setup catalogue
| Field | Notes |
|---|---|
| `string Name` | required, max 100, unique → index `IX_MovementTypes_Name` (**no `_Code` in the name**, see the `IsCodeUniqueViolation` gotcha) |
| `FlightLeg? FlightLeg` | `null` = not a flight |
| `bool IsActive` | default `true`. Inactive types are hidden from the board's palette but still render on existing movements |

New enum `Travel.Domain/Enums/FlightLeg.cs`: `InternationalArrival = 1`, `InternationalDeparture = 2`,
`DomesticArrival = 3`, `DomesticDeparture = 4`. The form's three questions compose it. Store one enum,
not three columns, because the flight resolver (§5) is one `switch` on it.

- **`FlightLeg` is locked once any movement uses the type.** Edit re-reads it from the stored row
  (the `MoneySafe.Currency` pattern). Changing it would silently re-point every historical movement at
  a different flight. The name stays editable.
- `Delete` refuses a type in use (readable message, point at `IsActive = false`), like `TourController`.

### `Driver` (new) — Setup catalogue
| Field | Notes |
|---|---|
| `string Name` | required, max 100, unique → `IX_Drivers_Name` |
| `bool IsActive` | default `true`. Only active drivers are offered in the popup |

`Delete` refuses a driver used by any movement.

### `InternalMovement` (new)
| Field | Notes |
|---|---|
| `int BookingId` → `Booking` | **Cascade.** Deleting a booking removes its movements. This is the only path from `Bookings`, so no 1785; still verify the migration |
| `int DestinationId` → `Destination` | Restrict |
| `DateOnly Date` | the board day it was assigned on |
| `int MovementTypeId` → `MovementType` | Restrict |
| `int DriverId` → `Driver` | Restrict |
| `TimeOnly PickupTime` | required (معاد الخروج) |
| `int Pax` | required, ≥ 1 |
| `DateTime CreatedAt`, `string? CreatedByName`, `DateTime? UpdatedAt`, `string? UpdatedByName` | name snapshots via `ICurrentUserService`, like `BlockFlight` |

Index on `(DestinationId, Date)`, **not unique**: a booking can have several movements in one day.
**Nothing about the flight, the hotel or the "needs review" state is stored.** All of it is derived
at read time (§5–§6).

Not part of the booking audit trail (not asked for). Log lines only (§10).

---

## 4. Screens

### 4.1 `InternalMovement/Index` — pick a destination
A card grid (the `MoneySafe/Index` style) of destinations with `HasInternalMovements = true`, each
linking to its calendar. One flagged destination is the common case. If there's exactly one, it's fine
to redirect straight to its calendar. If none is flagged, show an empty state pointing at Setup ▸
Destinations.

### 4.2 `InternalMovement/Calendar?destinationId=&month=yyyy-MM` — pick a day
A **server-rendered month grid** (prev/next month links, today highlighted). Each day cell shows:
- the number of bookings in the destination that day (rule in §5.1),
- the number of movements recorded,
- a red count of movements that **need review** (§6), only when > 0.

Clicking a day opens the day board. `tui-calendar` ships under `wwwroot/back/assets/libs`, but a
plain grid with counts is simpler and is the preferred approach. One query for the month's movements
plus one for the bookings overlapping the month: never one query per day.

### 4.3 `InternalMovement/Day?destinationId=&date=yyyy-MM-dd` — the board
Layout: a **palette** of active movement types (draggable chips) beside a list of booking cards.

Booking cards, each booking **once**, in sections:
1. **Needs review** (only when any exist): movements whose booking no longer qualifies (§6), with
   **Delete** and **Open booking**.
2. **Arriving today**: a stay here starts on D, or an arrival flight into here is on D.
3. **Departing today**: a stay here ends on D, or a departure flight out of here is on D.
4. **Staying**: everything else that is in the destination on D.
5. **Elsewhere** (greyed out, **not droppable**): bookings whose trip overlaps D but aren't here,
   each labelled with where they are (the destination of the stay covering D, e.g. `Cruise`,
   `Hurghada`).

A booking that both arrives and departs on D goes under Arriving and carries both hints.

Each droppable card shows: code (links to `Booking/Details`, `target="_blank"`), lead guest, pax,
category badge (`CategoryBadges`), the hotel, the passive flight hint(s), a selection checkbox, and
its recorded movements as chips (`type · pickup time · driver · pax`). Clicking a chip opens the
same popup in edit mode, with a delete button.

**Assigning:**
- Drag a type chip from the palette onto a card. If that card is ticked, the drop applies to **every
  ticked card**. Otherwise it applies to that card alone.
- **Touch / keyboard fallback:** a `+ Movement` button on each card (and one acting on the ticked
  cards) opens the same popup with a type `<select>`. Drag and drop is unusable on a phone.
- Use native HTML5 drag-and-drop. `dragula` ships under `wwwroot/back/assets/libs`, but it's built
  for sorting between containers, not drop-onto-target. Either is acceptable if it works with the
  fallback.

**The popup** (one shared modal, `_MovementModal.cshtml`, rendered **outside** any form. Bootstrap
5.0.0-beta2: `new bootstrap.Modal(el)` **once** at init, no `getOrCreateInstance`):
| Field | Behaviour |
|---|---|
| Type | read-only when it came from a drop, a `<select>` from the `+` button |
| Bookings | the selected booking(s), each with its own **Pax** box defaulting to that booking's pax (decision 5) |
| Flight | read-only, from §5.2: `MS 986 · 23:40`. Shown only for flight types. If nothing is recorded, a warning: `No flight recorded on this booking — open booking`. Doesn't block the save |
| Pickup time | required. **Prefilled with the flight time for arrival types** (the driver waits at landing), empty otherwise |
| Driver | required, active drivers only |

Save posts JSON (antiforgery header) and creates one row per booking in a **single `SaveChanges`**,
then re-renders the board (reload or partial). Validation is server-side too: pax ≥ 1, time and driver
present, type active, and **every booking must currently qualify for that destination + day** (§5.1).
A stale page or a tampered post gets a readable message, like `ValidateFlight` on Block Services.
Errors show **inside the open modal**, which stays open, never as an invisible ModelState error.

---

## 5. Derivation rules (one service owns them)

Put these in **one service** shared by the board, the calendar and the report, so they can't drift:
`IInternalMovementQuery` in `Travel.Domain/Services/InternalMovements/` (+ DTOs), implemented in
`Travel.Services/InternalMovements/`. The `BlockServiceQueryService` pattern: narrow by spec, then
match in memory, since `Pax` / `LeadGuestName` / composite keys aren't translatable. Decide split vs
single query per spec (`CLAUDE.md` → Query splitting); a booking spec with `Stays` + `Guests` + more
collections should split.

Cancelled bookings never appear on the board (sections 2–5). Their movements show under **Needs review**.

### 5.1 Is booking B in destination X on day D?
Let a stay's check-out be `CheckInDate.AddDays(Nights)`. B qualifies when **any** of these holds:
1. B has a stay at X with `CheckInDate <= D <= checkout` (both ends inclusive: arrival and departure
   days are movement days).
2. **International arrival:** `(FlightArrivalDate ?? ArrivalDate) == D` and B's **first** stay (by
   `SortOrder`) is at X.
3. **International departure:** `(FlightDepartureDate ?? DepartureDate) == D` and B's **last** stay is at X.
4. **Domestic arrival:** a `BlockFlight` with `FlightDate == D` that ticks B (`BlockFlightBooking`)
   on the arrival group of B's stay at X (`BlockKey.Of(booking, stay)`, which includes the category
   and `PrivateBookingId`).
5. **Domestic departure:** a `BlockFlight` with `FlightDate == D` that ticks B on the group of the
   stay **immediately after** a stay at X.

"Trip overlaps D" (for the Elsewhere section) uses the coalesced flight dates:
`(FlightArrivalDate ?? ArrivalDate) <= D <= (FlightDepartureDate ?? DepartureDate)`.

### 5.2 Flight for a movement (never stored)
| `FlightLeg` | Number · time · date |
|---|---|
| `InternationalArrival` | `ArrivalFlightNo` · `ArrivalTime` · `FlightArrivalDate ?? ArrivalDate` |
| `InternationalDeparture` | `DepartureFlightNo` · `DepartureTime` · `FlightDepartureDate ?? DepartureDate` |
| `DomesticArrival` | the `BlockFlight` ticking B on the group of B's stay at X (`FlightNumber` · `FlightTime` · `FlightDate`) |
| `DomesticDeparture` | the `BlockFlight` ticking B on the group of the stay after B's stay at X |

If several domestic flights match, take the one whose `FlightDate` is nearest D. If no number and no
time is recorded, it's **"no flight recorded"**: a warning, not an error.

### 5.3 Hotel for a booking on day D at X
`HotelName` of B's stay at X that covers D. If none covers D (the night-before arrival), take the
nearest **next** stay at X, then the nearest previous one. Blank → `—`.

---

## 6. "Needs review" (derived, never stored)

A movement needs review when any of these holds, with the reason shown in words:
| Reason | Text |
|---|---|
| Booking is `Cancelled` | `Booking cancelled` |
| Booking no longer qualifies for X on D (§5.1) | `Booking is no longer in {Destination} on this day` |
| Flight type, a flight resolves, and its date ≠ D | `Flight is now on {dd MMM}` |
| `Pax` > the booking's current pax | `Booking now has {n} pax` |

"No flight recorded" is **not** a review reason. It's an amber warning on the chip and the report row.
Review movements are counted on the calendar, listed first on the board, and kept in the report with
a visible flag column (screen **and** `.xlsx`). Never hide them: a filtered-out row can't complain
(see the `MinimumBookings` gotcha in `CLAUDE.md`).

---

## 7. Report — `Report/Movements`

In `ReportController`, like Flights/Revenue/Tours: `Movements(from, to, destinationId, driverId, applied)`
+ `ExportMovements(same args)`. Uses the `applied` idiom, so `from` defaults to today only when
`applied == false`.

- **Rows:** one per movement in the range, ordered by **Date, then PickupTime**, with a day header
  row per date (grouped, so **no DataTables**, like the Tours report).
- **Columns:** Date · Pickup time · Booking code · Lead guest · Pax (the movement's) · Movement ·
  Hotel · Flight no · Flight time · Driver · Status (`Needs review: <reason>` / `No flight recorded` / blank).
  Flight columns are `—` for non-flight types.
- **One shape** (`MovementReportRow` in `MovementReportViewModel.cs`) built once by a private builder,
  so the table and the sheet render the same rows.
- **Excel:** `MovementReportExcelExporter` in `Travel.Presentation/Services/`, using `ExcelFormat`
  mechanics. Frozen header, day header rows, filename `Movements_{Destination}_{from}_{to}.xlsx`.
- **Print:** `.print-sheet` on the card, `d-print-none` on the buttons and filters.
- No prices anywhere.

---

## 8. Permissions

Append new modules **at the end** of `PermissionCatalog.Modules` (Order 14, 15, 16) so nothing is
renumbered. Add each module's icon to `Helpers/PermissionIcons.cs`. Add the report pair inside the
existing **Reports** module.

| Module | Permissions |
|---|---|
| `InternalMovements` (14) | `View` (calendar, board, read-only), `Manage` (create / edit / delete movements) |
| `MovementTypes` (15) | `View`, `Create`, `Edit`, `Delete` |
| `Drivers` (16) | `View`, `Create`, `Edit`, `Delete` |
| `Reports` (existing) | `ViewMovements`, `ExportMovements` |

69 → **81**. `IsNameAvailable` remote checks are gated on **View**, not Create (`CLAUDE.md` gotcha).
On the board, resolve `Manage` once via `IAuthorizationService`: without it the palette, checkboxes,
`+` buttons and chip edit are hidden, but the board still renders.

---

## 9. Sidebar (`_Sidebar.cshtml`)

| Title | New item | Icon (verified present in `icons.min.css`) |
|---|---|---|
| Operations | Internal Movements | `bxs-car` |
| Setup | Movement Types | `bx-transfer` |
| Setup | Drivers | `bx-id-card` |
| Reports | Movements | `bxs-bus` |

Add each item's View permission to **its title's `mode="any"` list**. Use `ActiveItem`, matched on the
controller (and on the action for the report).

---

## 10. Logging

`ILogger<T>` per `CLAUDE.md` → Logging: Information after a successful save, past tense, ids and codes
only, **no guest names**. For example `Movement #12 (Arrival) recorded for MUSB-000021 in Cairo on
2026-08-06, driver #3`, `3 movements recorded …`, `Movement #12 deleted`. Warning on every refusal
(stale booking, type in use, …).

---

## 11. Files (expected)

- **Domain:** `Models/MovementType.cs`, `Models/Driver.cs`, `Models/InternalMovement.cs`,
  `Destination.HasInternalMovements`, `Enums/FlightLeg.cs`, `Services/InternalMovements/` (interface + DTOs),
  `Specifications/InternalMovements/`, permissions in `Authorization/`.
- **Repository:** `MovementTypeConfig.cs`, `DriverConfig.cs`, `InternalMovementConfig.cs`, `DbSet`s,
  migration `AddInternalMovements`.
- **Services:** `Travel.Services/InternalMovements/InternalMovementQueryService.cs`.
- **Presentation:** `InternalMovementController`, `MovementTypeController`, `DriverController`
  (the `TourController` CRUD shape), report actions in `ReportController`, ViewModels + AutoMapper
  profiles, `Views/{InternalMovement,MovementType,Driver}/`, `Views/Report/Movements.cshtml`,
  `MovementReportExcelExporter.cs`, `wwwroot/back/assets/js/internal-movements.js`, any styles in
  `wwwroot/css/app-extras.css` (**tokens only**, light + dark).

---

## 12. Traps from `CLAUDE.md` that apply here

- **Read the generated migration SQL** (`dotnet ef migrations script`) before `database update`. Watch
  for the enum-default trap. `FlightLeg` is nullable, so it has no default, but check the others.
- Run and migrate with `ASPNETCORE_ENVIRONMENT=Development` explicitly. Never `--no-launch-profile`.
- Every `ModelState` / validation error needs a visible surface. In the modal, show it in the modal.
- Bootstrap **5.0.0-beta2**: no `fw-semibold`, no `bg-*-subtle` unless defined in `app-extras.css`,
  no tooltips. Grep a class or icon before using it.
- The permission `<permission>` tag helper is C#: rebuild after touching it.
- Check **375 px** width: no sideways scroll. The board must be usable on a phone through the `+` button.
- Don't leave a `dotnet run` holding port 5145 when you hand back.

---

## 13. Verification (do this before calling it done)

Use real local data, and put any data you change back afterwards.
1. A booking whose international flight lands the **day before** its first Cairo stay appears on that
   day under **Arriving**, with the hint and the hotel of its upcoming stay.
2. A Cairo + Cruise booking shows under **Elsewhere — Cruise** on a cruise day, and can't be dropped on.
3. Multi-select 3 bookings, drag one type: 3 rows created, same time and driver, per-booking pax respected.
4. Flight resolution for all four `FlightLeg`s, including a **domestic departure** read from the next
   group's `BlockFlight`. A booking with no flight shows the "no flight recorded" warning.
5. Change a booking's flight number: the board and the report show the new one with no other action.
6. Move a booking's dates / cancel it / lower its pax: its movement lands under **Needs review** with
   the right reason, on the board, the calendar count and the report (screen + `.xlsx`).
7. A type in use: `FlightLeg` can't be changed and Delete refuses. A driver in use can't be deleted.
8. Permissions with a **non-SuperAdmin** user: View-only sees the board with no palette or edit;
   no View → AccessDenied. The sidebar titles hide correctly.
9. Excel opens with a frozen header, day rows, and the Status column filled where expected.

## 14. When done

Add an **Internal Movements** section to `CLAUDE.md` (decisions + why, like the other features), list
the new files in the File Structure Reference, update the permission count (81), and add any new
trap you hit to *Gotchas & Past Mistakes*.
