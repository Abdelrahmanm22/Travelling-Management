/*
 * The contract page (BookingContract/Edit). The page state is one JSON document — the same shape the
 * server renders and stores — and the editor cards are drawn from it. Every change updates the state
 * and refreshes the PDF preview ~800 ms after the last keystroke.
 *
 * CPF, RG and address live only in `party` below: they are sent with Preview and Generate and never
 * saved — not in the draft, not anywhere.
 */
(function () {
    'use strict';

    var stateEl = document.getElementById('contract-state');
    if (!stateEl) return;

    var boot = JSON.parse(stateEl.textContent);
    var token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    var urls = boot.urls;
    var links = boot.links || {};
    var locked = !!boot.locked;

    var S = {
        doc: boot.document,
        basis: boot.basis,
        warnings: boot.warnings || [],
        stale: new Set(boot.stale || []),
        party: { cpf: '', rg: '', address: '' },
        isPriced: !!boot.isPriced,
    };
    var dirty = false;

    var SECTION_LABELS = {
        passengers: 'passengers',
        payment: 'payment',
        schedule: 'schedule (nights)',
        lists: 'included / not included',
        accommodation: 'accommodation',
    };
    var TYPES = ['Adulto', 'Criança', 'Bebê'];

    var CARDS = [
        { key: 'party', num: 1, title: 'Contracting party', icon: 'bx-user' },
        { key: 'passengers', num: 2, title: 'Passengers', icon: 'bx-group', section: 'passengers' },
        { key: 'payment', num: 3, title: 'Payment', icon: 'bx-dollar-circle', section: 'payment' },
        { key: 'trip', num: 4, title: 'Trip', icon: 'bx-map-alt' },
        { key: 'schedule', num: 5, title: 'Schedule', icon: 'bx-calendar', section: 'schedule' },
        { key: 'lists', num: 6, title: 'Included / Not included', icon: 'bx-list-check', section: 'lists' },
        { key: 'accommodation', num: 7, title: 'Accommodation', icon: 'bx-building-house', section: 'accommodation' },
        { key: 'texts', num: 8, title: 'Important & Documents', icon: 'bx-info-circle' },
    ];

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    function esc(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function pad(n) { return n < 10 ? '0' + n : String(n); }

    function totalNights() {
        return (S.doc.schedule || []).reduce(function (sum, s) { return sum + Math.max(0, s.nights || 0); }, 0);
    }

    function tripLength() {
        var n = totalNights(), d = n + 1;
        return d + (d === 1 ? ' dia' : ' dias') + ' / ' + n + (n === 1 ? ' noite' : ' noites');
    }

    function money(value) {
        if (value === null || value === undefined || value === '') return '';
        return 'US$' + Number(value).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    /** Reads or writes a dotted path such as "doc.payment.lines.2.amount". */
    function setPath(path, value) {
        var parts = path.split('.');
        var target = parts[0] === 'party' ? S.party : S.doc;
        for (var i = 1; i < parts.length - 1; i++) target = target[parts[i]];
        target[parts[parts.length - 1]] = value;
    }

    function move(list, index, delta) {
        var to = index + delta;
        if (to < 0 || to >= list.length) return;
        var item = list.splice(index, 1)[0];
        list.splice(to, 0, item);
    }

    function input(bind, value, opts) {
        opts = opts || {};
        var type = opts.type || 'text';
        var attrs = ' data-bind="' + bind + '" data-kind="' + (opts.kind || 'text') + '"';
        if (opts.placeholder) attrs += ' placeholder="' + esc(opts.placeholder) + '"';
        if (opts.label) attrs += ' aria-label="' + esc(opts.label) + '"';
        if (opts.id) attrs += ' id="' + opts.id + '"';
        if (locked) attrs += ' disabled';
        if (type === 'textarea')
            return '<textarea class="form-control form-control-sm" rows="' + (opts.rows || 3) + '"' + attrs + '>' + esc(value) + '</textarea>';
        var extra = type === 'number' ? ' step="0.01" inputmode="decimal"' : '';
        return '<input type="' + type + '" class="form-control form-control-sm' + (opts.cls ? ' ' + opts.cls : '') + '"' + extra + attrs + ' value="' + esc(value == null ? '' : value) + '" autocomplete="off" />';
    }

    function iconButton(act, args, icon, label, cls) {
        return '<button type="button" class="btn btn-light btn-sm contract-icon-btn ' + (cls || '') + '" data-act="' + act + '"' +
            (args ? ' data-args="' + args + '"' : '') + ' aria-label="' + esc(label) + '" title="' + esc(label) + '"' + (locked ? ' disabled' : '') +
            '><i class="bx ' + icon + '"></i></button>';
    }

    function rowTools(act, index, length) {
        return '<div class="contract-row-tools">' +
            iconButton(act + '-up', index, 'bx-chevron-up', 'Move up', index === 0 ? 'invisible' : '') +
            iconButton(act + '-down', index, 'bx-chevron-down', 'Move down', index === length - 1 ? 'invisible' : '') +
            iconButton(act + '-remove', index, 'bx-trash', 'Remove', 'text-danger') +
            '</div>';
    }

    function addButton(act, label, args) {
        return '<button type="button" class="btn btn-outline-primary btn-sm mt-2" data-act="' + act + '"' +
            (args !== undefined ? ' data-args="' + args + '"' : '') + (locked ? ' disabled' : '') +
            '><i class="bx bx-plus me-1"></i>' + esc(label) + '</button>';
    }

    function errorSlot(key) {
        return '<div class="invalid-feedback d-block contract-error" data-error="' + key + '"></div>';
    }

    // ── Card bodies ─────────────────────────────────────────────────────────────────────────────

    function staleBanner(section) {
        if (!section || !S.stale.has(section)) return '';
        return '<div class="alert alert-warning d-flex flex-wrap align-items-center gap-2 py-2 small" role="alert">' +
            '<i class="bx bx-error"></i><div class="flex-grow-1">The booking changed since this draft was saved: ' +
            esc(SECTION_LABELS[section]) + '.</div>' +
            '<button type="button" class="btn btn-warning btn-sm" data-act="refresh" data-args="' + section + '"' + (locked ? ' disabled' : '') +
            '><i class="bx bx-revision me-1"></i>Refresh from booking</button></div>';
    }

    var bodies = {
        party: function () {
            return '<p class="small text-muted mb-2"><i class="bx bx-lock-alt me-1"></i>CPF, RG and address are not saved — type them each time.</p>' +
                '<div class="row g-2">' +
                '<div class="col-12"><label class="form-label small mb-1" for="cp-name">Name</label>' +
                input('doc.partyName', S.doc.partyName, { id: 'cp-name' }) + errorSlot('partyName') + '</div>' +
                '<div class="col-sm-6"><label class="form-label small mb-1" for="cp-cpf">CPF</label>' +
                input('party.cpf', S.party.cpf, { id: 'cp-cpf', placeholder: '000.000.000-00' }) + errorSlot('cpf') + '</div>' +
                '<div class="col-sm-6"><label class="form-label small mb-1" for="cp-rg">RG</label>' +
                input('party.rg', S.party.rg, { id: 'cp-rg' }) + errorSlot('rg') + '</div>' +
                '<div class="col-12"><label class="form-label small mb-1" for="cp-address">Address</label>' +
                input('party.address', S.party.address, { id: 'cp-address', placeholder: 'Rua…, nº – Bairro, Cidade/UF – CEP' }) + errorSlot('address') + '</div>' +
                '</div>';
        },

        passengers: function () {
            var list = S.doc.passengers;
            var html = staleBanner('passengers');
            if (!list.length) html += '<p class="small text-muted mb-1">No passengers.</p>';
            list.forEach(function (p, i) {
                var options = TYPES.concat(TYPES.indexOf(p.typeLabel) < 0 && p.typeLabel ? [p.typeLabel] : [])
                    .map(function (t) { return '<option' + (t === p.typeLabel ? ' selected' : '') + '>' + esc(t) + '</option>'; }).join('');
                html += '<div class="contract-row">' +
                    '<div class="flex-grow-1">' + input('doc.passengers.' + i + '.name', p.name, { label: 'Passenger name' }) + '</div>' +
                    '<select class="form-select form-select-sm contract-type-select" data-bind="doc.passengers.' + i + '.typeLabel" data-kind="text" aria-label="Passenger type"' + (locked ? ' disabled' : '') + '>' + options + '</select>' +
                    rowTools('passenger', i, list.length) + '</div>';
            });
            return html + addButton('passenger-add', 'Add passenger');
        },

        payment: function () {
            var p = S.doc.payment;
            var html = staleBanner('payment');
            html += '<label class="form-label small mb-1">Title</label>' + input('doc.payment.title', p.title, { label: 'Payment title' });
            html += '<div class="small text-muted mt-3 mb-1">Lines</div>';
            p.lines.forEach(function (line, i) {
                html += '<div class="contract-row">' +
                    '<div class="flex-grow-1">' + input('doc.payment.lines.' + i + '.label', line.label, { label: 'Line' }) + '</div>' +
                    '<div class="contract-amount">' + input('doc.payment.lines.' + i + '.amount', line.amount, { type: 'number', kind: 'money', label: 'Amount (US$)' }) + '</div>' +
                    rowTools('line', i, p.lines.length) + '</div>';
            });
            html += addButton('line-add', 'Add line');
            html += '<div class="row g-2 mt-2">' +
                '<div class="col-sm-4"><label class="form-label small mb-1">TOTAL (US$)</label>' + input('doc.payment.total', p.total, { type: 'number', kind: 'money' }) + '</div>' +
                '<div class="col-sm-4"><label class="form-label small mb-1">Entrada — paid (US$)</label>' + input('doc.payment.paid', p.paid, { type: 'number', kind: 'money0' }) + '</div>' +
                '<div class="col-sm-4"><label class="form-label small mb-1">Saldo devedor (US$)</label>' + input('doc.payment.balance', p.balance, { type: 'number', kind: 'money' }) + '</div>' +
                '</div>';
            html += '<p class="small text-muted mt-1 mb-2">Editing these changes the contract only, never the booking.</p>';
            html += '<label class="form-label small mb-1">Extra terms (optional)</label>' +
                input('doc.payment.extraText', p.extraText, { type: 'textarea', rows: 2, placeholder: 'e.g. Valores por pessoa em quarto duplo.' });
            return html;
        },

        trip: function () {
            return '<div class="row g-2">' +
                '<div class="col-sm-5"><label class="form-label small mb-1">Group</label>' + input('doc.groupLabel', S.doc.groupLabel, { placeholder: 'AGOSTO/2026' }) + '</div>' +
                '<div class="col-sm-7"><label class="form-label small mb-1">Trip title</label>' + input('doc.tripTitle', S.doc.tripTitle) + '</div>' +
                '<div class="col-sm-5"><label class="form-label small mb-1">Contract date</label>' + input('doc.date', S.doc.date, { type: 'date' }) + '</div>' +
                '</div>' +
                '<p class="small text-muted mt-2 mb-0">Prints as <span class="fw-bold">VIAGEM EGITO: <span data-live="tripTitle">' + esc(S.doc.tripTitle) +
                '</span> <span data-live="tripLength">' + tripLength() + '</span></span> — the nights come from the booking.</p>';
        },

        schedule: function () {
            var html = staleBanner('schedule');
            html += '<p class="small text-muted mb-2">Trip length: <span class="fw-bold" data-live="tripLength">' + tripLength() + '</span>. ' +
                'Day numbers update as you add, remove or reorder days.</p>';
            html += errorSlot('schedule');
            var number = 0;
            S.doc.schedule.forEach(function (section, si) {
                html += '<div class="contract-section">' +
                    '<div class="contract-row">' +
                    '<div class="flex-grow-1">' + input('doc.schedule.' + si + '.heading', section.heading, { label: 'Section heading', placeholder: 'Programação …' }) + '</div>' +
                    '<span class="badge badge-tag text-nowrap">' + (section.nights || 0) + (section.nights === 1 ? ' night' : ' nights') + '</span>' +
                    iconButton('section-remove', si, 'bx-trash', 'Remove section', 'text-danger') +
                    '</div>' +
                    '<div class="contract-days" data-section="' + si + '">';
                section.days.forEach(function (day, di) {
                    number++;
                    html += '<div class="contract-day' + (day.isInserted ? ' is-inserted' : '') + '" data-ref="' + si + ':' + di + '">' +
                        '<div class="contract-row">' +
                        '<span class="contract-drag" aria-hidden="true"><i class="mdi mdi-drag-vertical"></i></span>' +
                        '<span class="contract-day-number">Dia ' + pad(number) + '</span>' +
                        '<div class="flex-grow-1">' + input('doc.schedule.' + si + '.days.' + di + '.title', day.title, { label: 'Day ' + number + ' title' }) + '</div>' +
                        rowTools('day', si + ':' + di, section.days.length) +
                        '</div>' +
                        (day.isInserted ? '<span class="badge bg-warning-subtle text-warning mb-1">added — extra night</span>' : '') +
                        input('doc.schedule.' + si + '.days.' + di + '.body', day.body, { type: 'textarea', rows: 3, label: 'Day ' + number + ' text' }) +
                        '</div>';
                });
                html += '</div>' + addButton('day-add', 'Add day', si) + '</div>';
            });
            if (!S.doc.schedule.length) html += '<p class="small text-muted">No sections yet.</p>';
            return html + '<div>' + addButton('section-add', 'Add section') + '</div>' +
                '<p class="small text-muted mt-2 mb-0">A line starting with "* " prints as a note. Blank lines separate paragraphs.</p>';
        },

        lists: function () {
            var html = staleBanner('lists');
            [['includes', 'Pacote inclui'], ['excludes', 'Não está incluso']].forEach(function (pair) {
                var key = pair[0], list = S.doc[key];
                html += '<div class="fw-bold small mt-2 mb-1">' + pair[1] + '</div>';
                if (!list.length) html += '<p class="small text-muted mb-1">Empty.</p>';
                list.forEach(function (text, i) {
                    html += '<div class="contract-row"><div class="flex-grow-1">' + input('doc.' + key + '.' + i, text, { label: pair[1] }) + '</div>' +
                        rowTools(key, i, list.length) + '</div>';
                });
                html += addButton(key + '-add', 'Add line');
            });
            return html;
        },

        accommodation: function () {
            var list = S.doc.accommodation;
            var html = staleBanner('accommodation');
            if (!list.length) html += '<p class="small text-muted mb-1">No accommodation lines.</p>';
            list.forEach(function (a, i) {
                html += '<div class="contract-row">' +
                    '<div class="contract-accommodation-dest">' + input('doc.accommodation.' + i + '.destination', a.destination, { label: 'Destination' }) + '</div>' +
                    '<div class="flex-grow-1">' + input('doc.accommodation.' + i + '.text', a.text, { label: 'Accommodation' }) + '</div>' +
                    rowTools('accommodation', i, list.length) + '</div>';
            });
            return html + addButton('accommodation-add', 'Add line');
        },

        texts: function () {
            return '<label class="form-label small mb-1">Importante</label>' +
                input('doc.important', S.doc.important, { type: 'textarea', rows: 5 }) +
                '<label class="form-label small mb-1 mt-3">Documentos Requeridos</label>' +
                input('doc.documents', S.doc.documents, { type: 'textarea', rows: 4 });
        },
    };

    // ── Rendering ───────────────────────────────────────────────────────────────────────────────

    var editor = document.getElementById('contract-editor');

    function renderShell() {
        editor.innerHTML = CARDS.map(function (c) {
            return '<div class="card contract-card" data-card="' + c.key + '">' +
                '<div class="card-header bg-transparent">' +
                '<button class="btn btn-link p-0 text-reset contract-card-toggle" type="button" data-bs-toggle="collapse" ' +
                'data-bs-target="#cc-' + c.key + '" aria-expanded="true" aria-controls="cc-' + c.key + '">' +
                '<i class="bx bx-chevron-down me-1"></i><i class="bx ' + c.icon + ' me-1"></i><span class="fw-bold">' + c.num + '. ' + c.title + '</span>' +
                '<span class="contract-stale-dot ms-2 d-none" data-stale-dot="' + (c.section || '') + '">changed</span>' +
                '</button></div>' +
                '<div id="cc-' + c.key + '" class="collapse show"><div class="card-body" data-body="' + c.key + '"></div></div></div>';
        }).join('');
    }

    var drake = null;

    function renderCard(key) {
        var body = editor.querySelector('[data-body="' + key + '"]');
        if (!body) return;
        body.innerHTML = bodies[key]();
        if (key === 'schedule') initDrag();
        if (key === 'schedule' || key === 'trip') refreshLive();
        refreshStaleMarkers();
    }

    function renderAll() {
        CARDS.forEach(function (c) { renderCard(c.key); });
        renderWarnings();
    }

    function refreshLive() {
        document.querySelectorAll('[data-live="tripLength"]').forEach(function (el) { el.textContent = tripLength(); });
        document.querySelectorAll('[data-live="tripTitle"]').forEach(function (el) { el.textContent = S.doc.tripTitle; });
    }

    function refreshStaleMarkers() {
        document.querySelectorAll('[data-stale-dot]').forEach(function (el) {
            el.classList.toggle('d-none', !(el.dataset.staleDot && S.stale.has(el.dataset.staleDot)));
        });
        var summary = document.getElementById('contract-stale-summary');
        if (!S.stale.size) { summary.innerHTML = ''; return; }
        var names = Array.from(S.stale).map(function (s) { return SECTION_LABELS[s] || s; });
        summary.innerHTML = '<div class="alert alert-warning d-flex align-items-center gap-2" role="alert"><i class="bx bx-error font-size-18"></i>' +
            '<div>The booking changed since this draft was saved: <span class="fw-bold">' + esc(names.join(', ')) +
            '</span>. Each section shows a <span class="fw-bold">Refresh from booking</span> button — nothing is changed until you use it.</div></div>';
    }

    function renderWarnings() {
        var box = document.getElementById('contract-warnings');
        box.innerHTML = S.warnings.map(function (w) {
            var link = '';
            if (w.section === 'payment' && links.bookingEdit) link = ' <a href="' + links.bookingEdit + '">Edit the booking</a>';
            else if (w.section === 'schedule' && links.packageProgram) link = ' <a href="' + links.packageProgram + '" target="_blank">Open the contract program</a>';
            else if (w.section === 'accommodation' && links.destinations) link = ' <a href="' + links.destinations + '" target="_blank">Setup ▸ Destinations</a>';
            var danger = w.level === 'danger';
            return '<div class="alert ' + (danger ? 'alert-danger' : 'alert-warning') + ' d-flex align-items-start gap-2 py-2" role="alert">' +
                '<i class="bx ' + (danger ? 'bx-error-circle' : 'bx-info-circle') + ' font-size-18"></i><div>' + esc(w.message) + link + '</div></div>';
        }).join('');
    }

    function initDrag() {
        if (drake) { drake.destroy(); drake = null; }
        if (locked || typeof dragula !== 'function') return;
        var containers = Array.prototype.slice.call(editor.querySelectorAll('.contract-days'));
        drake = dragula(containers, {
            moves: function (el, source, handle) { return !!(handle && handle.closest('.contract-drag')); },
        });
        drake.on('drop', function () {
            // Rebuild every section's days from the DOM order, then redraw so the numbers follow.
            var pool = S.doc.schedule.map(function (s) { return s.days.slice(); });
            containers.forEach(function (box) {
                var si = Number(box.dataset.section);
                S.doc.schedule[si].days = Array.prototype.map.call(box.querySelectorAll('.contract-day'), function (el) {
                    var ref = el.dataset.ref.split(':');
                    return pool[Number(ref[0])][Number(ref[1])];
                });
            });
            changed();
            setTimeout(function () { renderCard('schedule'); }, 0);
        });
    }

    // ── Editing ─────────────────────────────────────────────────────────────────────────────────

    function changed() {
        dirty = true;
        schedulePreview();
    }

    editor.addEventListener('input', onEdit);
    editor.addEventListener('change', onEdit);

    function onEdit(e) {
        var el = e.target;
        if (!el.dataset || !el.dataset.bind) return;
        var kind = el.dataset.kind;
        var value = el.value;
        if (kind === 'money') value = value === '' ? null : Number(value);
        else if (kind === 'money0') value = value === '' ? 0 : Number(value);
        if (e.type === 'input' || el.tagName === 'SELECT') {
            setPath(el.dataset.bind, value);
            var key = el.dataset.bind.split('.').pop();
            var err = editor.querySelector('[data-error="' + errorKeyFor(el.dataset.bind) + '"]');
            if (err) err.textContent = '';
            if (key === 'tripTitle') refreshLive();
            changed();
        }
    }

    function errorKeyFor(bind) {
        if (bind === 'doc.partyName') return 'partyName';
        if (bind.indexOf('party.') === 0) return bind.substring(6);
        return '';
    }

    var actions = {
        'passenger-add': function () { S.doc.passengers.push({ name: '', typeLabel: 'Adulto' }); return 'passengers'; },
        'line-add': function () { S.doc.payment.lines.push({ label: '', amount: null }); return 'payment'; },
        'includes-add': function () { S.doc.includes.push(''); return 'lists'; },
        'excludes-add': function () { S.doc.excludes.push(''); return 'lists'; },
        'accommodation-add': function () { S.doc.accommodation.push({ destination: '', text: '' }); return 'accommodation'; },
        'section-add': function () { S.doc.schedule.push({ heading: '', nights: 0, days: [] }); return 'schedule'; },
        'day-add': function (si) { S.doc.schedule[Number(si)].days.push({ title: '', body: '', isInserted: false }); return 'schedule'; },
        'section-remove': function (si) {
            var section = S.doc.schedule[Number(si)];
            if (section.days.length && !window.confirm('Remove this section and its ' + section.days.length + ' day(s)?')) return null;
            S.doc.schedule.splice(Number(si), 1);
            return 'schedule';
        },
    };

    function listActions(prefix, getList, card) {
        actions[prefix + '-up'] = function (i) { move(getList(), Number(i), -1); return card; };
        actions[prefix + '-down'] = function (i) { move(getList(), Number(i), 1); return card; };
        actions[prefix + '-remove'] = function (i) { getList().splice(Number(i), 1); return card; };
    }
    listActions('passenger', function () { return S.doc.passengers; }, 'passengers');
    listActions('line', function () { return S.doc.payment.lines; }, 'payment');
    listActions('includes', function () { return S.doc.includes; }, 'lists');
    listActions('excludes', function () { return S.doc.excludes; }, 'lists');
    listActions('accommodation', function () { return S.doc.accommodation; }, 'accommodation');

    function dayArgs(args) { var p = String(args).split(':'); return { days: S.doc.schedule[Number(p[0])].days, i: Number(p[1]) }; }
    actions['day-up'] = function (a) { var d = dayArgs(a); move(d.days, d.i, -1); return 'schedule'; };
    actions['day-down'] = function (a) { var d = dayArgs(a); move(d.days, d.i, 1); return 'schedule'; };
    actions['day-remove'] = function (a) { var d = dayArgs(a); d.days.splice(d.i, 1); return 'schedule'; };

    editor.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-act]');
        if (!btn || locked) return;
        var act = btn.dataset.act;
        if (act === 'refresh') { refreshSection(btn.dataset.args); return; }
        var fn = actions[act];
        if (!fn) return;
        var card = fn(btn.dataset.args);
        if (!card) return;
        renderCard(card);
        if (card === 'schedule') renderCard('trip');
        changed();
    });

    // ── Server calls ────────────────────────────────────────────────────────────────────────────

    function post(url, body) {
        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token,
                'X-Requested-With': 'XMLHttpRequest',
            },
            body: JSON.stringify(body),
        });
    }

    function isPdf(res) { return (res.headers.get('content-type') || '').indexOf('application/pdf') >= 0; }

    function request(withParty) {
        var body = { bookingId: boot.bookingId, document: S.doc, basis: S.basis };
        if (withParty) body.party = S.party;
        return body;
    }

    function showErrors(errors) {
        editor.querySelectorAll('[data-error]').forEach(function (el) { el.textContent = ''; });
        var first = null;
        Object.keys(errors || {}).forEach(function (key) {
            var el = editor.querySelector('[data-error="' + key + '"]');
            if (!el) return;
            el.textContent = errors[key];
            if (!first) first = el;
        });
        if (first) {
            var collapse = first.closest('.collapse');
            if (collapse && !collapse.classList.contains('show')) collapse.classList.add('show');
            first.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    // Preview — debounced, and only the newest response is shown.
    var frame = document.getElementById('contract-preview-frame');
    var previewState = document.getElementById('contract-preview-state');
    var previewError = document.getElementById('contract-preview-error');
    var wide = window.matchMedia('(min-width: 1200px)');
    var timer = null, seq = 0, frameUrl = null;

    function schedulePreview() {
        if (locked || !wide.matches) return;
        clearTimeout(timer);
        previewState.textContent = 'Changes pending…';
        timer = setTimeout(refreshPreview, 800);
    }

    function fetchPreview() {
        return post(urls.preview, request(true)).then(function (res) {
            if (isPdf(res)) return res.blob();
            return res.json().then(function (data) { throw new Error(data.message || 'The preview could not be rendered.'); });
        });
    }

    function refreshPreview() {
        var mine = ++seq;
        previewState.textContent = 'Rendering…';
        fetchPreview().then(function (blob) {
            if (mine !== seq) return;
            if (frameUrl) URL.revokeObjectURL(frameUrl);
            frameUrl = URL.createObjectURL(blob);
            // No thumbnail pane: the preview column is half the screen and the page needs all of it.
            frame.src = frameUrl + '#navpanes=0&view=FitH';
            previewError.classList.add('d-none');
            previewState.textContent = 'Up to date.';
        }).catch(function (err) {
            if (mine !== seq) return;
            previewError.textContent = err.message || 'The preview could not be rendered.';
            previewError.classList.remove('d-none');
            previewState.textContent = '';
        });
    }

    wide.addEventListener && wide.addEventListener('change', function (e) { if (e.matches) schedulePreview(); });

    var openBtn = document.getElementById('contract-preview-open');
    if (openBtn) openBtn.addEventListener('click', function () {
        // Open the tab synchronously, or a phone's popup blocker eats it after the await.
        var tab = window.open('', '_blank');
        openBtn.disabled = true;
        fetchPreview().then(function (blob) {
            var url = URL.createObjectURL(blob);
            if (tab) tab.location.href = url; else window.location.href = url;
        }).catch(function (err) {
            if (tab) tab.close();
            toastr.error(err.message);
        }).finally(function () { openBtn.disabled = false; });
    });

    function busy(btn, on) {
        if (!btn) return;
        btn.disabled = on;
        if (on) { btn.dataset.html = btn.innerHTML; btn.innerHTML = '<i class="bx bx-loader-alt bx-spin me-1"></i>' + btn.textContent.trim(); }
        else if (btn.dataset.html) btn.innerHTML = btn.dataset.html;
    }

    var saveBtn = document.getElementById('contract-save');
    var rebuildBtn = document.getElementById('contract-rebuild');
    var generateBtn = document.getElementById('contract-generate');

    saveBtn && saveBtn.addEventListener('click', function () {
        busy(saveBtn, true);
        post(urls.save, request(false)).then(function (r) { return r.json(); }).then(function (res) {
            if (res.success) { dirty = false; toastr.success(res.message); }
            else toastr.error(res.message);
        }).catch(function () { toastr.error('Something went wrong. Please try again.'); })
            .finally(function () { busy(saveBtn, false); });
    });

    function confirmBox(title, text, confirmText) {
        if (window.Swal) {
            return Swal.fire({
                title: title, text: text, icon: 'warning', showCancelButton: true,
                confirmButtonColor: '#4f46e5', cancelButtonColor: '#74788d', confirmButtonText: confirmText,
            }).then(function (r) { return r.isConfirmed || r.value; });
        }
        return Promise.resolve(window.confirm(title + '\n\n' + text));
    }

    rebuildBtn && rebuildBtn.addEventListener('click', function () {
        confirmBox('Rebuild from booking?', 'Every edit on this page is replaced with a fresh contract built from the booking. The saved draft is replaced when you next save or generate.', 'Rebuild')
            .then(function (ok) {
                if (!ok) return;
                busy(rebuildBtn, true);
                post(urls.rebuild, { bookingId: boot.bookingId, section: '' }).then(function (r) { return r.json(); }).then(function (res) {
                    if (!res.success) { toastr.error(res.message); return; }
                    S.doc = res.document;
                    S.basis = res.basis;
                    S.warnings = res.warnings || [];
                    S.stale.clear();
                    renderAll();
                    changed();
                    toastr.success('Rebuilt from the booking. Save the draft to keep it.');
                }).catch(function () { toastr.error('Something went wrong. Please try again.'); })
                    .finally(function () { busy(rebuildBtn, false); });
            });
    });

    function refreshSection(section) {
        confirmBox('Refresh ' + (SECTION_LABELS[section] || section) + '?',
            'This section is replaced with what the booking says now. Your edits to it are lost; nothing else changes.', 'Refresh')
            .then(function (ok) {
                if (!ok) return;
                post(urls.refresh, { bookingId: boot.bookingId, section: section }).then(function (r) { return r.json(); }).then(function (res) {
                    if (!res.success) { toastr.error(res.message); return; }
                    var fresh = res.document;
                    if (section === 'passengers') S.doc.passengers = fresh.passengers;
                    else if (section === 'payment') { var extra = S.doc.payment.extraText; S.doc.payment = fresh.payment; S.doc.payment.extraText = extra; }
                    else if (section === 'schedule') S.doc.schedule = fresh.schedule;
                    else if (section === 'lists') { S.doc.includes = fresh.includes; S.doc.excludes = fresh.excludes; }
                    else if (section === 'accommodation') S.doc.accommodation = fresh.accommodation;
                    S.basis[section] = res.basis[section];
                    S.stale.delete(section);
                    var card = CARDS.filter(function (c) { return c.section === section; })[0];
                    renderCard(card ? card.key : section);
                    if (section === 'schedule') renderCard('trip');
                    changed();
                }).catch(function () { toastr.error('Something went wrong. Please try again.'); });
            });
    }

    function fileNameFrom(res) {
        var cd = res.headers.get('content-disposition') || '';
        var star = /filename\*=UTF-8''([^;]+)/i.exec(cd);
        if (star) return decodeURIComponent(star[1]);
        var plain = /filename="?([^";]+)"?/i.exec(cd);
        return plain ? plain[1] : 'Contrato_' + boot.bookingCode + '.pdf';
    }

    generateBtn && generateBtn.addEventListener('click', function () {
        busy(generateBtn, true);
        showErrors({});
        post(urls.generate, request(true)).then(function (res) {
            if (isPdf(res)) {
                var name = fileNameFrom(res);
                return res.blob().then(function (blob) {
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url; a.download = name;
                    document.body.appendChild(a); a.click(); a.remove();
                    setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
                    dirty = false;
                    markGenerated();
                    toastr.success('Contract generated and saved on the booking.');
                });
            }
            return res.json().then(function (data) {
                showErrors(data.errors);
                toastr.error(data.message || 'The contract could not be generated.');
            });
        }).catch(function () { toastr.error('Something went wrong. Please try again.'); })
            .finally(function () {
                busy(generateBtn, false);
                if (!S.isPriced) generateBtn.disabled = true;
            });
    });

    function markGenerated() {
        var line = document.getElementById('contract-status-line');
        if (line) line.innerHTML = '<span><i class="bx bx-check-circle text-success me-1"></i>Generated just now</span>';
        if (!document.getElementById('contract-download')) {
            var a = document.createElement('a');
            a.id = 'contract-download';
            a.href = urls.download;
            a.className = 'btn btn-light btn-sm me-1';
            a.innerHTML = '<i class="bx bx-download me-1"></i>Download contract';
            var back = document.querySelector('.contract-page .text-nowrap');
            if (back) back.insertBefore(a, back.firstChild);
        }
    }

    window.addEventListener('beforeunload', function (e) {
        if (!dirty || locked) return;
        e.preventDefault();
        e.returnValue = '';
    });

    renderShell();
    renderAll();
    if (!locked) schedulePreview();
})();
