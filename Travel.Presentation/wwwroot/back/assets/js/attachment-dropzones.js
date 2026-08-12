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

        var dz = new Dropzone(el, {
            url: '#',
            autoProcessQueue: false,
            addRemoveLinks: true,
            acceptedFiles: '.pdf,.jpg,.jpeg,.png',
            maxFilesize: 10,
            dictRemoveFile: 'Remove',
            dictMaxFileExceeded: 'This file is larger than 10MB.',
            dictInvalidFileType: 'Only PDF, JPG and PNG files are allowed.',
        });

        var sync = function () { syncInput(dz, input); };
        dz.on('addedfile', sync);
        dz.on('removedfile', sync);
        dz.on('error', sync);
    });
})();
