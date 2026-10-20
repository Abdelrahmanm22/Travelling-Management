using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services;

namespace Travel.Repository.Data.Auditing
{
    /// <summary>
    /// Writes a booking's audit trail automatically from the ChangeTracker, so a field added next
    /// month is covered without anyone remembering to log it.
    ///
    /// <para>
    /// 🔴 <b>The wholesale delete-and-recreate is the whole difficulty.</b> BookingController.Edit
    /// deletes every BookingStay/BookingGuest/BookingRoom/BookingTour and adds fresh ones on
    /// <em>every</em> save. A naive dump of the ChangeTracker would report "3 stays removed, 3 stays
    /// added" when the operator only fixed a phone number — noise that makes the trail useless within
    /// a week. <see cref="DiffChildren"/> therefore pairs Deleted rows with Added ones of the same
    /// type and SortOrder, compares them by value, and emits nothing when they match. Unpaired
    /// leftovers are genuine additions or removals.
    /// </para>
    /// </summary>
    public class BookingAuditInterceptor : SaveChangesInterceptor
    {
        private readonly ICurrentUserService _currentUser;

        public BookingAuditInterceptor(ICurrentUserService currentUser) => _currentUser = currentUser;

        // Never audited: surrogate keys, parent keys, and values that change on every save anyway.
        private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal)
        {
            "Id", "BookingId", "EventId", "TransactionId", "CreatedAt", "UploadedAt", "SortOrder",
        };

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is TravelContext context)
                Audit(context);

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is TravelContext context)
                Audit(context);

            return base.SavingChanges(eventData, result);
        }

        private void Audit(TravelContext context)
        {
            // Snapshot first: adding the audit rows below mutates the ChangeTracker we're reading.
            var entries = context.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(e => e.Entity is not BookingAuditEvent and not BookingAuditChange)
                .ToList();

            if (entries.Count == 0)
                return;

            var byBooking = new Dictionary<int, List<BookingAuditChange>>();
            var created = new HashSet<int>();

            // ── The booking row itself ──────────────────────────────────────
            foreach (var entry in entries.Where(e => e.Entity is Booking))
            {
                var booking = (Booking)entry.Entity;

                if (entry.State == EntityState.Added)
                {
                    // Its key isn't assigned until SaveChanges returns, so a new booking's changes
                    // can't be keyed by id here — Created is recorded by the controller instead.
                    continue;
                }

                if (entry.State == EntityState.Deleted)
                    continue;   // the cascade takes the trail with it; nothing to write

                var changes = DiffProperties(entry, "Booking", null);
                if (changes.Count > 0)
                    Bucket(byBooking, booking.Id).AddRange(changes);
            }

            // ── Children, paired so a rebuild that changed nothing says nothing ──
            DiffChildren<BookingStay>(entries, byBooking,
                "Stay", s => s.BookingId, s => s.SortOrder, s => s.Destination?.Name ?? $"Stay {s.SortOrder + 1}");
            DiffChildren<BookingGuest>(entries, byBooking,
                "Guest", g => g.BookingId, g => g.SortOrder, g => g.FullName);
            DiffChildren<BookingRoom>(entries, byBooking,
                "Room", r => r.BookingId, r => r.SortOrder, r => r.Type.ToString());
            DiffChildren<BookingTour>(entries, byBooking,
                "Tour", t => t.BookingId, t => t.SortOrder, t => t.Tour?.Name ?? $"Tour {t.SortOrder + 1}");

            // Extra-night rates are a grandchild, so DiffChildren can't reach them — see the method.
            DiffStayRates(entries, byBooking);
            // So is who is on each tour.
            DiffTourGuests(entries, byBooking);

            // Transactions are never rebuilt wholesale — each is its own deliberate action — so they
            // are diffed straight, without pairing.
            foreach (var entry in entries.Where(e => e.Entity is BookingTransaction))
            {
                var tx = (BookingTransaction)entry.Entity;
                var label = $"{tx.Type} {tx.Amount:0.00} on {tx.PaidOn:dd MMM yyyy}";

                var changes = entry.State switch
                {
                    EntityState.Added => Single("Transaction", label, "Recorded", null, label, AuditChangeKind.Added),
                    EntityState.Deleted => Single("Transaction", label, "Deleted", label, null, AuditChangeKind.Removed),
                    _ => DiffProperties(entry, "Transaction", label),
                };

                if (changes.Count > 0)
                    Bucket(byBooking, tx.BookingId).AddRange(changes);
            }

            foreach (var (bookingId, changes) in byBooking)
            {
                if (changes.Count == 0 || bookingId == 0)
                    continue;

                context.Set<BookingAuditEvent>().Add(new BookingAuditEvent
                {
                    BookingId = bookingId,
                    OccurredAt = DateTime.UtcNow,
                    UserId = _currentUser.UserId,
                    UserName = _currentUser.UserName,
                    Action = created.Contains(bookingId) ? AuditAction.Created : AuditAction.Updated,
                    Summary = $"Booking updated ({changes.Count} change{(changes.Count == 1 ? "" : "s")})",
                    Changes = changes,
                });
            }
        }

        /// <summary>
        /// Pairs Deleted with Added rows of the same parent and SortOrder and diffs them by value.
        /// A pair that matches produces nothing at all — which is what keeps an unrelated edit from
        /// re-listing the whole itinerary. See the class remarks.
        /// </summary>
        private static void DiffChildren<T>(
            List<EntityEntry> entries,
            Dictionary<int, List<BookingAuditChange>> byBooking,
            string entityType,
            Func<T, int> bookingIdOf,
            Func<T, int> keyOf,
            Func<T, string> labelOf) where T : class
        {
            var relevant = entries.Where(e => e.Entity is T).ToList();
            if (relevant.Count == 0)
                return;

            var removed = relevant.Where(e => e.State == EntityState.Deleted).ToList();
            var added = relevant.Where(e => e.State == EntityState.Added).ToList();

            foreach (var entry in relevant.Where(e => e.State == EntityState.Modified))
            {
                var entity = (T)entry.Entity;
                var changes = DiffProperties(entry, entityType, labelOf(entity));
                if (changes.Count > 0)
                    Bucket(byBooking, bookingIdOf(entity)).AddRange(changes);
            }

            foreach (var oldEntry in removed)
            {
                var oldEntity = (T)oldEntry.Entity;
                var bookingId = bookingIdOf(oldEntity);
                var key = keyOf(oldEntity);

                var match = added.FirstOrDefault(a =>
                    keyOf((T)a.Entity) == key && bookingIdOf((T)a.Entity) == bookingId);

                if (match is null)
                {
                    Bucket(byBooking, bookingId).Add(Change(
                        entityType, labelOf(oldEntity), "—", labelOf(oldEntity), null, AuditChangeKind.Removed));
                    continue;
                }

                added.Remove(match);

                // Same slot, same values → the rebuild was a no-op for this row. Say nothing.
                foreach (var (field, before, after) in CompareByValue(oldEntry, match))
                {
                    Bucket(byBooking, bookingId).Add(Change(
                        entityType, labelOf(oldEntity), field, before, after, AuditChangeKind.Modified));
                }
            }

            foreach (var newEntry in added)
            {
                var entity = (T)newEntry.Entity;
                Bucket(byBooking, bookingIdOf(entity)).Add(Change(
                    entityType, labelOf(entity), "—", null, labelOf(entity), AuditChangeKind.Added));
            }
        }

        /// <summary>
        /// Extra-night room rates, which live one level below the stay.
        ///
        /// <para>
        /// 🔴 These were audited for free while the rate was a single column on BookingStay. As a child
        /// table they drop out of <c>entry.Properties</c> entirely, so without this a save that only
        /// re-priced an extra night would write <b>no change and therefore no audit event at all</b> —
        /// money moving silently, in a tool where every other corrected figure is recorded.
        /// </para>
        ///
        /// <para>
        /// It walks the stays rather than the rate entries, for two reasons. An Added rate's
        /// <c>StayId</c> is still 0 (and <c>Property("StayId").CurrentValue</c> is a negative temporary
        /// key, which is why this must not go through <see cref="CompareByValue"/> — "StayId" is not in
        /// <see cref="IgnoredFields"/> and would log junk on every save). And only a <b>paired</b> stay
        /// is of interest: when the stay itself was genuinely added or removed,
        /// <see cref="DiffChildren"/> has already said so, and re-listing its rate rows underneath is
        /// exactly the noise the pairing exists to kill.
        /// </para>
        /// </summary>
        private static void DiffStayRates(
            List<EntityEntry> entries, Dictionary<int, List<BookingAuditChange>> byBooking)
        {
            var stays = entries.Where(e => e.Entity is BookingStay).ToList();
            if (stays.Count == 0)
                return;

            var added = stays.Where(e => e.State == EntityState.Added)
                             .Select(e => (BookingStay)e.Entity).ToList();

            foreach (var oldStay in stays.Where(e => e.State == EntityState.Deleted)
                                         .Select(e => (BookingStay)e.Entity))
            {
                var newStay = added.FirstOrDefault(s =>
                    s.SortOrder == oldStay.SortOrder && s.BookingId == oldStay.BookingId);

                if (newStay is null)
                    continue;   // genuinely removed — DiffChildren reported the stay itself

                var label = oldStay.Destination?.Name ?? $"Stay {oldStay.SortOrder + 1}";
                var before = oldStay.Rates.ToDictionary(r => r.RoomType);
                var after = newStay.Rates.ToDictionary(r => r.RoomType);

                foreach (var type in before.Keys.Union(after.Keys).OrderBy(t => t))
                {
                    before.TryGetValue(type, out var oldRate);
                    after.TryGetValue(type, out var newRate);

                    AddRateChange(byBooking, oldStay.BookingId, label, $"{type} rooms",
                        oldRate?.Quantity, newRate?.Quantity);
                    AddRateChange(byBooking, oldStay.BookingId, label, $"{type} rate",
                        oldRate?.Rate, newRate?.Rate);
                }
            }
        }

        /// <summary>
        /// Who is taking each tour — a grandchild, like the stay rates, and audited the same way and for
        /// the same reasons (see <see cref="DiffStayRates"/>): only for a paired tour slot, and never
        /// through <see cref="CompareByValue"/>, since an Added link's <c>GuestId</c> is a temporary key.
        ///
        /// <para>
        /// A tour logs only when its guests differ by <b>both</b> name and identity — identity being
        /// type + position among that type, the key BookingController re-points preserved tours by.
        /// Names alone would re-log every tour a renamed guest is on (the Guest row already records
        /// the rename); raw position alone would log "John, Tom → John, Tom" when adding an adult
        /// shifts the child down a row. A changed headcount is also logged as the tour's Pax by
        /// <see cref="DiffChildren"/>.
        /// </para>
        ///
        /// <para>
        /// The old side is read from the Deleted link entries rather than <c>oldTour.Guests</c>: Edit
        /// deletes the guests too, and their client cascade is free to have touched that collection.
        /// </para>
        /// </summary>
        private static void DiffTourGuests(
            List<EntityEntry> entries, Dictionary<int, List<BookingAuditChange>> byBooking)
        {
            var tours = entries.Where(e => e.Entity is BookingTour).ToList();
            if (tours.Count == 0)
                return;

            var oldLinks = entries
                .Where(e => e.Entity is BookingTourGuest && e.State == EntityState.Deleted)
                .ToLookup(e => (int)e.Property(nameof(BookingTourGuest.BookingTourId)).OriginalValue!,
                          e => ((BookingTourGuest)e.Entity).Guest);

            var added = tours.Where(e => e.State == EntityState.Added)
                             .Select(e => (BookingTour)e.Entity).ToList();

            foreach (var oldTour in tours.Where(e => e.State == EntityState.Deleted)
                                         .Select(e => (BookingTour)e.Entity))
            {
                var newTour = added.FirstOrDefault(t =>
                    t.SortOrder == oldTour.SortOrder && t.BookingId == oldTour.BookingId);

                if (newTour is null)
                    continue;   // genuinely removed — DiffChildren reported the tour itself

                var before = oldLinks[oldTour.Id].Where(g => g is not null).OrderBy(g => g.SortOrder).ToList();
                var after = newTour.Guests.Select(l => l.Guest).OrderBy(g => g.SortOrder).ToList();

                static string? Names(List<BookingGuest> guests) =>
                    guests.Count == 0 ? null : string.Join(", ", guests.Select(g => g.FullName));

                if (Names(before) == Names(after))
                    continue;

                var oldRoster = RosterOf(entries, EntityState.Deleted, oldTour.BookingId);
                var newRoster = RosterOf(entries, EntityState.Added, oldTour.BookingId);
                if (before.Select(g => KeyOf(oldRoster, g)).SequenceEqual(after.Select(g => KeyOf(newRoster, g))))
                    continue;

                var kind = before.Count == 0 ? AuditChangeKind.Added
                         : after.Count == 0 ? AuditChangeKind.Removed
                         : AuditChangeKind.Modified;

                Bucket(byBooking, oldTour.BookingId).Add(Change(
                    "Tour", oldTour.Tour?.Name ?? $"Tour {oldTour.SortOrder + 1}", "Guests",
                    Names(before), Names(after), kind));
            }
        }

        private static List<BookingGuest> RosterOf(List<EntityEntry> entries, EntityState state, int bookingId) =>
            entries.Where(e => e.State == state && e.Entity is BookingGuest g && g.BookingId == bookingId)
                   .Select(e => (BookingGuest)e.Entity)
                   .ToList();

        private static (GuestType, int) KeyOf(List<BookingGuest> roster, BookingGuest guest) =>
            (guest.Type, roster.Count(g => g.Type == guest.Type && g.SortOrder < guest.SortOrder));

        /// <summary>One rate field, emitted only when it actually moved. Kind follows which side is absent.</summary>
        private static void AddRateChange(
            Dictionary<int, List<BookingAuditChange>> byBooking,
            int bookingId, string label, string field, object? before, object? after)
        {
            var oldText = Render(before);
            var newText = Render(after);

            if (string.Equals(oldText, newText, StringComparison.Ordinal))
                return;

            var kind = before is null ? AuditChangeKind.Added
                     : after is null ? AuditChangeKind.Removed
                     : AuditChangeKind.Modified;

            Bucket(byBooking, bookingId).Add(Change("Stay", label, field, oldText, newText, kind));
        }

        /// <summary>Property-by-property comparison of a Deleted row's originals against an Added row's values.</summary>
        private static List<(string Field, string? Before, string? After)> CompareByValue(
            EntityEntry oldEntry, EntityEntry newEntry)
        {
            var result = new List<(string, string?, string?)>();

            foreach (var prop in oldEntry.Properties)
            {
                var name = prop.Metadata.Name;
                if (IgnoredFields.Contains(name))
                    continue;

                var before = Render(prop.OriginalValue);
                var after = Render(newEntry.Property(name).CurrentValue);

                if (!string.Equals(before, after, StringComparison.Ordinal))
                    result.Add((name, before, after));
            }

            return result;
        }

        private static List<BookingAuditChange> DiffProperties(EntityEntry entry, string entityType, string? label)
        {
            var changes = new List<BookingAuditChange>();

            foreach (var prop in entry.Properties)
            {
                if (!prop.IsModified || IgnoredFields.Contains(prop.Metadata.Name))
                    continue;

                var before = Render(prop.OriginalValue);
                var after = Render(prop.CurrentValue);
                if (string.Equals(before, after, StringComparison.Ordinal))
                    continue;

                changes.Add(Change(entityType, label, prop.Metadata.Name, before, after, AuditChangeKind.Modified));
            }

            return changes;
        }

        private static List<BookingAuditChange> Single(
            string entityType, string? label, string field, string? before, string? after, AuditChangeKind kind) =>
            new() { Change(entityType, label, field, before, after, kind) };

        private static BookingAuditChange Change(
            string entityType, string? label, string field, string? before, string? after, AuditChangeKind kind) =>
            new()
            {
                EntityType = entityType,
                EntityLabel = Truncate(label, 200),
                Field = Truncate(field, 100) ?? field,
                OldValue = Truncate(before, 1000),
                NewValue = Truncate(after, 1000),
                Kind = kind,
            };

        /// <summary>As it would read on screen — an audit row records what was displayed.</summary>
        private static string? Render(object? value) => value switch
        {
            null => null,
            bool b => b ? "Yes" : "No",
            DateOnly d => d.ToString("dd MMM yyyy"),
            DateTime dt => dt.ToString("dd MMM yyyy HH:mm"),
            TimeOnly t => t.ToString("HH:mm"),
            decimal m => m.ToString("#,##0.00"),
            Enum e => e.ToString(),
            _ => value.ToString(),
        };

        private static string? Truncate(string? value, int max) =>
            value is null ? null : value.Length <= max ? value : value[..max];

        private static List<BookingAuditChange> Bucket(Dictionary<int, List<BookingAuditChange>> map, int bookingId)
        {
            if (!map.TryGetValue(bookingId, out var list))
                map[bookingId] = list = new List<BookingAuditChange>();
            return list;
        }
    }
}
