// Booking Create + Edit — one editable schedule with live recalculation. Both forms rebuild it
// from the package via the Preview endpoint when the package, arrival or cruise sailing changes;
// Create additionally generates the very first set of rows that way.
(function () {
    'use strict';

    function token(form) {
        var el = form.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }
    // Date math done entirely in UTC so a positive local offset (e.g. UTC+2/+3) can't shift the day.
    function addDays(iso, days) {
        var p = iso.split('-');
        var d = new Date(Date.UTC(+p[0], +p[1] - 1, +p[2]));
        d.setUTCDate(d.getUTCDate() + days);
        return d.toISOString().slice(0, 10);
    }
    function fmt(iso) {
        var p = iso.split('-');
        var d = new Date(Date.UTC(+p[0], +p[1] - 1, +p[2]));
        return d.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' });
    }
    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    // Create-only and Edit-only elements are absent on the other form — these no-op on null.
    function show(el, on) { if (el) el.classList.toggle('d-none', !on); }
    function setText(el, s) { if (el) el.textContent = s; }

    // ─── Money (absent for users without Bookings.EditPricing) ───────────────
    // One module rather than one per section, because the grand total spans all three: pax pricing
    // lives in the Guests card, extra nights in the Schedule table, tours in the Tours card. Both
    // inner modules call totals.refresh() rather than each keeping its own running figure.
    //
    // Every element it touches is absent for a user who can't price, so refresh() must stay a no-op
    // in that case rather than throwing and taking the rest of the form's JS down with it.
    var totals = (function () {
        var form = document.getElementById('booking-form') || document.getElementById('booking-edit-form');
        var grand = document.getElementById('pax-grand-total');
        // The currency label is read off the markup so Helpers/Money.cs stays its only source.
        var currency = (grand && grand.getAttribute('data-currency')) || '';

        function money(amount) {
            return (currency ? currency + ' ' : '') +
                amount.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        }
        function num(el) {
            if (!el || el.value === '') return null;
            var n = parseFloat(el.value);
            return isNaN(n) || n < 0 ? null : n;
        }
        function intOf(el) {
            var n = el ? parseInt(el.value, 10) : 0;
            return isNaN(n) || n < 0 ? 0 : n;
        }

        // Pax: count × rate per type. Returns null when a type with travellers has no rate yet, so
        // the grand total reads "—" instead of a figure that silently omits them.
        function pax() {
            var subtotals = form.querySelectorAll('.pax-subtotal');
            if (!subtotals.length) return 0;

            var sum = 0, complete = true;
            subtotals.forEach(function (span) {
                var type = span.getAttribute('data-guest-type');
                var countEl = form.querySelector('.pax-count[data-guest-type="' + type + '"]');
                var priceEl = form.querySelector('.pax-price[data-guest-type="' + type + '"]');
                var n = intOf(countEl), rate = num(priceEl);

                if (n === 0) { span.textContent = '—'; return; }
                if (rate === null) { span.textContent = '—'; complete = false; return; }

                span.textContent = money(n * rate);
                sum += n * rate;
            });
            return complete ? sum : null;
        }

        // Extra nights: derived per row from nights − baseline, so the badge, the rate input's
        // enabled state and the row total all follow the schedule rather than anything typed.
        function extraNights() {
            var rows = form.querySelectorAll('.stay-row');
            var sum = 0, complete = true;

            rows.forEach(function (row) {
                var badge = row.querySelector('.stay-extra');
                if (!badge) return;   // pricing columns not rendered for this user

                var nights = intOf(row.querySelector('.stay-nights'));
                var baseline = intOf(row.querySelector('.stay-baseline'));
                var extra = Math.max(0, nights - baseline);
                var input = row.querySelector('.stay-extra-price');
                var lineTotal = row.querySelector('.stay-extra-total');

                badge.textContent = extra > 0 ? '+' + extra : '—';
                badge.className = 'badge stay-extra ' +
                    (extra > 0 ? 'bg-warning-subtle text-warning' : 'bg-secondary-subtle text-secondary');

                if (extra === 0) {
                    // Blanked as well as disabled: a rate left behind a row that stopped being extra
                    // would come back the moment someone raised the nights again. The server does the
                    // same in BuildStaysFromInputsAsync.
                    if (input) { input.disabled = true; input.value = ''; }
                    setText(lineTotal, '');
                    return;
                }

                if (input) input.disabled = false;
                var rate = num(input);
                if (rate === null) { setText(lineTotal, ''); complete = false; return; }

                setText(lineTotal, money(extra * rate));
                sum += extra * rate;
            });

            return complete ? sum : null;
        }

        // Tours: pax × unit price per row.
        function tours() {
            var rows = form.querySelectorAll('.tour-row');
            var sum = 0, complete = true;

            rows.forEach(function (row) {
                var n = intOf(row.querySelector('.tour-pax'));
                var rate = num(row.querySelector('.tour-price'));
                var lineTotal = row.querySelector('.tour-total');

                if (rate === null || n === 0) { setText(lineTotal, '—'); if (rate === null) complete = false; return; }

                setText(lineTotal, money(n * rate));
                sum += n * rate;
            });

            return complete ? sum : null;
        }

        function refresh() {
            if (!form || !grand) return;

            var parts = [pax(), extraNights(), tours()];
            setText(form.querySelector('#extra-nights-total'), money(parts[1] || 0));
            setText(form.querySelector('#tours-total'), money(parts[2] || 0));

            // Any incomplete component makes the whole total unknown — a partial figure would be
            // read as the price and quoted to the client.
            var incomplete = parts.some(function (p) { return p === null; });
            var sum = parts.reduce(function (a, p) { return a + (p || 0); }, 0);

            grand.textContent = incomplete ? '—' : money(sum);
            refreshFirstPayment(incomplete ? null : sum);
        }

        // Create only: the first payment must be at least 10% of the total, and the operator needs
        // to see that figure move as they price the booking.
        function refreshFirstPayment(total) {
            var hint = document.getElementById('first-payment-min');
            if (!hint) return;
            var min = total === null ? null : Math.round(total * 10) / 100;   // 10%, to the cent
            hint.textContent = min === null ? '—' : money(min);
            hint.setAttribute('data-min', min === null ? '' : min);
        }

        return { refresh: refresh, money: money };
    })();

    // ─── Schedule (shared by Create and Edit) ────────────────────────────────
    (function schedule() {
        var form = document.getElementById('booking-form') || document.getElementById('booking-edit-form');
        if (!form) return;

        var container = document.getElementById('stays-container');
        var tpl = document.getElementById('stay-row-template');
        if (!container || !tpl) return;

        var isCreate = form.id === 'booking-form';

        var arrivalInput = document.getElementById('arrivalDate');
        var departureInput = document.getElementById('departureDate');
        var departureHint = document.getElementById('departureHint');
        // Actual flight dates — mirror the package dates until the operator enters their own.
        var flightArrival = document.getElementById('flightArrivalDate');
        var flightDeparture = document.getElementById('flightDepartureDate');
        var flightDirty = { arrival: false, departure: false };
        var banner = document.getElementById('schedule-banner');
        var manualFlag = document.getElementById('isManuallyEdited');
        var addBtn = document.getElementById('add-stay');

        // By name, not id — an id override would break the <label asp-for="PackageId"> association.
        var pkg = form.querySelector('select[name="PackageId"]');
        var embarkBlock = document.getElementById('embarkBlock');
        var embarkOptions = document.getElementById('embarkOptions');
        var embarkHint = document.getElementById('embarkHint');
        var embarkTpl = document.getElementById('embark-option-template');
        var extAlert = document.getElementById('extensionAlert');
        var previewError = document.getElementById('previewError');
        var url = form.getAttribute('data-preview-url');

        // Create-only — Edit always has at least one stay, so it has no empty state and no
        // wrapper to toggle. show() no-ops on null.
        var previewEmpty = document.getElementById('previewEmpty');
        var previewWrap = document.getElementById('previewWrap');
        var rebuildBtn = document.getElementById('rebuild-schedule-btn');

        // Edit-only. Absent for users without Bookings.Regenerate — must be null-guarded.
        var regenBtn = document.getElementById('regenerate-btn');
        var regenForm = document.getElementById('regenerate-form');

        // Package context for the banner. Seeded from data-* (correct on Edit, and on any Create
        // redisplay after a failed post) and refreshed from every successful preview.
        var cfg = {
            hasFixed: form.getAttribute('data-fixed') === 'true',
            pkgTotal: parseInt(form.getAttribute('data-total'), 10),
            baseId: form.getAttribute('data-base') || '',
            hasCruise: form.getAttribute('data-cruise') === 'true'
        };

        var applied = { pkg: '', arrival: '', departure: '', embark: '' }; // last values a render was built from
        var quiet = false;  // suppresses dirty-marking while we rewrite rows ourselves
        var seq = 0;        // discards preview responses that arrive out of order
        var timer = null;
        // The generator's wording, only valid while the schedule is untouched. Seeded from the
        // server so Edit shows the stored reason without waiting for a preview.
        var extensionReason = form.getAttribute('data-extension-reason') || '';

        function rows() { return Array.prototype.slice.call(container.querySelectorAll('.stay-row')); }
        function arrival() { return arrivalInput.value; }
        // asp-for renders a bool hidden as "True"/"False"; markEdited() writes "true"/"false".
        // Without the fold, a saved manually-edited booking reads as clean on the initial Edit GET
        // and the rebuild confirm is skipped — silently discarding the operator's hotels and notes.
        function isDirty() { return !!manualFlag && String(manualFlag.value).toLowerCase() === 'true'; }
        function selectedEmbark() {
            var r = embarkOptions && embarkOptions.querySelector('input[name="EmbarkDay"]:checked');
            return r ? r.value : '';
        }

        function markEdited(row) {
            if (quiet) return;
            if (manualFlag) manualFlag.value = 'true';
            if (row) {
                var ov = row.querySelector('.stay-overridden');
                if (ov) ov.value = 'true';
            }
            // The generator's wording described the schedule it built; the night count stays live.
            extensionReason = '';
        }

        // Hotel/Notes grow with their content. rows="1" is the floor; the scrollHeight guard is for
        // Create, where #previewWrap starts hidden and every box would otherwise be pinned to 0px.
        function autosize(el) {
            el.style.height = 'auto';
            if (el.scrollHeight) el.style.height = el.scrollHeight + 'px';
        }
        function autosizeAll() {
            container.querySelectorAll('.stay-grow').forEach(autosize);
        }

        function recalc() {
            var cursor = arrival();
            var total = 0;
            rows().forEach(function (row, i) {
                row.querySelector('.stay-index').textContent = i + 1;
                // textarea included — Hotel/Notes are textareas, and leaving them out would keep
                // their names on a stale Stays[n], posting them against the wrong stay after a move.
                row.querySelectorAll('select, input, textarea').forEach(function (f) {
                    if (f.name) f.name = f.name.replace(/Stays\[\d+\]/, 'Stays[' + i + ']');
                });
                var nights = parseInt(row.querySelector('.stay-nights').value, 10) || 0;
                var checkin = row.querySelector('.stay-checkin');
                var checkout = row.querySelector('.stay-checkout');
                if (cursor) {
                    checkin.value = cursor;
                    cursor = addDays(cursor, nights);
                    setText(checkout, 'out ' + fmt(cursor));
                } else {
                    setText(checkout, '');
                }
                total += nights;
            });
            syncDepartureMode(total);
            syncFlightDates();
            updateExtension(total);
            updateBanner(total);
            // Nights just moved, so the per-row extra counts and every money figure did too.
            totals.refresh();
        }

        // Mirrors the server's ApplyExtraNights, so the figure on screen is the one that gets saved.
        function updateExtension(total) {
            if (!extAlert) return;

            var extra = cfg.hasFixed && !isNaN(cfg.pkgTotal) ? Math.max(0, total - cfg.pkgTotal) : 0;
            if (extra <= 0) { show(extAlert, false); return; }

            extAlert.innerHTML = '<i class="bx bx-error-circle me-1"></i> This booking includes ' + extra +
                ' extra night' + (extra > 1 ? 's' : '') + '. ' +
                (extensionReason ? escapeHtml(extensionReason) + ' ' : '') +
                'Confirm this is charged to the guest.';
            show(extAlert, true);
        }

        // The rows are the single source of truth for the departure date, except on Create before
        // anything has been generated — there it's the operator's way of telling the generator how
        // long a variable-length trip runs.
        function syncDepartureMode(total) {
            if (rows().length) {
                if (arrival()) departureInput.value = addDays(arrival(), total);
                departureInput.readOnly = true;
                setText(departureHint, 'Derived from the schedule — change the nights below to adjust it.');
                return;
            }
            // Setting .readOnly = false would strip the readonly attribute from Edit's markup, so
            // never take that branch there — Edit recomputes the departure date on POST regardless.
            if (!isCreate || cfg.hasFixed) {
                departureInput.readOnly = true;
                setText(departureHint, isCreate ? 'Computed from the package total.' : 'Derived from the schedule.');
                return;
            }
            departureInput.readOnly = false;
            setText(departureHint, 'Enter the departure date to build the schedule.');
        }

        // Convenience only — the flight dates default to the package dates until the operator edits
        // them, and are otherwise unconstrained (a guest may fly in after the start or out before
        // the end). Called from recalc() rather than a listener on departureDate — that input is
        // readonly on Edit and written programmatically, which never fires a change event.
        function syncFlightDates() {
            if (flightArrival && !flightDirty.arrival) flightArrival.value = arrivalInput.value;
            if (flightDeparture && !flightDirty.departure) flightDeparture.value = departureInput.value;
        }

        function updateBanner(total) {
            if (!rows().length) {
                show(banner, false);
                show(previewWrap, false);
                if (previewError && previewError.classList.contains('d-none')) show(previewEmpty, true);
                return;
            }
            show(previewEmpty, false);
            // A hidden element has no scrollHeight, so Create's boxes can only be sized once the
            // wrapper is actually revealed.
            var wasHidden = previewWrap && previewWrap.classList.contains('d-none');
            show(previewWrap, true);
            if (wasHidden) autosizeAll();
            if (!banner) return;

            var warnings = [];
            if (cfg.hasFixed && !isNaN(cfg.pkgTotal) && total !== cfg.pkgTotal) {
                var extra = total - cfg.pkgTotal;
                warnings.push('Schedule is ' + total + ' nights; package total is ' + cfg.pkgTotal +
                    ' (' + (extra > 0 ? '+' + extra : extra) + ').');
            }
            var last = rows()[rows().length - 1];
            if (last && cfg.baseId) {
                var lastDest = last.querySelector('.stay-destination').value;
                if (lastDest !== cfg.baseId) warnings.push('Last night is not at the base destination.');
            }

            banner.classList.remove('d-none', 'alert-warning', 'alert-success', 'alert-info');
            if (warnings.length) {
                banner.classList.add('alert-warning');
                banner.innerHTML = '<i class="bx bx-error-circle me-1"></i> ' + warnings.join(' ') +
                    ' <span class="text-muted">You can still save — confirm it’s intentional.</span>';
            } else if (cfg.hasFixed) {
                banner.classList.add('alert-success');
                banner.innerHTML = '<i class="bx bx-check-circle me-1"></i> Schedule matches the package (' + total + ' nights), last night at base.';
            } else {
                banner.classList.add('alert-info');
                banner.innerHTML = 'Schedule is ' + total + ' nights.';
            }
        }

        function addRow() {
            container.insertAdjacentHTML('beforeend', tpl.innerHTML.replace(/__INDEX__/g, rows().length));
            markEdited();
            recalc();
            autosizeAll();
        }

        container.addEventListener('click', function (e) {
            var row = e.target.closest('.stay-row');
            if (e.target.closest('.stay-remove')) {
                if (rows().length <= 1) { toastr.warning('A booking needs at least one stay.'); return; }
                row.remove(); markEdited(); recalc();
            } else if (e.target.closest('.stay-up')) {
                var prev = row.previousElementSibling;
                if (prev) { container.insertBefore(row, prev); markEdited(row); recalc(); }
            } else if (e.target.closest('.stay-down')) {
                var next = row.nextElementSibling;
                if (next) { container.insertBefore(next, row); markEdited(row); recalc(); }
            }
        });

        container.addEventListener('input', function (e) {
            if (e.target.classList.contains('stay-grow')) autosize(e.target);

            if (e.target.classList.contains('stay-nights')) { markEdited(e.target.closest('.stay-row')); recalc(); }
            // The rate moves money only — no dates, no reindexing, so skip the full recalc.
            else if (e.target.classList.contains('stay-extra-price')) { totals.refresh(); }
            else if (e.target.matches('.stay-destination, input, textarea')) {
                // Hotel/notes don't move any dates, so skip the full recalc — just refresh the summaries.
                markEdited(e.target.closest('.stay-row'));
                var total = rows().reduce(function (n, r) { return n + (parseInt(r.querySelector('.stay-nights').value, 10) || 0); }, 0);
                updateExtension(total);
                updateBanner(total);
            }
        });
        container.addEventListener('change', function (e) {
            if (e.target.classList.contains('stay-destination')) { markEdited(e.target.closest('.stay-row')); recalc(); }
        });

        if (addBtn) addBtn.addEventListener('click', addRow);

        // ─── Generating the schedule from the package (both forms) ───────────

        function showError(message) {
            show(previewEmpty, false);
            setText(previewError, message);
            show(previewError, true);
        }

        // Rebuilds the table from generated stays. Never marks the schedule as manually edited.
        function renderRows(stays) {
            quiet = true;
            container.innerHTML = stays.map(function (s, i) {
                return tpl.innerHTML.replace(/__INDEX__/g, i);
            }).join('');

            rows().forEach(function (row, i) {
                var s = stays[i];
                var sel = row.querySelector('.stay-destination');
                sel.value = String(s.destinationId);
                if (sel.value !== String(s.destinationId)) {
                    // The package references a destination that is no longer active, so it isn't in
                    // StayDestinationOptions. Inject it rather than silently posting DestinationId=0.
                    sel.insertAdjacentHTML('beforeend',
                        '<option value="' + s.destinationId + '">' + escapeHtml(s.destinationName) + '</option>');
                    sel.value = String(s.destinationId);
                }
                row.querySelector('.stay-nights').value = s.nights;
                row.querySelector('.stay-checkin').value = s.checkIn;
                var ov = row.querySelector('.stay-overridden');
                if (ov) ov.value = 'false'; // the template defaults to true, for the Add-stay case
                // The generator's baseline, not the nights: on the base-return leg of an extended
                // cruise booking these differ, and that difference IS the chargeable extension.
                // The template defaults to 0 for the Add-stay case, which must not leak into a
                // generated row or every night of it would read as extra.
                var bl = row.querySelector('.stay-baseline');
                if (bl) bl.value = s.baselineNights;
            });

            quiet = false;
            if (manualFlag) manualFlag.value = 'false';
            recalc();
            autosizeAll();
        }

        // Must produce the same DOM as _BookingEmbarkOptions.cshtml, or the picker visibly reshapes
        // after the first rebuild. Both sides clone the same template to keep that true.
        function renderEmbark(res) {
            if (!embarkOptions || !embarkTpl) return;

            var opts = res.embarkOptions || [];
            if (!opts.length) {
                embarkOptions.innerHTML = '';
                show(embarkBlock, false);
                return;
            }

            var current = selectedEmbark();
            if (!opts.some(function (o) { return o.value === current; })) current = res.defaultEmbark;

            embarkOptions.innerHTML = '';
            opts.forEach(function (o) {
                // Only __VALUE__ is substituted textually (enum names, no regex hazard); the label
                // goes in via textContent so it can't diverge from Razor's escaping.
                var holder = document.createElement('div');
                holder.innerHTML = embarkTpl.innerHTML.replace(/__VALUE__/g, o.value);
                var node = holder.firstElementChild;
                node.querySelector('.embark-text').textContent = o.text;
                var badge = node.querySelector('.embark-extra');
                badge.textContent = '+' + o.extraNights + ' night' + (o.extraNights === 1 ? '' : 's');
                badge.classList.toggle('d-none', o.extraNights <= 0);
                node.querySelector('input').checked = o.value === current;
                embarkOptions.appendChild(node);
            });

            setText(embarkHint, opts.length > 1
                ? 'Pick which sailing the guest boards.'
                : 'The only sailing available for this arrival.');
            show(embarkBlock, true);
        }

        function applyPreview(res) {
            show(previewError, false);

            if (!res.success) {
                showError(res.error || 'Unable to preview this schedule.');
                show(extAlert, false);
                return;
            }

            cfg.hasFixed = !!res.hasFixedTotal;
            cfg.pkgTotal = res.packageTotalNights == null ? NaN : res.packageTotalNights;
            cfg.baseId = res.baseDestinationId == null ? '' : String(res.baseDestinationId);
            cfg.hasCruise = !!res.hasCruise;

            extensionReason = res.extensionReason || '';
            renderEmbark(res);
            renderRows(res.stays); // recalc() inside paints the extension alert and the banner

            applied.pkg = pkg ? pkg.value : '';
            applied.arrival = arrivalInput.value;
            applied.departure = departureInput.value;
            applied.embark = selectedEmbark();
        }

        function requestPreview() {
            if (!url) return;
            if (!pkg || !pkg.value || !arrival()) {
                show(previewEmpty, true);
                show(previewWrap, false);
                show(previewError, false);
                show(extAlert, false);
                show(embarkBlock, false);
                return;
            }

            var data = new URLSearchParams();
            data.append('packageId', pkg.value);
            data.append('arrivalDate', arrival());
            if (departureInput.value) data.append('departureDate', departureInput.value);
            var emb = selectedEmbark();
            if (emb) data.append('embarkDay', emb);
            data.append('__RequestVerificationToken', token(form));

            var mine = ++seq;
            fetch(url, { method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                // Preview is gated on Bookings.View — an operator with Edit but not View gets a 403,
                // which would otherwise surface as an opaque JSON parse error.
                .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
                .then(function (res) { if (mine === seq) applyPreview(res); })
                .catch(function () { if (mine === seq) showError('Unable to reach the schedule generator. The schedule was not rebuilt.'); });
        }

        // Restores the inputs to whatever the current rows were built from. Required because
        // SweetAlert2 resolves after the change has already committed to the control.
        function revert() {
            if (pkg) pkg.value = applied.pkg;
            if (applied.arrival) arrivalInput.value = applied.arrival;
            if (!departureInput.readOnly) departureInput.value = applied.departure;
            var r = embarkOptions && applied.embark &&
                embarkOptions.querySelector('input[name="EmbarkDay"][value="' + applied.embark + '"]');
            if (r) r.checked = true;
        }

        function rebuild(what) {
            if (!isDirty()) { requestPreview(); return; }
            Swal.fire({
                title: 'Rebuild the schedule?',
                text: 'Changing the ' + what + ' regenerates the schedule from the package and discards your manual changes.',
                icon: 'warning', showCancelButton: true,
                confirmButtonColor: '#f1b44c', cancelButtonColor: '#74788d', confirmButtonText: 'Yes, rebuild'
            }).then(function (r) { r.isConfirmed ? requestPreview() : revert(); });
        }

        if (pkg) pkg.addEventListener('change', function () { rebuild('package'); });
        if (embarkOptions) embarkOptions.addEventListener('change', function () { rebuild('embark day'); });
        if (rebuildBtn) rebuildBtn.addEventListener('click', function () { rebuild('schedule'); });

        departureInput.addEventListener('change', function () {
            if (departureInput.readOnly) return; // always true on Edit — the date is derived there
            clearTimeout(timer);
            timer = setTimeout(function () { rebuild('departure date'); }, 250);
        });

        // Moving the arrival date just slides a non-cruise schedule — hotels and notes survive.
        // A cruise only sails on certain weekdays, so its embark options have to reload.
        // Debounced: a date input fires change on every arrow-key step, and without this a cruise
        // booking would get one rebuild and one dialog per keystroke.
        arrivalInput.addEventListener('change', function () {
            clearTimeout(timer);
            // Not debounced — the flight date should track the picker immediately, and the branches
            // below can return before ever reaching recalc().
            syncFlightDates();
            timer = setTimeout(function () {
                if (!rows().length) { requestPreview(); return; }
                if (cfg.hasCruise) { rebuild('arrival date'); return; }
                recalc();
                applied.arrival = arrival();
            }, 250);
        });

        // Clearing a flight date resumes mirroring, so there's always a way back to the default.
        if (flightArrival) flightArrival.addEventListener('change', function () { flightDirty.arrival = !!this.value; });
        if (flightDeparture) flightDeparture.addEventListener('change', function () { flightDirty.departure = !!this.value; });

        // Edit only — a server-side regenerate against the saved booking.
        if (regenBtn && regenForm) {
            regenBtn.addEventListener('click', function () {
                Swal.fire({
                    title: 'Regenerate schedule?',
                    text: 'This rebuilds the schedule from the package and discards any manual changes.',
                    icon: 'warning', showCancelButton: true,
                    confirmButtonColor: '#f1b44c', cancelButtonColor: '#74788d', confirmButtonText: 'Yes, regenerate'
                }).then(function (r) { if (r.isConfirmed) regenForm.submit(); });
            });
        }

        // Hard-block structural errors on submit — the schedule can't be saved incomplete.
        form.addEventListener('submit', function (e) {
            var rs = rows();
            if (rs.length === 0) {
                // Create allows it — the server falls back to generating the schedule itself.
                if (!isCreate) { e.preventDefault(); toastr.error('The schedule must have at least one stay.'); }
                return;
            }
            for (var i = 0; i < rs.length; i++) {
                var nights = parseInt(rs[i].querySelector('.stay-nights').value, 10);
                if (!nights || nights < 1) { e.preventDefault(); toastr.error('Every stay must be at least 1 night.'); return; }
                if (!rs[i].querySelector('.stay-destination').value) { e.preventDefault(); toastr.error('Every stay needs a destination.'); return; }
            }
        });

        // Must run BEFORE recalc(), which calls syncFlightDates() and would otherwise overwrite a
        // stored flight date with the package date. A rendered value that differs from the package
        // date can only have been entered by hand — editing a saved booking, or a redisplay after a
        // failed post. Equal values are indistinguishable from the mirror, so mirroring is harmless.
        if (flightArrival) flightDirty.arrival = !!flightArrival.value && flightArrival.value !== arrivalInput.value;
        if (flightDeparture) flightDirty.departure = !!flightDeparture.value && flightDeparture.value !== departureInput.value;

        recalc();
        // Sizes a saved booking's stored hotels and notes on the Edit GET.
        autosizeAll();

        // Seed after recalc() so applied.departure captures the derived value. Without this,
        // cancelling a rebuild calls revert() with an empty applied.pkg — and Edit's package select
        // has no empty option, so it would deselect entirely and post PackageId=0.
        applied.pkg = pkg ? pkg.value : '';
        applied.arrival = arrival();
        applied.departure = departureInput.value;
        applied.embark = selectedEmbark();

        // Only generate when there's nothing to lose — on a redisplay after a failed post the
        // package and arrival are both set, and the rows are the operator's edited schedule.
        // The isCreate guard is deliberate: Edit always has rows, and this states that invariant
        // rather than relying on it emerging from the row count.
        if (isCreate && !rows().length && pkg && pkg.value && arrival()) requestPreview();
    })();

    // ─── Guests & rooms (shared by Create and Edit) ──────────────────────────
    (function guestsAndRooms() {
        var form = document.getElementById('booking-form') || document.getElementById('booking-edit-form');
        if (!form) return;

        var guestsContainer = document.getElementById('guests-container');
        var roomsContainer = document.getElementById('rooms-container');
        if (!guestsContainer || !roomsContainer) return;

        var guestTpl = document.getElementById('guest-row-template');
        var roomTpl = document.getElementById('room-row-template');
        var paxTotal = document.getElementById('pax-total');
        var roomsSummary = document.getElementById('rooms-summary');
        var counts = Array.prototype.slice.call(form.querySelectorAll('.pax-count'));

        // Type → [badge classes, label, min age, max age]. Adults carry no age.
        var TYPES = {
            Adult: ['bg-primary-subtle text-primary', 'Adult', null, null],
            Child: ['bg-info-subtle text-info', 'Child (6-12)', 6, 12],
            Infant: ['bg-secondary-subtle text-secondary', 'Infant (0-5)', 0, 5]
        };
        var BEDS = { '1': 1, '2': 2, '3': 3 }; // SGL / DBL / TRP

        function guestRows() { return Array.prototype.slice.call(guestsContainer.querySelectorAll('.guest-row')); }
        function roomRows() { return Array.prototype.slice.call(roomsContainer.querySelectorAll('.room-row')); }
        function count(type) {
            var input = counts.filter(function (c) { return c.getAttribute('data-guest-type') === type; })[0];
            var n = input ? parseInt(input.value, 10) : 0;
            return isNaN(n) || n < 0 ? 0 : n;
        }

        function applyType(row, type) {
            var spec = TYPES[type];
            row.setAttribute('data-type', type);
            row.querySelector('.guest-type').value = type;

            var label = row.querySelector('.guest-type-label');
            label.className = 'badge ' + spec[0] + ' guest-type-label';
            label.textContent = spec[1];

            var age = row.querySelector('.guest-age');
            var dash = row.querySelector('.guest-age-dash');
            if (type === 'Adult') {
                age.value = '';
                age.disabled = true;
                age.classList.add('d-none');
                if (dash) dash.classList.remove('d-none');
            } else {
                age.disabled = false;
                age.min = spec[2];
                age.max = spec[3];
                age.classList.remove('d-none');
                if (dash) dash.classList.add('d-none');
            }

            // Only an adult can lead the booking.
            var lead = row.querySelector('.guest-lead');
            lead.disabled = type !== 'Adult';
            if (lead.disabled) lead.checked = false;
        }

        function reindexGuests() {
            var checked = guestsContainer.querySelector('.guest-lead:checked');
            guestRows().forEach(function (row, i) {
                row.querySelector('.guest-index').textContent = i + 1;
                row.querySelectorAll('input').forEach(function (f) {
                    if (f.name && f.name.indexOf('Guests[') === 0) {
                        f.name = f.name.replace(/Guests\[\d+\]/, 'Guests[' + i + ']');
                    }
                });
                row.querySelector('.guest-lead').value = i;
            });

            // Fall back to the first adult when the previous lead is gone or no longer eligible.
            if (!checked || checked.disabled || !checked.isConnected) {
                var firstAdult = guestRows().filter(function (r) { return r.getAttribute('data-type') === 'Adult'; })[0];
                if (firstAdult) firstAdult.querySelector('.guest-lead').checked = true;
            }

            var total = guestRows().length;
            if (paxTotal) paxTotal.textContent = total + ' PAX';
            renderRoomsSummary();
        }

        // Rebuild the roster to match the counts, keeping already-typed names/ages per type and position.
        function syncGuestRows() {
            var wanted = [];
            ['Adult', 'Child', 'Infant'].forEach(function (type) {
                for (var i = 0; i < count(type); i++) wanted.push(type);
            });
            if (wanted.length === 0) wanted.push('Adult');

            var kept = { Adult: [], Child: [], Infant: [] };
            var leadAdultPos = -1;
            guestRows().forEach(function (row) {
                var type = row.getAttribute('data-type');
                if (type === 'Adult' && row.querySelector('.guest-lead').checked) leadAdultPos = kept.Adult.length;
                kept[type].push({
                    name: row.querySelector('.guest-name').value,
                    age: row.querySelector('.guest-age').value
                });
            });

            var used = { Adult: 0, Child: 0, Infant: 0 };
            guestsContainer.innerHTML = '';
            wanted.forEach(function (type, i) {
                guestsContainer.insertAdjacentHTML('beforeend', guestTpl.innerHTML.replace(/__INDEX__/g, i));
                var row = guestRows()[i];
                var pos = used[type]++;
                applyType(row, type);

                var prev = kept[type][pos];
                if (prev) {
                    row.querySelector('.guest-name').value = prev.name;
                    if (type !== 'Adult') row.querySelector('.guest-age').value = prev.age;
                }
                // Keep the same person as lead across a rebuild.
                if (type === 'Adult' && pos === leadAdultPos) row.querySelector('.guest-lead').checked = true;
            });
            reindexGuests();
        }

        function reindexRooms() {
            roomRows().forEach(function (row, i) {
                row.querySelectorAll('select, input').forEach(function (f) {
                    if (f.name) f.name = f.name.replace(/Rooms\[\d+\]/, 'Rooms[' + i + ']');
                });
            });
            renderRoomsSummary();
        }

        // Informational only — rooming is operator-defined and never blocks the save.
        function renderRoomsSummary() {
            if (!roomsSummary) return;
            var rooms = 0, beds = 0;
            roomRows().forEach(function (row) {
                var qty = parseInt(row.querySelector('.room-qty').value, 10) || 0;
                rooms += qty;
                beds += qty * (BEDS[row.querySelector('.room-type').value] || 0);
            });
            roomsSummary.textContent = rooms === 0
                ? 'No rooms added yet.'
                : rooms + ' room' + (rooms === 1 ? '' : 's') + ' · sleeps up to ' + beds +
                  ' · ' + guestRows().length + ' PAX';
        }

        counts.forEach(function (input) {
            input.addEventListener('input', syncGuestRows);
            input.addEventListener('change', syncGuestRows);
            input.addEventListener('input', totals.refresh);
            input.addEventListener('change', totals.refresh);
        });

        form.querySelectorAll('.pax-price').forEach(function (input) {
            input.addEventListener('input', totals.refresh);
            input.addEventListener('change', totals.refresh);
        });

        // ─── Extra tours (absent for users without Bookings.EditPricing) ─────
        var toursContainer = document.getElementById('tours-container');
        var tourTpl = document.getElementById('tour-row-template');

        if (toursContainer && tourTpl) {
            var tourRows = function () {
                return Array.prototype.slice.call(toursContainer.querySelectorAll('.tour-row'));
            };

            // 'select, input' — NOT 'input' alone. A tour row leads with a <select>, and reindexGuests'
            // input-only walk is exactly the trap that would leave Tours[0].TourId on every row.
            function reindexTours() {
                tourRows().forEach(function (row, i) {
                    row.querySelectorAll('select, input').forEach(function (f) {
                        if (f.name) f.name = f.name.replace(/Tours\[\d+\]/, 'Tours[' + i + ']');
                    });
                });
                totals.refresh();
            }

            document.getElementById('add-tour').addEventListener('click', function () {
                toursContainer.insertAdjacentHTML('beforeend', tourTpl.innerHTML.replace(/__INDEX__/g, tourRows().length));
                var added = tourRows()[tourRows().length - 1];
                // Default the tour's pax to the booking's — most tours take everyone, and the
                // operator trims it for the ones that don't.
                var paxInput = added.querySelector('.tour-pax');
                if (paxInput) paxInput.value = Math.max(1, guestRows().length);
                reindexTours();
            });

            toursContainer.addEventListener('click', function (e) {
                if (!e.target.closest('.tour-remove')) return;
                e.target.closest('.tour-row').remove();
                reindexTours();
            });

            // Picking a tour fills the rate from the catalogue — a starting point, not a lock: the
            // operator's figure is what gets stored, so a discount survives the save.
            toursContainer.addEventListener('change', function (e) {
                if (e.target.classList.contains('tour-select')) {
                    var opt = e.target.options[e.target.selectedIndex];
                    var priceInput = e.target.closest('.tour-row').querySelector('.tour-price');
                    var listed = opt ? opt.getAttribute('data-price') : null;
                    if (priceInput && listed && priceInput.value === '') priceInput.value = listed;
                }
                totals.refresh();
            });
            toursContainer.addEventListener('input', totals.refresh);
        }

        var addRoomBtn = document.getElementById('add-room');
        if (addRoomBtn) {
            addRoomBtn.addEventListener('click', function () {
                roomsContainer.insertAdjacentHTML('beforeend', roomTpl.innerHTML.replace(/__INDEX__/g, roomRows().length));
                reindexRooms();
            });
        }

        roomsContainer.addEventListener('click', function (e) {
            if (!e.target.closest('.room-remove')) return;
            if (roomRows().length <= 1) { toastr.warning('A booking needs at least one room.'); return; }
            e.target.closest('.room-row').remove();
            reindexRooms();
        });

        roomsContainer.addEventListener('input', renderRoomsSummary);
        roomsContainer.addEventListener('change', renderRoomsSummary);

        form.addEventListener('submit', function (e) {
            var rows = guestRows();
            if (rows.length === 0) { e.preventDefault(); toastr.error('Add at least one guest.'); return; }

            for (var i = 0; i < rows.length; i++) {
                var type = rows[i].getAttribute('data-type');
                if (!rows[i].querySelector('.guest-name').value.trim()) {
                    e.preventDefault(); toastr.error('Enter a name for guest ' + (i + 1) + '.'); return;
                }
                if (type !== 'Adult') {
                    var spec = TYPES[type];
                    var age = parseInt(rows[i].querySelector('.guest-age').value, 10);
                    if (isNaN(age) || age < spec[2] || age > spec[3]) {
                        e.preventDefault();
                        toastr.error(spec[1] + ' — guest ' + (i + 1) + ' needs an age between ' + spec[2] + ' and ' + spec[3] + '.');
                        return;
                    }
                }
            }

            if (!guestsContainer.querySelector('.guest-lead:checked')) {
                e.preventDefault(); toastr.error('Select the lead guest.'); return;
            }
            if (roomRows().length === 0) {
                e.preventDefault(); toastr.error('Add at least one room.'); return;
            }
        });

        // On load (including a redisplay after a failed post) make the roster match the counts.
        if (guestRows().length !== count('Adult') + count('Child') + count('Infant')) syncGuestRows();
        else reindexGuests();
        renderRoomsSummary();
        // Last: the schedule module already ran its own recalc, but the tours card and the
        // first-payment minimum are only reachable from here.
        totals.refresh();
    })();
})();
