// The domestic-flight dialog on BlockService/Details (_BlockFlightModal.cshtml): one shared modal for
// Add and Edit, posted as an ordinary multipart form so the dropzone's files travel with it. The
// dropzone itself is driven by attachment-dropzones.js, which must load first.
(function () {
    'use strict';

    var modalEl = document.getElementById('block-flight-modal');
    var form = document.getElementById('block-flight-form');
    if (!modalEl || !form) return;

    // Created once: this Bootstrap build (5.0.0-beta2) has no getOrCreateInstance.
    var modal = new bootstrap.Modal(modalEl);

    var title = document.getElementById('block-flight-title');
    var idInput = document.getElementById('block-flight-id');
    var number = document.getElementById('block-flight-number');
    var date = document.getElementById('block-flight-date');
    var time = document.getElementById('block-flight-time');
    var dateNote = document.getElementById('block-flight-date-note');
    var existing = document.getElementById('block-flight-existing');
    var existingList = document.getElementById('block-flight-existing-list');
    var filesLabel = document.getElementById('block-flight-files-label');
    var dropzoneEl = document.getElementById('dropzone-BlockFlightFiles');
    var fileInput = document.getElementById('input-BlockFlightFiles');
    var submit = document.getElementById('block-flight-submit');
    var checkIn = form.getAttribute('data-check-in');

    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    function shortDate(iso) {
        var p = (iso || '').split('-');
        return p.length === 3 ? p[2] + ' ' + MONTHS[parseInt(p[1], 10) - 1] : iso;
    }

    function bookingBoxes() {
        return Array.prototype.slice.call(form.querySelectorAll('.js-flight-booking'));
    }

    // No rule on the date (an overnight flight the evening before is routine) — just say so.
    function updateDateNote() {
        var differs = date.value && date.value !== checkIn;
        dateNote.textContent = differs ? 'Flight date ' + shortDate(date.value) + ', check-in ' + shortDate(checkIn) : '';
        dateNote.classList.toggle('d-none', !differs);
    }
    date.addEventListener('change', updateDateNote);

    function resetDropzone() {
        if (dropzoneEl && dropzoneEl.dropzone) dropzoneEl.dropzone.removeAllFiles(true);
        if (fileInput) fileInput.value = '';
    }

    function renderExistingFiles(files) {
        existingList.innerHTML = '';
        files.forEach(function (f) {
            var li = document.createElement('li');
            li.className = 'd-flex align-items-center justify-content-between border rounded px-2 py-1 mb-1';

            var link = document.createElement('a');
            link.href = f.url;
            link.target = '_blank';
            link.rel = 'noopener';
            link.className = 'text-truncate me-2';
            link.textContent = f.name;

            var check = document.createElement('div');
            check.className = 'form-check mb-0 text-nowrap';
            var box = document.createElement('input');
            box.type = 'checkbox';
            box.className = 'form-check-input js-flight-remove-file';
            box.name = 'RemoveFileIds';
            box.value = f.id;
            box.id = 'flight-remove-file-' + f.id;
            var label = document.createElement('label');
            label.className = 'form-check-label text-danger small';
            label.htmlFor = box.id;
            label.textContent = 'Remove';
            check.appendChild(box);
            check.appendChild(label);

            li.appendChild(link);
            li.appendChild(check);
            existingList.appendChild(li);
        });
        existing.classList.toggle('d-none', files.length === 0);
    }

    function openAdd() {
        form.action = form.getAttribute('data-add-url');
        title.textContent = 'Add domestic flight';
        idInput.disabled = true;
        idInput.value = '';
        number.value = '';
        date.value = checkIn;
        time.value = '';
        // Only the bookings no flight here covers yet — the first flight therefore starts with everyone.
        bookingBoxes().forEach(function (box) { box.checked = box.getAttribute('data-on-flight') !== 'true'; });
        renderExistingFiles([]);
        filesLabel.textContent = 'Ticket files';
        resetDropzone();
        updateDateNote();
        modal.show();
    }

    function openEdit(btn) {
        form.action = form.getAttribute('data-edit-url');
        title.textContent = 'Edit domestic flight';
        idInput.disabled = false;
        idInput.value = btn.getAttribute('data-id');
        number.value = btn.getAttribute('data-number');
        date.value = btn.getAttribute('data-date');
        time.value = btn.getAttribute('data-time');

        var ticked = JSON.parse(btn.getAttribute('data-booking-ids') || '[]').map(String);
        bookingBoxes().forEach(function (box) { box.checked = ticked.indexOf(box.value) !== -1; });

        renderExistingFiles(JSON.parse(btn.getAttribute('data-files') || '[]'));
        filesLabel.textContent = 'Add more files';
        resetDropzone();
        updateDateNote();
        modal.show();
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('.js-flight-add')) { openAdd(); return; }
        var edit = e.target.closest('.js-flight-edit');
        if (edit) { openEdit(edit); return; }
        var del = e.target.closest('.js-flight-delete');
        if (del) confirmDelete(del);
    });

    // The server checks every one of these again; this only spares a round trip, which would also
    // empty the dropzone.
    form.addEventListener('submit', function (e) {
        var problem = null;
        var kept = existingList.querySelectorAll('.js-flight-remove-file:not(:checked)').length;
        var added = fileInput ? fileInput.files.length : 0;

        if (!number.value.trim()) problem = 'Enter the flight number.';
        else if (!date.value) problem = 'Enter the flight date.';
        else if (!time.value) problem = 'Enter the flight time.';
        else if (!bookingBoxes().some(function (b) { return b.checked; })) problem = 'Tick at least one booking on this flight.';
        else if (kept + added === 0) problem = 'Attach at least one ticket file.';

        if (problem) {
            e.preventDefault();
            toastr.error(problem);
            return;
        }
        submit.disabled = true;
        submit.innerHTML = '<i class="bx bx-loader-alt bx-spin me-1"></i>Saving…';
    });

    function confirmDelete(btn) {
        var token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        Swal.fire({
            title: 'Delete this flight?',
            text: btn.getAttribute('data-number') + ' — its files are deleted, and the bookings on it lose their domestic flight.',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonColor: '#f46a6a',
            cancelButtonColor: '#74788d',
            confirmButtonText: 'Yes, delete it'
        }).then(function (result) {
            if (!result.isConfirmed) return;

            $.post(form.getAttribute('data-delete-url'), { id: btn.getAttribute('data-id'), __RequestVerificationToken: token })
                .done(function (res) {
                    if (res.success) { toastr.success(res.message); setTimeout(function () { location.reload(); }, 800); }
                    else { toastr.error(res.message); }
                })
                .fail(function () { toastr.error('Something went wrong. Please try again.'); });
        });
    }
})();
