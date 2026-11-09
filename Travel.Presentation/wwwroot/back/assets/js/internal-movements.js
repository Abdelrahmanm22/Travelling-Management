// Internal Movements day board: drag a movement type onto a booking card (or use the + buttons),
// fill the one shared popup, and save as JSON. The server re-validates everything; this script only
// gathers the selection and shows the server's messages inside the open modal.
(function () {
    'use strict';

    var cfg = window.movementBoard || {};
    var modalEl = document.getElementById('movement-modal');
    if (!modalEl) return;

    // Created once: this Bootstrap build (5.0.0-beta2) has no getOrCreateInstance, and a new instance
    // per click leaks the previous one's handlers.
    var modal = new bootstrap.Modal(modalEl);

    var els = {
        title: document.getElementById('mv-modal-title'),
        errors: document.getElementById('mv-errors'),
        typeStatic: document.getElementById('mv-type-static'),
        typeSelect: document.getElementById('mv-type-select'),
        bookings: document.getElementById('mv-bookings'),
        time: document.getElementById('mv-time'),
        timeNote: document.getElementById('mv-time-note'),
        driver: document.getElementById('mv-driver'),
        save: document.getElementById('mv-save'),
        del: document.getElementById('mv-delete'),
        template: document.getElementById('mv-booking-template'),
        count: document.getElementById('mv-selected-count')
    };

    var ARRIVAL_LEGS = ['1', '3'];
    var state = { mode: 'create', id: null, typeId: '', leg: '', cards: [], timePrefilled: false, trigger: null, saving: false };
    var dragging = null;

    // ---- selection --------------------------------------------------------------------------

    function selectedCards() {
        return Array.prototype.map.call(document.querySelectorAll('.mv-select:checked'), function (cb) {
            return cb.closest('.mv-card');
        });
    }

    function syncSelection() {
        var n = selectedCards().length;
        if (els.count) els.count.textContent = n;
        document.querySelectorAll('.js-mv-add-selected, .js-mv-clear').forEach(function (b) { b.disabled = n === 0; });
        document.querySelectorAll('.mv-card').forEach(function (card) {
            var cb = card.querySelector('.mv-select');
            card.classList.toggle('is-selected', !!(cb && cb.checked));
        });
    }

    // A drop on a ticked card applies to every ticked card; otherwise to that card alone.
    function targetsFor(card) {
        var cb = card.querySelector('.mv-select');
        return cb && cb.checked ? selectedCards() : [card];
    }

    // ---- drag and drop (native HTML5) ------------------------------------------------------

    document.querySelectorAll('.mv-type').forEach(function (chip) {
        chip.addEventListener('dragstart', function (e) {
            dragging = chip;
            e.dataTransfer.effectAllowed = 'copy';
            e.dataTransfer.setData('text/plain', chip.dataset.typeId);
            document.body.classList.add('mv-dragging');
        });
        chip.addEventListener('dragend', function () {
            dragging = null;
            document.body.classList.remove('mv-dragging');
            document.querySelectorAll('.mv-drop-over').forEach(function (c) { c.classList.remove('mv-drop-over'); });
        });
    });

    document.querySelectorAll('.mv-drop').forEach(function (card) {
        card.addEventListener('dragover', function (e) {
            if (!dragging) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
            card.classList.add('mv-drop-over');
        });
        card.addEventListener('dragleave', function (e) {
            if (!card.contains(e.relatedTarget)) card.classList.remove('mv-drop-over');
        });
        card.addEventListener('drop', function (e) {
            if (!dragging) return;
            e.preventDefault();
            card.classList.remove('mv-drop-over');
            var chip = dragging;
            dragging = null;
            openCreate(targetsFor(card), chip.dataset.typeId, chip.dataset.name, chip.dataset.leg, card);
        });
    });

    // ---- click handlers (the touch / keyboard path) ----------------------------------------

    document.addEventListener('change', function (e) {
        if (e.target.classList && e.target.classList.contains('mv-select')) syncSelection();
    });

    document.addEventListener('click', function (e) {
        var add = e.target.closest('.js-mv-add');
        if (add) { openCreate([add.closest('.mv-card')], '', '', '', add); return; }

        var addSelected = e.target.closest('.js-mv-add-selected');
        if (addSelected) {
            var cards = selectedCards();
            if (cards.length) openCreate(cards, '', '', '', addSelected);
            return;
        }

        if (e.target.closest('.js-mv-clear')) {
            document.querySelectorAll('.mv-select:checked').forEach(function (cb) { cb.checked = false; });
            syncSelection();
            return;
        }

        var edit = e.target.closest('.js-mv-edit');
        if (edit) { openEdit(edit); return; }

        var del = e.target.closest('.js-mv-delete');
        if (del) { confirmDelete(del.dataset.id); }
    });

    // ---- the popup ---------------------------------------------------------------------------

    function cardData(card) {
        var flights = {};
        try { flights = JSON.parse(card.dataset.flights || '{}'); } catch (err) { flights = {}; }
        return {
            id: parseInt(card.dataset.bookingId, 10),
            code: card.dataset.code,
            lead: card.dataset.lead,
            pax: parseInt(card.dataset.pax, 10) || 1,
            flights: flights
        };
    }

    function openCreate(cards, typeId, typeName, leg, trigger) {
        state = { mode: 'create', id: null, typeId: typeId || '', leg: leg || '', cards: cards.map(cardData), timePrefilled: false, trigger: trigger, saving: false };
        els.title.textContent = cards.length > 1 ? 'New movement for ' + cards.length + ' bookings' : 'New movement';
        showType(typeId, typeName);
        renderBookings(null);
        els.time.value = '';
        els.driver.value = '';
        els.del.classList.add('d-none');
        prefillTime();
        show();
    }

    function openEdit(chip) {
        var card = chip.closest('.mv-card');
        state = {
            mode: 'edit', id: chip.dataset.id, typeId: chip.dataset.typeId, leg: chip.dataset.leg || '',
            cards: [cardData(card)], timePrefilled: false, trigger: chip, saving: false
        };
        els.title.textContent = 'Edit movement';
        // Edit can change the type; an inactive type isn't in the select, so add it back for this row.
        ensureTypeOption(chip.dataset.typeId, chip.dataset.typeName, chip.dataset.leg);
        showType('', '');
        els.typeSelect.value = chip.dataset.typeId;
        renderBookings(parseInt(chip.dataset.pax, 10));
        els.time.value = chip.dataset.time || '';
        ensureDriverOption(chip.dataset.driverId);
        els.driver.value = chip.dataset.driverId;
        els.del.classList.remove('d-none');
        refreshFlights();
        show();
    }

    function show() {
        hideErrors();
        els.save.disabled = false;
        modal.show();
    }

    // From a drop the type is fixed; from a + button it is chosen in the select.
    function showType(typeId, typeName) {
        var fixed = !!typeId;
        els.typeStatic.classList.toggle('d-none', !fixed);
        els.typeSelect.classList.toggle('d-none', fixed);
        els.typeStatic.textContent = typeName || '';
        if (!fixed) els.typeSelect.value = state.mode === 'edit' ? state.typeId : '';
    }

    function ensureTypeOption(id, name, leg) {
        if (!id || els.typeSelect.querySelector('option[value="' + id + '"]')) return;
        var opt = document.createElement('option');
        opt.value = id;
        opt.textContent = name + ' (inactive)';
        opt.dataset.leg = leg || '';
        els.typeSelect.appendChild(opt);
    }

    function ensureDriverOption(id) {
        if (!id || els.driver.querySelector('option[value="' + id + '"]')) return;
        var opt = document.createElement('option');
        opt.value = id;
        opt.textContent = 'Current driver (inactive)';
        els.driver.appendChild(opt);
    }

    function renderBookings(editPax) {
        els.bookings.innerHTML = '';
        state.cards.forEach(function (b) {
            var node = els.template.content.firstElementChild.cloneNode(true);
            node.dataset.bookingId = b.id;
            node.querySelector('.mv-modal-code').textContent = b.code;
            node.querySelector('.mv-modal-lead').textContent = b.lead;
            var pax = node.querySelector('.mv-modal-pax input');
            pax.max = b.pax;
            pax.value = editPax || b.pax;
            pax.setAttribute('aria-label', 'Pax for ' + b.code);
            els.bookings.appendChild(node);
        });
        refreshFlights();
    }

    function currentLeg() {
        if (state.typeId && !els.typeStatic.classList.contains('d-none')) return state.leg || '';
        var opt = els.typeSelect.options[els.typeSelect.selectedIndex];
        return opt && opt.value ? (opt.dataset.leg || '') : '';
    }

    function currentTypeId() {
        return !els.typeStatic.classList.contains('d-none') ? state.typeId : els.typeSelect.value;
    }

    // The flight is read live from the booking, shown read-only; a missing one is a warning, never a block.
    function refreshFlights() {
        var leg = currentLeg();
        els.bookings.querySelectorAll('.mv-modal-booking').forEach(function (node, i) {
            var b = state.cards[i];
            var line = node.querySelector('.mv-modal-flight');
            line.innerHTML = '';
            line.className = 'small mv-modal-flight';
            if (!leg) return;

            var f = b.flights[leg];
            if (f && f.recorded) {
                var parts = [];
                if (f.number) parts.push(f.number);
                if (f.time) parts.push(f.time);
                if (f.date !== cfg.date) parts.push(f.dateText);
                line.classList.add('text-muted');
                line.textContent = '✈ ' + parts.join(' · ');
            } else {
                line.classList.add('text-warning');
                line.appendChild(document.createTextNode('No flight recorded on this booking'));
                if (cfg.canOpenBooking) {
                    line.appendChild(document.createTextNode(' — '));
                    var a = document.createElement('a');
                    a.href = cfg.bookingUrl + '/' + b.id;
                    a.target = '_blank';
                    a.textContent = 'open booking';
                    line.appendChild(a);
                }
            }
        });
    }

    // Arrival types wait at landing, so the pickup starts at the flight time.
    function prefillTime() {
        var leg = currentLeg();
        els.timeNote.classList.add('d-none');
        if (ARRIVAL_LEGS.indexOf(leg) === -1) {
            if (state.timePrefilled) { els.time.value = ''; state.timePrefilled = false; }
            return;
        }
        if (els.time.value && !state.timePrefilled) return;

        var time = '';
        state.cards.some(function (b) {
            var f = b.flights[leg];
            if (f && f.time) { time = f.time; return true; }
            return false;
        });
        els.time.value = time;
        state.timePrefilled = !!time;
        els.timeNote.classList.toggle('d-none', !time);
    }

    els.typeSelect.addEventListener('change', function () {
        refreshFlights();
        if (state.mode === 'create') prefillTime();
    });
    els.time.addEventListener('input', function () {
        state.timePrefilled = false;
        els.timeNote.classList.add('d-none');
    });

    // ---- save / delete ----------------------------------------------------------------------

    function showErrors(list) {
        els.errors.innerHTML = '';
        if (list.length === 1) {
            els.errors.textContent = list[0];
        } else {
            var ul = document.createElement('ul');
            ul.className = 'mb-0 ps-3';
            list.forEach(function (m) { var li = document.createElement('li'); li.textContent = m; ul.appendChild(li); });
            els.errors.appendChild(ul);
        }
        els.errors.classList.remove('d-none');
        els.errors.scrollIntoView({ block: 'nearest' });
    }

    function hideErrors() {
        els.errors.classList.add('d-none');
        els.errors.innerHTML = '';
    }

    els.save.addEventListener('click', function () {
        if (state.saving) return;

        var bookings = [];
        els.bookings.querySelectorAll('.mv-modal-booking').forEach(function (node) {
            bookings.push({
                bookingId: parseInt(node.dataset.bookingId, 10),
                pax: parseInt(node.querySelector('.mv-modal-pax input').value, 10) || 0
            });
        });

        var payload = {
            id: state.mode === 'edit' ? parseInt(state.id, 10) : null,
            destinationId: cfg.destinationId,
            date: cfg.date,
            movementTypeId: parseInt(currentTypeId(), 10) || 0,
            driverId: parseInt(els.driver.value, 10) || 0,
            pickupTime: els.time.value,
            bookings: bookings
        };

        // A first pass to save a round trip; the server checks all of this again.
        var errors = [];
        if (!payload.movementTypeId) errors.push('Choose a movement type.');
        if (!payload.pickupTime) errors.push('Enter the pickup time.');
        if (!payload.driverId) errors.push('Choose a driver.');
        bookings.forEach(function (b, i) {
            var max = state.cards[i].pax;
            if (b.pax < 1) errors.push(state.cards[i].code + ': pax must be at least 1.');
            else if (b.pax > max) errors.push(state.cards[i].code + ': pax can’t be more than the booking’s ' + max + '.');
        });
        if (errors.length) { showErrors(errors); return; }

        state.saving = true;
        els.save.disabled = true;
        hideErrors();

        fetch(cfg.saveUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': cfg.token,
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(payload)
        })
            .then(function (res) {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.json();
            })
            .then(function (res) {
                if (res.success) {
                    modal.hide();
                    toastr.success(res.message);
                    setTimeout(function () { location.reload(); }, 600);
                } else {
                    state.saving = false;
                    els.save.disabled = false;
                    showErrors(res.errors && res.errors.length ? res.errors : [res.message]);
                }
            })
            .catch(function () {
                state.saving = false;
                els.save.disabled = false;
                showErrors(['Something went wrong. Reload the page and try again.']);
            });
    });

    els.del.addEventListener('click', function () {
        if (state.mode === 'edit') confirmDelete(state.id);
    });

    function confirmDelete(id) {
        Swal.fire({
            title: 'Delete movement?',
            text: 'It will be removed from the board and the drivers’ sheet.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#f46a6a',
            cancelButtonColor: '#74788d',
            confirmButtonText: 'Yes, delete it'
        }).then(function (result) {
            if (!result.isConfirmed) return;
            $.post(cfg.deleteUrl, { id: id, __RequestVerificationToken: cfg.token })
                .done(function (res) {
                    if (res.success) {
                        modal.hide();
                        toastr.success(res.message);
                        setTimeout(function () { location.reload(); }, 600);
                    } else {
                        toastr.error(res.message);
                    }
                })
                .fail(function () { toastr.error('Something went wrong. Please try again.'); });
        });
    }

    // This Bootstrap build doesn't return focus to the trigger on close.
    modalEl.addEventListener('hidden.bs.modal', function () {
        if (state.trigger && state.trigger.isConnected) state.trigger.focus();
    });

    syncSelection();
})();
