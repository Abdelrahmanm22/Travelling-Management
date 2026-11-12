/*
 * Setup ▸ Contract Terms. The three settings plus every section, grouped by where it prints. Saved as
 * one JSON body; a validation error comes back keyed by its field ("Sections[3].Body") and is shown
 * beside that field.
 */
(function () {
    'use strict';

    var stateEl = document.getElementById('terms-state');
    if (!stateEl) return;

    var boot = JSON.parse(stateEl.textContent);
    var M = boot.model;
    var canEdit = !!boot.canEdit;
    var token = document.querySelector('input[name="__RequestVerificationToken"]').value;
    var editor = document.getElementById('terms-editor');
    var dirty = false;

    var GROUPS = [
        { placement: 1, title: 'Opening', hint: 'The party paragraph under the title, before the passengers.' },
        { placement: 2, title: 'Object clause', hint: 'Clause 1.0 and the line that introduces the schedule.' },
        { placement: 3, title: 'Closing clauses', hint: 'Everything after "Documentos Requeridos" — clauses 1.3 to 7 and the double-room clause.' },
    ];

    function esc(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function errorSlot(key) {
        return '<div class="invalid-feedback d-block" data-error="' + key + '"></div>';
    }

    function btn(act, index, icon, label, cls) {
        return '<button type="button" class="btn btn-light btn-sm contract-icon-btn ' + (cls || '') + '" data-act="' + act + '" data-index="' + index +
            '" aria-label="' + label + '" title="' + label + '"><i class="bx ' + icon + '"></i></button>';
    }

    function render() {
        var ro = canEdit ? '' : ' disabled';
        var html = '<div class="row g-2 mb-4">' +
            '<div class="col-md-12"><label class="form-label">Document title</label>' +
            '<input class="form-control" data-bind="DocumentTitle" value="' + esc(M.documentTitle) + '"' + ro + ' />' + errorSlot('DocumentTitle') + '</div>' +
            '<div class="col-md-6"><label class="form-label">Signature city</label>' +
            '<input class="form-control" data-bind="SignatureCity" value="' + esc(M.signatureCity) + '"' + ro + ' />' + errorSlot('SignatureCity') +
            '<div class="form-text">Printed as "São Paulo, 17/08/2026" above the signatures.</div></div>' +
            '<div class="col-md-6"><label class="form-label">Company signature name</label>' +
            '<input class="form-control" data-bind="CompanySignatureName" value="' + esc(M.companySignatureName) + '"' + ro + ' />' + errorSlot('CompanySignatureName') +
            '<div class="form-text">Printed as "CONTRATADA: MUSTA TRAVEL".</div></div>' +
            '</div>';

        GROUPS.forEach(function (g) {
            var rows = [];
            M.sections.forEach(function (s, i) { if (s.placement === g.placement) rows.push(i); });
            html += '<div class="contract-terms-group mb-4"><h5 class="font-size-15 mb-1">' + g.title + '</h5>' +
                '<p class="small text-muted mb-2">' + g.hint + '</p>';
            if (!rows.length) html += '<p class="small text-muted">No sections.</p>';
            rows.forEach(function (i, pos) {
                var s = M.sections[i];
                html += '<div class="contract-section">' +
                    '<div class="contract-row">' +
                    '<input class="form-control form-control-sm fw-bold flex-grow-1" data-bind="Sections.' + i + '.heading" ' +
                    'placeholder="Heading (optional) — e.g. CLÁUSULA SEGUNDA - OBRIGAÇÕES DA CONTRATADA" value="' + esc(s.heading) + '"' + ro + ' />' +
                    (canEdit ? '<div class="contract-row-tools">' +
                        btn('up', i, 'bx-chevron-up', 'Move up', pos === 0 ? 'invisible' : '') +
                        btn('down', i, 'bx-chevron-down', 'Move down', pos === rows.length - 1 ? 'invisible' : '') +
                        btn('remove', i, 'bx-trash', 'Remove section', 'text-danger') + '</div>' : '') +
                    '</div>' + errorSlot('Sections[' + i + '].Heading') +
                    '<textarea class="form-control form-control-sm mt-1" rows="' + Math.min(14, Math.max(3, (s.body || '').split('\n').length + 1)) +
                    '" data-bind="Sections.' + i + '.body"' + ro + '>' + esc(s.body) + '</textarea>' + errorSlot('Sections[' + i + '].Body') +
                    '</div>';
            });
            if (canEdit)
                html += '<button type="button" class="btn btn-outline-primary btn-sm" data-act="add" data-placement="' + g.placement + '"><i class="bx bx-plus me-1"></i>Add section</button>';
            html += '</div>';
        });
        editor.innerHTML = html;
    }

    editor.addEventListener('input', function (e) {
        var bind = e.target.dataset && e.target.dataset.bind;
        if (!bind) return;
        var parts = bind.split('.');
        if (parts[0] === 'Sections') M.sections[Number(parts[1])][parts[2]] = e.target.value;
        else M[bind.charAt(0).toLowerCase() + bind.slice(1)] = e.target.value;
        dirty = true;
    });

    /** Swaps a section with the nearest one of the same placement in the given direction. */
    function shift(index, dir) {
        var placement = M.sections[index].placement;
        for (var j = index + dir; j >= 0 && j < M.sections.length; j += dir) {
            if (M.sections[j].placement !== placement) continue;
            var t = M.sections[j]; M.sections[j] = M.sections[index]; M.sections[index] = t;
            return;
        }
    }

    editor.addEventListener('click', function (e) {
        var b = e.target.closest('[data-act]');
        if (!b || !canEdit) return;
        var i = Number(b.dataset.index);
        switch (b.dataset.act) {
            case 'add': M.sections.push({ placement: Number(b.dataset.placement), heading: '', body: '' }); break;
            case 'up': shift(i, -1); break;
            case 'down': shift(i, 1); break;
            case 'remove':
                if ((M.sections[i].body || '').trim() && !window.confirm('Remove this section?')) return;
                M.sections.splice(i, 1);
                break;
            default: return;
        }
        dirty = true;
        render();
    });

    var saveBtn = document.getElementById('terms-save');
    saveBtn && saveBtn.addEventListener('click', function () {
        saveBtn.disabled = true;
        editor.querySelectorAll('[data-error]').forEach(function (el) { el.textContent = ''; });
        fetch(boot.saveUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' },
            body: JSON.stringify(M),
        }).then(function (r) { return r.json(); }).then(function (res) {
            if (res.success) { dirty = false; toastr.success(res.message); return; }
            var first = null;
            Object.keys(res.errors || {}).forEach(function (key) {
                var el = editor.querySelector('[data-error="' + key + '"]');
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
