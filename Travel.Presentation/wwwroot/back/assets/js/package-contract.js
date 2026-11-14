/*
 * Setup ▸ Packages ▸ Contract program. Legs (from the package's itinerary) with their days, the trip's
 * last day, the two lists with the visa line, and the Importante / Documentos texts. Saved as one JSON
 * body; errors come back keyed by field ("Legs[1].Days[0].Title") and show beside it.
 */
(function () {
    'use strict';

    var stateEl = document.getElementById('program-state');
    if (!stateEl) return;

    var boot = JSON.parse(stateEl.textContent);
    var M = boot.model;
    var canEdit = !!boot.canEdit;
    var token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    var editor = document.getElementById('program-editor');
    var dirty = false;
    var ro = canEdit ? '' : ' disabled';

    function esc(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function errorSlot(key) { return '<div class="invalid-feedback d-block" data-error="' + key + '"></div>'; }

    function tools(list, index, length) {
        if (!canEdit) return '';
        function b(act, icon, label, cls) {
            return '<button type="button" class="btn btn-light btn-sm contract-icon-btn ' + (cls || '') + '" data-act="' + act +
                '" data-list="' + list + '" data-index="' + index + '" aria-label="' + label + '" title="' + label + '"><i class="bx ' + icon + '"></i></button>';
        }
        return '<div class="contract-row-tools">' +
            b('up', 'bx-chevron-up', 'Move up', index === 0 ? 'invisible' : '') +
            b('down', 'bx-chevron-down', 'Move down', index === length - 1 ? 'invisible' : '') +
            b('remove', 'bx-trash', 'Remove', 'text-danger') + '</div>';
    }

    function add(list, label) {
        return canEdit ? '<button type="button" class="btn btn-outline-primary btn-sm mt-1" data-act="add" data-list="' + list +
            '"><i class="bx bx-plus me-1"></i>' + label + '</button>' : '';
    }

    /** "legs.1.days" → the array; "finalDays", "includes", "excludes" → the array. */
    function listAt(path) {
        var parts = path.split('.');
        var target = M;
        for (var i = 0; i < parts.length; i++) target = target[parts[i]];
        return target;
    }

    function daysHtml(list, days, errPrefix, numberFrom) {
        var html = '';
        days.forEach(function (d, i) {
            html += '<div class="contract-day' + (d.isDepartureDay ? ' is-departure' : '') + '">' +
                '<div class="contract-row">' +
                '<span class="contract-day-number">Dia ' + (numberFrom + i) + '</span>' +
                '<input class="form-control form-control-sm flex-grow-1" data-bind="' + list + '.' + i + '.title" placeholder="Chegada ao Cairo" value="' + esc(d.title) + '"' + ro + ' />' +
                tools(list, i, days.length) + '</div>' + errorSlot(errPrefix + '[' + i + '].Title') +
                '<textarea class="form-control form-control-sm mt-1" rows="3" data-bind="' + list + '.' + i + '.body"' + ro + '>' + esc(d.body) + '</textarea>' +
                (list.indexOf('legs.') === 0
                    ? '<div class="form-check form-switch mt-1"><input class="form-check-input" type="checkbox" id="dep-' + list.replace(/\./g, '-') + '-' + i +
                      '" data-bind="' + list + '.' + i + '.isDepartureDay" data-kind="bool"' + (d.isDepartureDay ? ' checked' : '') + ro + ' />' +
                      '<label class="form-check-label small" for="dep-' + list.replace(/\./g, '-') + '-' + i + '">Departure day — guests leave this destination today; extra free days go before it</label></div>'
                    : '') +
                '</div>';
        });
        return html;
    }

    function render() {
        var html = '<div class="row g-2 mb-4"><div class="col-md-8"><label class="form-label">Contract title (Portuguese)</label>' +
            '<input class="form-control" data-bind="contractTitle" placeholder="Cairo + Cruzeiro + Hurghada" value="' + esc(M.contractTitle) + '"' + ro + ' />' +
            errorSlot('ContractTitle') +
            '<div class="form-text">Prints as "VIAGEM EGITO: {title} 10 dias / 9 noites" and "Roteiro {TITLE}". Blank uses the package name.</div></div></div>';

        html += '<h5 class="font-size-15">Day-by-day program</h5>';
        var number = 1;
        M.legs.forEach(function (leg, li) {
            var days = leg.days || [];
            html += '<div class="contract-section">' +
                '<div class="d-flex flex-wrap align-items-end gap-2 mb-2">' +
                '<div class="fw-bold me-auto">' + (li + 1) + '. ' + esc(leg.label) + '</div>' +
                '<div><label class="form-label small mb-0">Written for</label>' +
                '<div class="input-group input-group-sm contract-nights"><input type="number" min="0" max="120" class="form-control" data-bind="legs.' + li + '.writtenForNights" data-kind="int" value="' +
                esc(leg.writtenForNights) + '"' + ro + ' /><span class="input-group-text">nights</span></div></div>' +
                '<div class="flex-grow-1 contract-heading-input"><label class="form-label small mb-0">Heading</label>' +
                '<input class="form-control form-control-sm" data-bind="legs.' + li + '.heading" placeholder="Programação ' + esc(leg.label.replace(' (retorno)', '')) +
                '" value="' + esc(leg.heading) + '"' + ro + ' /></div></div>' +
                errorSlot('Legs[' + li + '].WrittenForNights') +
                '<p class="small text-muted mb-2">' + days.length + ' day(s) written for ' + (leg.writtenForNights || 0) + ' night(s).' +
                (days.length && !days.some(function (d) { return d.isDepartureDay; }) ? ' No departure day is marked, so extra free days go at the end of this leg.' : '') + '</p>' +
                daysHtml('legs.' + li + '.days', days, 'Legs[' + li + '].Days', number) +
                add('legs.' + li + '.days', 'Add day') + '</div>';
            number += days.length;
        });

        html += '<div class="contract-section"><div class="fw-bold mb-1">Last day</div>' +
            '<p class="small text-muted mb-2">The trip\'s final day — e.g. "Cairo / País de Origem". Printed after every leg.</p>' +
            daysHtml('finalDays', M.finalDays, 'FinalDays', number) + add('finalDays', 'Add day') + '</div>';

        html += '<div class="row g-4 mt-1"><div class="col-lg-6"><h5 class="font-size-15">Pacote inclui</h5>';
        M.includes.forEach(function (item, i) {
            html += '<div class="contract-row"><input class="form-control form-control-sm flex-grow-1" data-bind="includes.' + i + '.text" value="' + esc(item.text) + '"' + ro + ' />' +
                tools('includes', i, M.includes.length) + '</div>' + errorSlot('Includes[' + i + '].Text');
        });
        html += add('includes', 'Add line') + '<p class="small text-muted mt-2 mb-0">Extra tours a booking buys are added automatically.</p></div>';

        html += '<div class="col-lg-6"><h5 class="font-size-15">Não está incluso</h5>';
        M.excludes.forEach(function (item, i) {
            html += '<div class="contract-row"><input class="form-control form-control-sm flex-grow-1" data-bind="excludes.' + i + '.text" value="' + esc(item.text) + '"' + ro + ' />' +
                '<div class="form-check text-nowrap mb-0"><input class="form-check-input" type="radio" name="visa-line" id="visa-' + i + '" data-visa="' + i + '"' +
                (M.visaLineIndex === i ? ' checked' : '') + ro + ' /><label class="form-check-label small" for="visa-' + i + '">Visa line</label></div>' +
                tools('excludes', i, M.excludes.length) + '</div>' + errorSlot('Excludes[' + i + '].Text');
        });
        html += add('excludes', 'Add line') + errorSlot('VisaLineIndex') +
            '<p class="small text-muted mt-2 mb-0">Mark the visa line: when a booking includes the visa, it moves to "Pacote inclui" as "Visto de entrada no Egito".' +
            (canEdit && M.visaLineIndex >= 0 ? ' <a href="#" data-act="clear-visa">No visa line</a>' : '') + '</p></div></div>';

        html += '<div class="row g-3 mt-2"><div class="col-lg-6"><label class="form-label">Importante</label>' +
            '<textarea class="form-control" rows="8" data-bind="importantText"' + ro + '>' + esc(M.importantText) + '</textarea>' +
            '<div class="form-text">Tips and safety notes. Write the guide days into the text for this package.</div></div>' +
            '<div class="col-lg-6"><label class="form-label">Documentos Requeridos</label>' +
            '<textarea class="form-control" rows="8" data-bind="documentsText"' + ro + '>' + esc(M.documentsText) + '</textarea></div></div>';

        editor.innerHTML = html;
    }

    function setPath(path, value) {
        var parts = path.split('.');
        var target = M;
        for (var i = 0; i < parts.length - 1; i++) target = target[parts[i]];
        target[parts[parts.length - 1]] = value;
    }

    editor.addEventListener('input', onEdit);
    editor.addEventListener('change', onEdit);

    function onEdit(e) {
        var el = e.target;
        if (el.dataset && el.dataset.visa !== undefined) {
            M.visaLineIndex = Number(el.dataset.visa);
            dirty = true;
            render();
            return;
        }
        if (!el.dataset || !el.dataset.bind) return;
        var kind = el.dataset.kind;
        if (kind === 'bool') {
            if (e.type !== 'change') return;
            setPath(el.dataset.bind, el.checked);
            dirty = true;
            render();
            return;
        }
        if (e.type !== 'input') return;
        setPath(el.dataset.bind, kind === 'int' ? (el.value === '' ? 0 : parseInt(el.value, 10) || 0) : el.value);
        dirty = true;
    }

    editor.addEventListener('click', function (e) {
        var b = e.target.closest('[data-act]');
        if (!b || !canEdit) return;
        e.preventDefault();
        var act = b.dataset.act;
        if (act === 'clear-visa') { M.visaLineIndex = -1; dirty = true; render(); return; }

        var list = listAt(b.dataset.list);
        var i = Number(b.dataset.index);
        var isDays = /days$|finalDays$/i.test(b.dataset.list);
        var isExcludes = b.dataset.list === 'excludes';

        if (act === 'add') list.push(isDays ? { title: '', body: '', isDepartureDay: false } : { text: '' });
        else if (act === 'up' && i > 0) { swap(list, i, i - 1); if (isExcludes) fixVisa(i, i - 1); }
        else if (act === 'down' && i < list.length - 1) { swap(list, i, i + 1); if (isExcludes) fixVisa(i, i + 1); }
        else if (act === 'remove') {
            list.splice(i, 1);
            if (isExcludes) {
                if (M.visaLineIndex === i) M.visaLineIndex = -1;
                else if (M.visaLineIndex > i) M.visaLineIndex--;
            }
        }
        dirty = true;
        render();
    });

    function swap(list, a, b) { var t = list[a]; list[a] = list[b]; list[b] = t; }
    function fixVisa(a, b) {
        if (M.visaLineIndex === a) M.visaLineIndex = b;
        else if (M.visaLineIndex === b) M.visaLineIndex = a;
    }

    var saveBtn = document.getElementById('program-save');
    saveBtn && saveBtn.addEventListener('click', function () {
        saveBtn.disabled = true;
        document.querySelectorAll('[data-error]').forEach(function (el) { el.textContent = ''; });
        fetch(boot.saveUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' },
            body: JSON.stringify(M),
        }).then(function (r) { return r.json(); }).then(function (res) {
            if (res.success) { dirty = false; toastr.success(res.message); return; }
            var first = null;
            Object.keys(res.errors || {}).forEach(function (key) {
                var el = document.querySelector('[data-error="' + key + '"]');
                if (el) { el.textContent = res.errors[key]; first = first || el; }
            });
            if (first) first.scrollIntoView({ behavior: 'smooth', block: 'center' });
            toastr.error(res.message);
        }).catch(function () { toastr.error('Something went wrong. Please try again.'); })
            .finally(function () { saveBtn.disabled = false; });
    });

    window.addEventListener('beforeunload', function (e) {
        if (!dirty) return;
        e.preventDefault();
        e.returnValue = '';
    });

    render();
})();
