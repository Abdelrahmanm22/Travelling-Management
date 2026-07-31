// Booking Create (live preview) + Edit (editable schedule with live recalculation).
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

    // ─── Create: live preview ────────────────────────────────────────────────
    (function create() {
        var form = document.getElementById('booking-form');
        if (!form) return;

        var pkg = document.getElementById('packageSelect');
        var arrival = document.getElementById('arrivalDate');
        var departure = document.getElementById('departureDate');
        var departureHint = document.getElementById('departureHint');
        var embarkBlock = document.getElementById('embarkBlock');
        var embarkOptions = document.getElementById('embarkOptions');
        var embarkHint = document.getElementById('embarkHint');
        var extAlert = document.getElementById('extensionAlert');
        var previewError = document.getElementById('previewError');
        var previewEmpty = document.getElementById('previewEmpty');
        var previewWrap = document.getElementById('previewWrap');
        var previewBody = document.getElementById('previewBody');
        var url = form.getAttribute('data-preview-url');
        var timer = null;

        function selectedEmbark() {
            var r = embarkOptions.querySelector('input[name="EmbarkDay"]:checked');
            return r ? r.value : '';
        }

        function render(res) {
            previewEmpty.classList.add('d-none');
            previewError.classList.add('d-none');

            if (!res.success) {
                previewError.textContent = res.error || 'Unable to preview this schedule.';
                previewError.classList.remove('d-none');
                previewWrap.classList.add('d-none');
                extAlert.classList.add('d-none');
                return;
            }

            // Departure mode.
            if (res.hasFixedTotal) {
                departure.value = res.departureDate;
                departure.readOnly = true;
                departureHint.textContent = 'Computed from the package total.';
            } else {
                departure.readOnly = false;
                departureHint.textContent = 'Enter the guest’s departure date.';
            }

            // Embark options — one per departure day configured on the cruise destination.
            if (res.embarkOptions && res.embarkOptions.length) {
                var current = selectedEmbark();
                var stillValid = res.embarkOptions.some(function (o) { return o.value === current; });
                if (!stillValid) current = res.defaultEmbark;

                embarkOptions.innerHTML = res.embarkOptions.map(function (o) {
                    var checked = o.value === current ? 'checked' : '';
                    var warn = o.extraNights > 0
                        ? ' <span class="badge bg-warning-subtle text-warning">+' + o.extraNights + ' night' + (o.extraNights > 1 ? 's' : '') + '</span>'
                        : '';
                    return '<div class="form-check">' +
                        '<input class="form-check-input" type="radio" name="EmbarkDay" value="' + o.value + '" id="emb_' + o.value + '" ' + checked + '>' +
                        '<label class="form-check-label" for="emb_' + o.value + '">' + o.text + warn + '</label></div>';
                }).join('');
                embarkHint.textContent = res.embarkOptions.length > 1
                    ? 'Pick which sailing the guest boards.'
                    : 'The only sailing available for this arrival.';
                embarkBlock.classList.remove('d-none');
            } else {
                embarkOptions.innerHTML = '';
                embarkBlock.classList.add('d-none');
            }

            // Extension alert.
            if (res.extraNights > 0) {
                var total = res.stays.reduce(function (n, s) { return n + s.nights; }, 0);
                extAlert.innerHTML = '<i class="bx bx-error-circle me-1"></i> This booking includes ' + res.extraNights +
                    ' extra night' + (res.extraNights > 1 ? 's' : '') + ' (' + total + ' nights instead of ' + (total - res.extraNights) + '). ' +
                    (res.extensionReason || '') + ' Confirm this is charged to the guest.';
                extAlert.classList.remove('d-none');
            } else {
                extAlert.classList.add('d-none');
            }

            // Preview rows.
            previewBody.innerHTML = res.stays.map(function (s, i) {
                var icon = s.isCruise ? '<i class="bx bx-ship me-1"></i>' : '<i class="bx bx-buildings me-1"></i>';
                return '<tr><td>' + (i + 1) + '</td><td>' + icon + s.destinationName + '</td><td>' +
                    fmt(s.checkIn) + '</td><td>' + fmt(s.checkOut) + '</td><td class="text-end">' + s.nights + '</td></tr>';
            }).join('');
            previewWrap.classList.remove('d-none');
        }

        function requestPreview() {
            if (!pkg.value || !arrival.value) {
                previewEmpty.classList.remove('d-none');
                previewWrap.classList.add('d-none');
                previewError.classList.add('d-none');
                extAlert.classList.add('d-none');
                embarkBlock.classList.add('d-none');
                return;
            }
            var data = new URLSearchParams();
            data.append('packageId', pkg.value);
            data.append('arrivalDate', arrival.value);
            if (departure.value) data.append('departureDate', departure.value);
            var emb = selectedEmbark();
            if (emb) data.append('embarkDay', emb);
            data.append('__RequestVerificationToken', token(form));

            fetch(url, { method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { return r.json(); })
                .then(render)
                .catch(function () {
                    previewError.textContent = 'Unable to reach the schedule generator.';
                    previewError.classList.remove('d-none');
                });
        }

        function debounced() { clearTimeout(timer); timer = setTimeout(requestPreview, 250); }

        pkg.addEventListener('change', requestPreview);
        arrival.addEventListener('change', requestPreview);
        departure.addEventListener('change', debounced);
        embarkOptions.addEventListener('change', requestPreview);

        if (pkg.value && arrival.value) requestPreview();
    })();

    // ─── Edit: editable schedule ─────────────────────────────────────────────
    (function edit() {
        var form = document.getElementById('booking-edit-form');
        if (!form) return;

        var container = document.getElementById('stays-container');
        var tpl = document.getElementById('stay-row-template');
        var arrivalInput = document.getElementById('arrivalDate');
        var departureInput = document.getElementById('departureDate');
        var banner = document.getElementById('schedule-banner');
        var manualFlag = document.getElementById('isManuallyEdited');

        var hasFixed = form.getAttribute('data-fixed') === 'true';
        var pkgTotal = parseInt(form.getAttribute('data-total'), 10);
        var baseId = form.getAttribute('data-base');

        function rows() { return Array.prototype.slice.call(container.querySelectorAll('.stay-row')); }
        function arrival() { return arrivalInput.value; }

        function markEdited(row) {
            manualFlag.value = 'true';
            if (row) {
                var ov = row.querySelector('.stay-overridden');
                if (ov) ov.value = 'true';
            }
        }

        function recalc() {
            var cursor = arrival();
            var total = 0;
            rows().forEach(function (row, i) {
                row.querySelector('.stay-index').textContent = i + 1;
                row.querySelectorAll('select, input').forEach(function (f) {
                    if (f.name) f.name = f.name.replace(/Stays\[\d+\]/, 'Stays[' + i + ']');
                });
                var nights = parseInt(row.querySelector('.stay-nights').value, 10) || 0;
                var checkin = row.querySelector('.stay-checkin');
                if (cursor) { checkin.value = cursor; cursor = addDays(cursor, nights); }
                total += nights;
            });
            if (arrival()) departureInput.value = addDays(arrival(), total);
            updateBanner(total);
        }

        function updateBanner(total) {
            var warnings = [];
            if (hasFixed && !isNaN(pkgTotal) && total !== pkgTotal) {
                var extra = total - pkgTotal;
                warnings.push('Schedule is ' + total + ' nights; package total is ' + pkgTotal +
                    ' (' + (extra > 0 ? '+' + extra : extra) + ').');
            }
            var last = rows()[rows().length - 1];
            if (last && baseId) {
                var lastDest = last.querySelector('.stay-destination').value;
                if (lastDest !== baseId) warnings.push('Last night is not at the base destination.');
            }

            banner.classList.remove('d-none', 'alert-warning', 'alert-success', 'alert-info');
            if (warnings.length) {
                banner.classList.add('alert-warning');
                banner.innerHTML = '<i class="bx bx-error-circle me-1"></i> ' + warnings.join(' ') +
                    ' <span class="text-muted">You can still save — confirm it’s intentional.</span>';
            } else if (hasFixed) {
                banner.classList.add('alert-success');
                banner.innerHTML = '<i class="bx bx-check-circle me-1"></i> Schedule matches the package (' + total + ' nights), last night at base.';
            } else {
                banner.classList.add('alert-info');
                banner.innerHTML = 'Schedule is ' + total + ' nights.';
            }
        }

        function addRow() {
            var index = rows().length;
            container.insertAdjacentHTML('beforeend', tpl.innerHTML.replace(/__INDEX__/g, index));
            markEdited();
            recalc();
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
            if (e.target.classList.contains('stay-nights')) { markEdited(e.target.closest('.stay-row')); recalc(); }
            else if (e.target.matches('.stay-destination, input')) { markEdited(e.target.closest('.stay-row')); updateBanner(rows().reduce(function (n, r) { return n + (parseInt(r.querySelector('.stay-nights').value, 10) || 0); }, 0)); }
        });
        container.addEventListener('change', function (e) {
            if (e.target.classList.contains('stay-destination')) { markEdited(e.target.closest('.stay-row')); recalc(); }
        });

        document.getElementById('add-stay').addEventListener('click', addRow);
        arrivalInput.addEventListener('change', recalc);

        // Regenerate — discards manual edits behind a confirm.
        document.getElementById('regenerate-btn').addEventListener('click', function () {
            Swal.fire({
                title: 'Regenerate schedule?',
                text: 'This rebuilds the schedule from the package and discards any manual changes.',
                icon: 'warning', showCancelButton: true,
                confirmButtonColor: '#f1b44c', cancelButtonColor: '#74788d', confirmButtonText: 'Yes, regenerate'
            }).then(function (r) { if (r.isConfirmed) document.getElementById('regenerate-form').submit(); });
        });

        // Hard-block structural errors on submit.
        form.addEventListener('submit', function (e) {
            var rs = rows();
            if (rs.length === 0) { e.preventDefault(); toastr.error('The schedule must have at least one stay.'); return; }
            for (var i = 0; i < rs.length; i++) {
                var nights = parseInt(rs[i].querySelector('.stay-nights').value, 10);
                if (!nights || nights < 1) { e.preventDefault(); toastr.error('Every stay must be at least 1 night.'); return; }
                if (!rs[i].querySelector('.stay-destination').value) { e.preventDefault(); toastr.error('Every stay needs a destination.'); return; }
            }
        });

        recalc();
    })();
})();
