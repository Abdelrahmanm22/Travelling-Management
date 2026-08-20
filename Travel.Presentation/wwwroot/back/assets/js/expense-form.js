// Drives the block-expense form: the arrival-group picker, the per-room / per-pax swap, and the live
// preview.
//
// The preview is ADVISORY. ExpenseController re-runs the same IExpenseCalculator on the POST and
// stores that result — the client never posts a computed amount, or a tampered form could write any
// figure it liked into a safe.
//
// The JSON field names below are a contract with ExpenseController.Preview / .Groups, not an
// implementation detail; MVC's default camelCase policy is what lines them up. And renderGroups()
// mirrors the markup in _ExpenseForm.cshtml — a change to one needs the same change in the other.
(function () {
    'use strict';

    var form = document.getElementById('expense-form');
    if (!form) return;

    var PER_ROOM = '1';
    var PER_PAX = '2';

    var safeSelect = document.getElementById('expense-safe');
    var typeSelect = document.getElementById('expense-type');
    var picker = document.getElementById('group-picker');
    var chips = document.getElementById('group-chips');
    var perRoom = document.getElementById('rates-per-room');
    var perPax = document.getElementById('rates-per-pax');
    var paxRule = document.getElementById('pax-rule');
    var preview = document.getElementById('expense-preview');
    var isEdit = (form.querySelector('input[name="IsEdit"]') || {}).value === 'True';

    // The currency bookings are priced in. Supplied by the page so Money.Code stays the one owner.
    var baseCurrency = form.getAttribute('data-base-currency') || 'USD';

    // The last total the server calculated, so the rate preview can show a real converted figure.
    var lastTotal = null;

    // Every key the operator has ticked, including ones the current filter no longer shows. Kept here
    // rather than read off the DOM, because filtering re-renders the rows — reading the checkboxes
    // after a filter change would silently lose every selection made under the previous one.
    var selected = new Set();
    if (picker) {
        picker.querySelectorAll('input[name="SelectedGroupKeys"]:checked')
            .forEach(function (input) { selected.add(input.value); });
    }

    // Groups seen so far, by key, so a chip can still name a group the filter has hidden.
    var known = {};
    if (picker) {
        picker.querySelectorAll('.group-picker-row').forEach(function (row) {
            var input = row.querySelector('input[name="SelectedGroupKeys"]');
            if (!input) return;
            known[input.value] = row.querySelector('.fw-semibold').textContent.trim()
                + ' · ' + (row.textContent.match(/\d{2} \w{3} \d{4}/) || [''])[0];
        });
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
                + ' in every group you ticked, then shared out by each booking\'s share.';
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
        var preview = document.getElementById('rate-preview');
        var input = form.querySelector('[name="UnitsPerUsd"]');
        if (!preview || !input) return;

        var rate = parseFloat(input.value);
        var total = currentTotal();

        if (!(rate > 0) || total === null) { preview.textContent = ''; return; }

        preview.textContent = '→ ' + baseCurrency + ' '
            + (total / rate).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
            + ' at this rate';
    }

    // The figure the preview converts: the calculated grand total when we have one, else the typed
    // per-pax lump sum. Per-room has no total until the preview returns, so it simply shows nothing.
    function currentTotal() {
        if (lastTotal !== null) return lastTotal;
        var lump = form.querySelector('[name="LumpSumTotal"]');
        var value = lump ? parseFloat(lump.value) : NaN;
        return isNaN(value) ? null : value;
    }

    // ── Group picker ────────────────────────────────────────────────────

    function renderChips() {
        if (!chips) return;
        chips.innerHTML = '';

        selected.forEach(function (key) {
            var chip = document.createElement('span');
            chip.className = 'group-chip me-1 mb-1';
            chip.innerHTML = '<i class="bx bx-map-pin"></i> '
                + escapeHtml(known[key] || key)
                + ' <i class="bx bx-x group-chip-remove" data-key="' + escapeHtml(key) + '"></i>';
            chips.appendChild(chip);
        });
    }

    // Selections the current filter no longer shows still have to post, or narrowing the dates would
    // silently drop them.
    function renderHiddenSelections() {
        form.querySelectorAll('.js-hidden-group').forEach(function (el) { el.remove(); });
        if (!picker) return;

        var visible = {};
        picker.querySelectorAll('input[name="SelectedGroupKeys"]').forEach(function (input) {
            visible[input.value] = true;
        });

        selected.forEach(function (key) {
            if (visible[key]) return;

            var hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = 'SelectedGroupKeys';
            hidden.value = key;
            hidden.className = 'js-hidden-group';
            picker.parentNode.appendChild(hidden);
        });
    }

    // Mirrors the row markup in _ExpenseForm.cshtml. Keep the two in step.
    function renderGroups(groups) {
        if (!picker) return;
        picker.innerHTML = '';

        if (!groups.length) {
            picker.innerHTML = '<div class="text-muted p-2">No arrival groups in this range.</div>';
            return;
        }

        groups.forEach(function (g) {
            known[g.key] = g.destinationName + ' · ' + g.checkInText;

            var nights = g.minNights === g.maxNights ? g.minNights + 'n' : g.minNights + '–' + g.maxNights + 'n';
            var isSelected = selected.has(g.key);

            var label = document.createElement('label');
            label.className = 'group-picker-row d-flex align-items-start gap-2 p-2 mb-2' + (isSelected ? ' is-selected' : '');
            label.innerHTML =
                '<input class="form-check-input mt-1 flex-shrink-0" type="checkbox" name="SelectedGroupKeys" value="'
                    + escapeHtml(g.key) + '"' + (isSelected ? ' checked' : '') + ' />'
                + '<span class="flex-grow-1 min-width-0">'
                + '<span class="fw-semibold">' + escapeHtml(g.destinationName) + '</span>'
                + '<span class="text-muted"> · ' + escapeHtml(g.checkInText) + '</span>'
                + '<span class="d-block font-size-12 text-muted">'
                + g.bookingCount + ' booking' + (g.bookingCount === 1 ? '' : 's')
                + ' · ' + g.pax + ' pax (' + g.adults + ' ad, ' + g.children + ' ch, ' + g.infants + ' inf)'
                + ' · ' + escapeHtml(g.rooms)
                + ' · ' + nights
                + '</span></span>';

            picker.appendChild(label);
        });
    }

    function loadGroups() {
        if (!picker) return;

        var params = {
            from: value('groups-from'),
            to: value('groups-to'),
            destinationId: value('groups-destination')
        };

        $.getJSON(form.getAttribute('data-groups-url'), params)
            .done(function (res) {
                if (!res.success) return;
                renderGroups(res.groups);
                renderHiddenSelections();
                renderChips();
                schedulePreview();
            })
            .fail(function () { toastr.error('Could not load arrival groups.'); });
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

        if (!selected.size || !basis) {
            lastTotal = null;
            renderRatePreview(currentCurrency());
            preview.innerHTML = emptyPreview('Pick an expense type and at least one arrival group to see the breakdown.');
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

        res.groups.forEach(function (g) {
            html += '<div class="expense-group p-2 mb-2">'
                + '<div class="d-flex justify-content-between gap-2">'
                + '<span class="fw-semibold font-size-12">' + escapeHtml(g.destinationName)
                + ' <span class="text-muted">· ' + escapeHtml(g.checkInText) + '</span></span>'
                + '<span class="fw-semibold font-size-12">' + money(g.amount) + '</span>'
                + '</div>'
                + '<div class="text-muted font-size-12">' + g.bookingCount + ' booking'
                + (g.bookingCount === 1 ? '' : 's') + ' · ' + escapeHtml(g.rooms) + '</div>'
                + '<table class="table table-sm mb-0 mt-1"><tbody>';

            g.bookings.forEach(function (b) {
                html += '<tr>'
                    + '<td class="ps-0 font-size-12">' + escapeHtml(b.code)
                    + '<div class="text-muted">' + escapeHtml(b.leadGuestName) + '</div></td>'
                    + '<td class="text-muted font-size-12">' + escapeHtml(b.rooms) + ' · ' + b.nights + 'n · ' + b.eligiblePax + ' pax</td>'
                    + '<td class="text-end pe-0 font-size-12">' + money(b.amount) + '</td>'
                    + '</tr>';
            });

            html += '</tbody></table></div>';
        });

        return html;
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

    if (picker) {
        picker.addEventListener('change', function (e) {
            if (!e.target.matches('input[name="SelectedGroupKeys"]')) return;

            if (e.target.checked) selected.add(e.target.value);
            else selected.delete(e.target.value);

            e.target.closest('.group-picker-row').classList.toggle('is-selected', e.target.checked);
            renderHiddenSelections();
            renderChips();
            schedulePreview();
        });
    }

    if (chips) {
        chips.addEventListener('click', function (e) {
            if (!e.target.matches('.group-chip-remove')) return;

            var key = e.target.getAttribute('data-key');
            selected.delete(key);

            var input = picker && picker.querySelector('input[value="' + cssEscape(key) + '"]');
            if (input) {
                input.checked = false;
                input.closest('.group-picker-row').classList.remove('is-selected');
            }

            renderHiddenSelections();
            renderChips();
            schedulePreview();
        });
    }

    var filterButton = document.getElementById('groups-filter');
    if (filterButton) filterButton.addEventListener('click', loadGroups);

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

    function escapeHtml(value) {
        return String(value === null || typeof value === 'undefined' ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function cssEscape(value) {
        return String(value).replace(/["\\]/g, '\\$&');
    }

    syncBasis();
    syncCurrency();
    renderChips();
    schedulePreview();
})();
