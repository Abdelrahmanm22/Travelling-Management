// Drives the expense form: the Applied-to switch (arrival groups | bookings), the two pickers, the
// per-room / per-pax swap, and the live preview.
//
// The preview is ADVISORY. ExpenseController re-runs the same IExpenseCalculator on the POST and
// stores that result — the client never posts a computed amount, or a tampered form could write any
// figure it liked into a safe.
//
// The JSON field names below are a contract with ExpenseController.Preview / .Groups / .Bookings, not
// an implementation detail; MVC's default camelCase policy is what lines them up. renderGroups() and
// renderBookings() mirror the row markup in _ExpenseForm.cshtml — a change to one needs the same
// change in the other.
(function () {
    'use strict';

    var form = document.getElementById('expense-form');
    if (!form) return;

    var PER_ROOM = '1';
    var PER_PAX = '2';
    var TARGET_GROUPS = '1';
    var TARGET_BOOKINGS = '2';

    var safeSelect = document.getElementById('expense-safe');
    var typeSelect = document.getElementById('expense-type');
    var groupsPane = document.getElementById('target-groups');
    var bookingsPane = document.getElementById('target-bookings');
    var perRoom = document.getElementById('rates-per-room');
    var perPax = document.getElementById('rates-per-pax');
    var paxRule = document.getElementById('pax-rule');
    var preview = document.getElementById('expense-preview');
    var isEdit = (form.querySelector('input[name="IsEdit"]') || {}).value === 'True';

    // The currency bookings are priced in. Supplied by the page so Money.Code stays the one owner.
    var baseCurrency = form.getAttribute('data-base-currency') || 'USD';

    // The last total the server calculated, so the rate preview can show a real converted figure.
    var lastTotal = null;

    // ── Pickers ─────────────────────────────────────────────────────────
    //
    // One shape for both lists. Every value the operator has ticked — including ones the current
    // filter no longer shows — is kept in `selected` rather than read off the DOM, because filtering
    // re-renders the rows: reading the checkboxes after a filter change would silently lose every
    // selection made under the previous one.

    function createPicker(options) {
        var list = document.getElementById(options.pickerId);
        var chips = document.getElementById(options.chipsId);
        var selected = new Set();
        var known = {};

        if (list) {
            list.querySelectorAll('.group-picker-row').forEach(function (row) {
                var input = row.querySelector('input[name="' + options.name + '"]');
                if (!input) return;
                // Server-rendered as "Cairo · 5★ · 01 Aug 2026" / "MUSB-000012 · Ahmed Ali", so a
                // chip still names a row the filter has hidden.
                known[input.value] = row.getAttribute('data-label') || input.value;
                if (input.checked) selected.add(input.value);
            });
        }

        // Edit renders no picker — the locked selection posts as hidden inputs instead, and still
        // counts as "something is selected" for the preview.
        form.querySelectorAll('input.js-locked-selection[name="' + options.name + '"]')
            .forEach(function (input) { selected.add(input.value); });

        function renderChips() {
            if (!chips) return;
            chips.innerHTML = '';

            selected.forEach(function (key) {
                var chip = document.createElement('span');
                chip.className = 'group-chip me-1 mb-1';
                chip.innerHTML = '<i class="bx ' + options.icon + '"></i> '
                    + escapeHtml(known[key] || key)
                    + ' <i class="bx bx-x group-chip-remove" data-key="' + escapeHtml(key) + '"></i>';
                chips.appendChild(chip);
            });
        }

        // Selections the current filter no longer shows still have to post, or narrowing the filter
        // would silently drop them.
        function renderHiddenSelections() {
            form.querySelectorAll('.' + options.hiddenClass).forEach(function (el) { el.remove(); });
            if (!list) return;

            var visible = {};
            list.querySelectorAll('input[name="' + options.name + '"]').forEach(function (input) {
                visible[input.value] = true;
            });

            selected.forEach(function (key) {
                if (visible[key]) return;

                var hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = options.name;
                hidden.value = key;
                hidden.className = options.hiddenClass;
                list.parentNode.appendChild(hidden);
            });
        }

        function refresh() {
            renderHiddenSelections();
            renderChips();
            schedulePreview();
        }

        if (list) {
            list.addEventListener('change', function (e) {
                if (!e.target.matches('input[name="' + options.name + '"]')) return;

                if (e.target.checked) selected.add(e.target.value);
                else selected.delete(e.target.value);

                e.target.closest('.group-picker-row').classList.toggle('is-selected', e.target.checked);
                refresh();
            });
        }

        if (chips) {
            chips.addEventListener('click', function (e) {
                if (!e.target.matches('.group-chip-remove')) return;

                var key = e.target.getAttribute('data-key');
                selected.delete(key);

                var input = list && list.querySelector('input[value="' + cssEscape(key) + '"]');
                if (input) {
                    input.checked = false;
                    input.closest('.group-picker-row').classList.remove('is-selected');
                }

                refresh();
            });
        }

        return {
            list: list,
            selected: selected,
            known: known,
            renderChips: renderChips,
            // Replaces the rows with freshly fetched ones; `rowHtml` builds one row's inner markup.
            render: function (items, keyOf, labelOf, rowHtml, emptyText) {
                if (!list) return;
                list.innerHTML = '';

                if (!items.length) {
                    list.innerHTML = '<div class="text-muted font-size-12 p-2">' + escapeHtml(emptyText) + '</div>';
                }

                items.forEach(function (item) {
                    var key = String(keyOf(item));
                    known[key] = labelOf(item);

                    var isSelected = selected.has(key);
                    var label = document.createElement('label');
                    label.className = 'group-picker-row d-flex align-items-start gap-2 p-2 mb-2' + (isSelected ? ' is-selected' : '');
                    label.setAttribute('data-label', labelOf(item));
                    label.innerHTML =
                        '<input class="form-check-input mt-1 flex-shrink-0" type="checkbox" name="' + options.name + '" value="'
                            + escapeHtml(key) + '"' + (isSelected ? ' checked' : '') + ' />'
                        + rowHtml(item);

                    list.appendChild(label);
                });

                refresh();
            }
        };
    }

    var groups = createPicker({
        pickerId: 'group-picker', chipsId: 'group-chips', name: 'SelectedGroupKeys',
        hiddenClass: 'js-hidden-group', icon: 'bx-map-pin'
    });

    var bookings = createPicker({
        pickerId: 'booking-picker', chipsId: 'booking-chips', name: 'SelectedBookingIds',
        hiddenClass: 'js-hidden-booking', icon: 'bx-user'
    });

    // ── Target ──────────────────────────────────────────────────────────

    function currentTarget() {
        var radio = form.querySelector('input[name="Target"]:checked');
        if (radio) return radio.value;
        // Edit locks the target into a hidden input.
        var hidden = form.querySelector('input[type="hidden"][name="Target"]');
        return hidden ? hidden.value : TARGET_GROUPS;
    }

    function activePicker() {
        return currentTarget() === TARGET_BOOKINGS ? bookings : groups;
    }

    function syncTarget() {
        var forBookings = currentTarget() === TARGET_BOOKINGS;
        if (groupsPane) groupsPane.classList.toggle('d-none', forBookings);
        if (bookingsPane) bookingsPane.classList.toggle('d-none', !forBookings);
        syncBasis();
    }

    // ── Basis ───────────────────────────────────────────────────────────

    function currentType() {
        if (!typeSelect) return null;
        var option = typeSelect.options[typeSelect.selectedIndex];
        return option && option.value ? option : null;
    }

    function currentBasis() {
        var option = currentType();
        return option ? option.getAttribute('data-basis') : null;
    }

    function syncBasis() {
        if (isEdit || !perRoom || !perPax) return;

        var basis = currentBasis();
        perRoom.classList.toggle('d-none', basis !== PER_ROOM);
        perPax.classList.toggle('d-none', basis !== PER_PAX);

        var option = currentType();
        if (option && paxRule) {
            var who = ['adults'];
            if (option.getAttribute('data-children') === 'true') who.push('children');
            if (option.getAttribute('data-infants') === 'true') who.push('infants');

            paxRule.textContent = 'One total for the whole expense, divided across '
                + who.join(', ').replace(/, ([^,]*)$/, ' and $1')
                + (currentTarget() === TARGET_BOOKINGS
                    ? ' of every booking you ticked, by each booking\'s share.'
                    : ' in every group you ticked, then shared out by each booking\'s share.');
        }
    }

    function currentCurrency() {
        if (!safeSelect) {
            // Edit locks the safe, so the currency is whatever the server already stamped.
            var stamped = form.querySelector('.js-currency');
            return stamped ? stamped.textContent.trim() : null;
        }
        var option = safeSelect.options[safeSelect.selectedIndex];
        return option ? option.getAttribute('data-currency') : null;
    }

    function syncCurrency() {
        var code = currentCurrency();
        if (!code) return;

        form.querySelectorAll('.js-currency').forEach(function (el) { el.textContent = code; });
        syncRate(code);
    }

    // A safe already in the base currency needs no rate — the server forces 1 in that case, so hiding
    // the input is a convenience, not the rule.
    function syncRate(code) {
        var block = document.getElementById('exchange-rate');
        if (!block) return;

        block.classList.toggle('d-none', !code || code === baseCurrency);
        renderRatePreview(code);
    }

    function renderRatePreview(code) {
        var ratePreview = document.getElementById('rate-preview');
        var input = form.querySelector('[name="UnitsPerUsd"]');
        if (!ratePreview || !input) return;

        var rate = parseFloat(input.value);
        var total = currentTotal();

        if (!(rate > 0) || total === null) { ratePreview.textContent = ''; return; }

        ratePreview.textContent = '→ ' + baseCurrency + ' '
            + (total / rate).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
            + ' at this rate';
    }

    // The figure the preview converts: the calculated grand total when we have one, else the typed
    // per-pax lump sum. Per-room has no total until the preview returns, so it simply shows nothing.
    function currentTotal() {
        if (lastTotal !== null) return lastTotal;
        var lump = form.querySelector('[name="LumpSumTotal"]');
        var amount = lump ? parseFloat(lump.value) : NaN;
        return isNaN(amount) ? null : amount;
    }

    // ── Loading the pickers ─────────────────────────────────────────────

    // Mirrors the group row markup in _ExpenseForm.cshtml. Keep the two in step.
    function groupRow(g) {
        var nights = g.minNights === g.maxNights ? g.minNights + 'n' : g.minNights + '–' + g.maxNights + 'n';

        return '<span class="flex-grow-1 min-width-0">'
            + '<span class="fw-bold">' + escapeHtml(g.destinationName) + '</span>'
            + '<span class="' + escapeHtml(g.categoryCss) + ' ms-1">' + escapeHtml(g.categoryText) + '</span>'
            + (g.privateCode
                ? '<span class="badge bg-dark-subtle text-dark ms-1"><i class="bx bx-lock-alt me-1"></i>Private · '
                    + escapeHtml(g.privateCode) + '</span>'
                : '')
            + '<span class="text-muted"> · ' + escapeHtml(g.checkInText) + '</span>'
            + '<span class="d-block font-size-12 text-muted">'
            + g.bookingCount + ' booking' + (g.bookingCount === 1 ? '' : 's')
            + ' · ' + g.pax + ' pax (' + g.adults + ' ad, ' + g.children + ' ch, ' + g.infants + ' inf)'
            + ' · ' + escapeHtml(g.rooms)
            + ' · ' + nights
            + '</span></span>';
    }

    // Mirrors the booking row markup in _ExpenseForm.cshtml. Keep the two in step.
    function bookingRow(b) {
        return '<span class="flex-grow-1 min-width-0">'
            + '<span class="badge bg-primary-subtle text-primary">' + escapeHtml(b.code) + '</span>'
            + '<span class="fw-bold ms-1">' + escapeHtml(b.leadGuestName) + '</span>'
            + '<span class="' + escapeHtml(b.categoryCss) + ' ms-1">' + escapeHtml(b.categoryText) + '</span>'
            + (b.isPrivate
                ? '<span class="badge bg-dark-subtle text-dark ms-1"><i class="bx bx-lock-alt me-1"></i>Private</span>'
                : '')
            + (b.isCancelled
                ? '<span class="badge bg-danger-subtle text-danger ms-1"><i class="bx bx-x-circle me-1"></i>Cancelled</span>'
                : '')
            + '<span class="text-muted"> · ' + escapeHtml(b.datesText) + '</span>'
            + '<span class="d-block font-size-12 text-muted">'
            + escapeHtml(b.route)
            + ' · ' + escapeHtml(b.paxText)
            + ' · ' + escapeHtml(b.rooms)
            + ' · ' + b.nights + ' n'
            + '</span></span>'
            + (bookings.list && bookings.list.getAttribute('data-can-open') === 'true' && b.url
                ? '<a href="' + escapeHtml(b.url) + '" target="_blank" rel="noopener" class="text-muted flex-shrink-0" title="Open the booking">'
                    + '<i class="bx bx-link-external"></i></a>'
                : '');
    }

    function loadGroups() {
        if (!groups.list) return;

        $.getJSON(form.getAttribute('data-groups-url'), {
            from: value('groups-from'),
            to: value('groups-to'),
            destinationId: value('groups-destination')
        })
            .done(function (res) {
                if (!res.success) return;
                groups.render(res.groups,
                    function (g) { return g.key; },
                    function (g) { return g.label; },
                    groupRow,
                    'No arrival groups in this range.');
            })
            .fail(function () { toastr.error('Could not load arrival groups.'); });
    }

    function loadBookings() {
        if (!bookings.list) return;

        var cancelled = document.getElementById('bookings-cancelled');

        $.getJSON(form.getAttribute('data-bookings-url'), {
            from: value('bookings-from'),
            to: value('bookings-to'),
            destinationId: value('bookings-destination'),
            search: value('bookings-search'),
            includeCancelled: cancelled && cancelled.checked ? 'true' : 'false'
        })
            .done(function (res) {
                if (!res.success) return;
                bookings.render(res.bookings,
                    function (b) { return b.id; },
                    function (b) { return b.label; },
                    bookingRow,
                    'No bookings match this filter.');
            })
            .fail(function () { toastr.error('Could not load bookings.'); });
    }

    // ── Live preview ────────────────────────────────────────────────────

    var previewTimer = null;

    function schedulePreview() {
        if (!preview) return;
        clearTimeout(previewTimer);
        previewTimer = setTimeout(runPreview, 400);
    }

    function runPreview() {
        if (!preview) return;

        var basis = isEdit ? (perPax && !perPax.classList.contains('d-none') ? PER_PAX : PER_ROOM) : currentBasis();
        var forBookings = currentTarget() === TARGET_BOOKINGS;

        if (!activePicker().selected.size || !basis) {
            lastTotal = null;
            renderRatePreview(currentCurrency());
            preview.innerHTML = emptyPreview(forBookings
                ? 'Pick an expense type and at least one booking to see the breakdown.'
                : 'Pick an expense type and at least one arrival group to see the breakdown.');
            return;
        }

        preview.innerHTML = emptyPreview('<i class="bx bx-loader-alt bx-spin"></i> Calculating…');

        $.post(form.getAttribute('data-preview-url'), $(form).serialize())
            .done(function (res) {
                if (!res.success) {
                    lastTotal = null;
                    renderRatePreview(currentCurrency());
                    preview.innerHTML = errorPreview(res.errors || []);
                    return;
                }
                lastTotal = Number(res.total);
                renderRatePreview(currentCurrency());
                preview.innerHTML = renderPreview(res);
            })
            .fail(function () {
                preview.innerHTML = emptyPreview('Could not calculate right now.');
            });
    }

    function renderPreview(res) {
        var html = '<div class="d-flex justify-content-between align-items-baseline mb-1">'
            + '<span class="text-muted font-size-12">Total</span>'
            + '<span class="expense-total">'
            + '<span class="safe-currency">' + escapeHtml(res.currency) + '</span>' + money(res.total)
            + '</span></div>';

        if (res.perPerson !== null && typeof res.perPerson !== 'undefined') {
            html += '<div class="text-muted font-size-12 mb-2">'
                + escapeHtml(res.currency) + ' ' + money(res.perPerson) + ' per person</div>';
        }

        if (res.balanceAfter !== null && typeof res.balanceAfter !== 'undefined') {
            var negative = res.balanceAfter < 0;
            html += '<div class="font-size-12 mb-3 ' + (negative ? 'text-danger' : 'text-muted') + '">'
                + (negative ? '<i class="bx bx-error-circle"></i> ' : '')
                + 'Leaves the safe at ' + escapeHtml(res.currency) + ' ' + money(res.balanceAfter)
                + '</div>';
        }

        // A booking expense has no groups to head its lines — one flat table.
        if (String(res.target) === TARGET_BOOKINGS) {
            html += '<div class="expense-group p-2 mb-2">'
                + '<div class="text-muted font-size-12">' + res.bookings.length + ' booking'
                + (res.bookings.length === 1 ? '' : 's') + ' · whole-trip nights</div>'
                + bookingTable(res.bookings)
                + '</div>';
            return html;
        }

        res.groups.forEach(function (g) {
            html += '<div class="expense-group p-2 mb-2">'
                + '<div class="d-flex justify-content-between gap-2">'
                + '<span class="fw-bold font-size-12">' + escapeHtml(g.destinationName)
                + ' <span class="text-muted">· ' + escapeHtml(g.categoryText) + ' · ' + escapeHtml(g.checkInText) + '</span></span>'
                + '<span class="fw-bold font-size-12">' + money(g.amount) + '</span>'
                + '</div>'
                + '<div class="text-muted font-size-12">' + g.bookingCount + ' booking'
                + (g.bookingCount === 1 ? '' : 's') + ' · ' + escapeHtml(g.rooms) + '</div>'
                + bookingTable(g.bookings)
                + '</div>';
        });

        return html;
    }

    function bookingTable(lines) {
        var html = '<table class="table table-sm mb-0 mt-1"><tbody>';

        lines.forEach(function (b) {
            html += '<tr>'
                + '<td class="ps-0 font-size-12">' + escapeHtml(b.code)
                + '<div class="text-muted">' + escapeHtml(b.leadGuestName) + '</div></td>'
                + '<td class="text-muted font-size-12">' + escapeHtml(b.rooms) + ' · ' + b.nights + 'n · ' + b.eligiblePax + ' pax</td>'
                + '<td class="text-end pe-0 font-size-12">' + money(b.amount) + '</td>'
                + '</tr>';
        });

        return html + '</tbody></table>';
    }

    function errorPreview(errors) {
        var html = '<div class="text-danger font-size-12">';
        errors.forEach(function (e) { html += '<div>' + escapeHtml(e.message) + '</div>'; });
        return html + '</div>';
    }

    function emptyPreview(text) {
        return '<div class="text-muted font-size-12">' + text + '</div>';
    }

    // ── Wiring ──────────────────────────────────────────────────────────

    form.querySelectorAll('input[type="radio"][name="Target"]').forEach(function (radio) {
        radio.addEventListener('change', function () { syncTarget(); schedulePreview(); });
    });

    var groupsFilter = document.getElementById('groups-filter');
    if (groupsFilter) groupsFilter.addEventListener('click', loadGroups);

    var bookingsFilter = document.getElementById('bookings-filter');
    if (bookingsFilter) bookingsFilter.addEventListener('click', loadBookings);

    var cancelledToggle = document.getElementById('bookings-cancelled');
    if (cancelledToggle) cancelledToggle.addEventListener('change', loadBookings);

    // Enter in the search box searches — it must never submit the whole expense.
    var search = document.getElementById('bookings-search');
    if (search) {
        search.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter') return;
            e.preventDefault();
            loadBookings();
        });
    }

    if (typeSelect) {
        typeSelect.addEventListener('change', function () { syncBasis(); schedulePreview(); });
    }

    if (safeSelect) {
        safeSelect.addEventListener('change', function () { syncCurrency(); schedulePreview(); });
    }

    form.querySelectorAll('.js-rate, [name="LumpSumTotal"]').forEach(function (input) {
        input.addEventListener('input', schedulePreview);
    });

    // The exchange rate changes no amount the server calculates — it only affects how the expense
    // reads against a base-currency sale price — so it refreshes its own line and skips the preview.
    var rateInput = form.querySelector('[name="UnitsPerUsd"]');
    if (rateInput) {
        rateInput.addEventListener('input', function () { renderRatePreview(currentCurrency()); });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    function value(id) {
        var el = document.getElementById(id);
        return el ? el.value : '';
    }

    function money(amount) {
        return Number(amount).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function escapeHtml(text) {
        return String(text === null || typeof text === 'undefined' ? '' : text)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function cssEscape(text) {
        return String(text).replace(/["\\]/g, '\\$&');
    }

    syncTarget();
    syncCurrency();
    groups.renderChips();
    bookings.renderChips();
    schedulePreview();
})();
