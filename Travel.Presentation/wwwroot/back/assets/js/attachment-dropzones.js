// Dropzone.js drag/drop UI synced to native hidden multi-file inputs via the DataTransfer API, so
// files travel with the normal form POST instead of Dropzone's own XHR upload.
//
// Drives every ".attachment-dropzone" on the page, pairing "#dropzone-{key}" with "#input-{key}".
// Shared by Booking Create/Edit and BlockService/Details — it knows nothing about either.
(function () {
    'use strict';

    if (typeof Dropzone === 'undefined') return;
    Dropzone.autoDiscover = false;

    function syncInput(dz, input) {
        var dt = new DataTransfer();
        dz.files.forEach(function (file) {
            if (file.status !== Dropzone.ERROR) dt.items.add(file);
        });
        input.files = dt.files;
    }

    document.querySelectorAll('.attachment-dropzone').forEach(function (el) {
        var key = el.id.replace('dropzone-', '');
        var input = document.getElementById('input-' + key);
        if (!input) return;

        // Opt-in per dropzone via data-max-files. Absent means unlimited, which is what the booking
        // flight/passport and block-document areas want — only the transaction receipt caps at one.
        var maxFiles = parseInt(el.getAttribute('data-max-files'), 10);
        if (isNaN(maxFiles) || maxFiles < 1) maxFiles = null;

        var dz = new Dropzone(el, {
            url: '#',
            autoProcessQueue: false,
            addRemoveLinks: true,
            acceptedFiles: '.pdf,.jpg,.jpeg,.png',
            maxFilesize: 10,
            maxFiles: maxFiles,
            dictRemoveFile: 'Remove',
            dictMaxFileExceeded: 'This file is larger than 10MB.',
            dictInvalidFileType: 'Only PDF, JPG and PNG files are allowed.',
            dictMaxFilesExceeded: 'Only one file can be attached here.',
        });

        // Dropzone's own maxFiles only marks the extra file as an error and leaves it on screen; the
        // sync below already filters ERROR files out of the input, but dropping the tile too is what
        // makes a single-file zone read as "replace" instead of "you have two, one of which is red".
        if (maxFiles === 1) {
            dz.on('addedfile', function (file) {
                if (dz.files.length > 1) dz.removeFile(dz.files[0]);
            });
        }

        var sync = function () { syncInput(dz, input); };
        dz.on('addedfile', sync);
        dz.on('removedfile', sync);
        dz.on('error', sync);
    });
})();
