# Booking form (Create / Edit) — UI/UX recommendations

> A review of the booking form from a user-experience and project-management angle.
>
> **Status: implemented on `feature/booking-form-ux` (2026-11-19),** with these decisions:
>
> - **Declined:** the "Paste names" button, and every Rooms change (the room suggestion and the
>   capacity warning). The Rooms card is unchanged.
> - **Reorder:** ↑ ↓ buttons only, with no drag handle.
> - **Layout:** Booking & package and the Schedule each take a full-width row. Contact sits above
>   Rooms, beside Guests.
> - **Errors:** a red panel at the top lists every error and is clickable, and the Save bar shows the
>   error count. Server-side errors land in the same panel.
>
> How it maps onto the code is in `CLAUDE.md` under "The booking form's layout (Create / Edit)".
> The sections below are the original review, kept as written.

---

## Summary

The Schedule table has up to **10 columns**, and their fixed and minimum widths add up to roughly
**1,150–1,250 px**. It sits inside a `col-lg-7` card that is only about **600–650 px** wide on a
normal 1366 px laptop. So the operator scrolls left and right on every booking, and while scrolling
loses track of which stay they are editing.

---

## 1. The Schedule: what to fix

**Biggest win: replace the table with a stack of stay cards, two or three lines each:**

```
┌──────────────────────────────────────────────────────────────────────┐
│ ⋮⋮ 1  [Cairo ▾]   Mon 03 Aug → Wed 05 Aug   [−] 2 [+] nights          │
│        ✈ Domestic flight  ·  +1 extra · USD 120 ✎          ↑ ↓ 🗑      │
│        Hotel [optional……………]   Notes [optional……………………………]           │
└──────────────────────────────────────────────────────────────────────┘
```

- **Show check-in and check-out as plain text, not a date input.** The field is `readonly` today but
  still looks editable, which confuses the operator, and it takes 140 px. Show "03 Aug → 05 Aug" and
  keep the hidden input for the post.
- **Merge the Extra and Extra nights cost columns into one chip:** `+1 · USD 120 ✎`. Clicking it opens
  the existing rates modal, and it disappears when there are no extra nights.
- **Make Dom. flight a small toggle chip (✈) on the card** instead of a whole column.
- **Give Nights a − / + stepper,** because the operator usually adds or removes a single night.
- **Move Hotel and Notes to a second line.** They are optional, and they are what makes the table too wide.
- **Add a summary line under the list:** `3 stays · 7 nights · 03 Aug → 10 Aug`. It is the sum the
  operator currently does in their head.

**On the code side:** with `<div>` rows instead of `<tr>`, the trap where an input placed directly
under a `<tr>` gets pushed out of the table goes away. Name reindexing, the shared rates modal and the
in-row validation spans all keep working unchanged.

**If you want an easier first step:** give the Schedule its own full-width row under the
Booking & package card, and apply only the check-in-as-text and merged Extra changes. That removes
most of the scrolling in about a day, but some will remain on small laptops.

**Files:** `Views/Booking/_BookingStayRows.cshtml`, `Create.cshtml` / `Edit.cshtml`,
`wwwroot/back/assets/js/booking-form.js`, section 15 of `wwwroot/css/app-extras.css`.

---

## 2. The rest of the form

### Page layout and flow

- **A sticky bar at the bottom of the screen** showing PAX, the grand total, the minimum first
  payment and the **Save** button. Save currently sits at the very end of the page, after
  Attachments; the page is very long and the total is easy to lose sight of.
- **Section navigation that stays visible while scrolling:**
  Package · Schedule · Guests · Flights · Payment · Files. This is better than a multi-step wizard:
  operators edit bookings far more often than they create them, and a wizard hides how the schedule,
  the guests and the price affect each other.
- **Move the Private and Visa switches** below the dates, or into a small "Options" row. They
  currently sit between Category and the dates, which breaks the natural flow
  Package → dates → Schedule.
- **Move the Contact card next to Guests,** because the email, phone and nationality belong to the
  lead guest, not to the flights.

### Flights

Lay each flight out in real-world order, **Date → Flight no → Time**, on one row, with Arrival and
Departure side by side. Today the date sits alone on a full-width line below the number and time.

### Guests and rooms

- **A "Paste names" button:** the operator pastes a list of names from WhatsApp or a passport sheet
  and the rows fill themselves. This is probably the single biggest time saver here.
- **− / + steppers for the Adults, Children and Infants counts.**
- **Suggest rooms from the pax count** (e.g. 4 adults → 2 DBL), and warn when the room capacity
  doesn't fit the PAX.

### Errors

Errors currently appear as one toast after another ("Enter a name for guest 3"). Better: **mark every
invalid field at once, scroll to the first one, and show a short clickable list of all the errors.**
