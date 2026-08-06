// Block status modal — shared by Block Services, Single Arrivals and the block file page.
//
// The page supplies window.blockStatusConfig = { url, token } before loading this script; the
// markup lives in Views/BlockService/_BlockStatusModal.cshtml and the trigger buttons carry every
// value as data-* so no per-row state is kept here.
(function () {
    'use strict';

    var config = window.blockStatusConfig || {};

    // Delegated off the document so DataTables paging (which rips rows out of the DOM) can't
    // detach the handler, and so the same script serves the single button on the Details page.
    $(document).on('click', '.js-block-status', function () {
        var $btn = $(this);

        $('#block-status-destination').val($btn.data('destination-id'));
        $('#block-status-checkin').val($btn.data('check-in'));
        $('#block-status-value').val($btn.data('status'));
        $('#block-status-request').val($btn.data('request-link') || '');
        $('#block-status-confirmation').val($btn.data('confirmation-link') || '');
        $('#block-status-subject').text($btn.data('destination-name') + ' · ' + $btn.data('check-in-text'));

        syncFields();
        new bootstrap.Modal(document.getElementById('block-status-modal')).show();
    });

    // Point the operator at the field that matters for the status they picked, without hiding the
    // other one — both links stay editable so a typo in either can be fixed at any time.
    $(document).on('change', '#block-status-value', syncFields);

    function syncFields() {
        var status = parseInt($('#block-status-value').val(), 10);

        $('#block-status-request-group').toggleClass('border-start border-3 border-warning ps-2', status === 2);
        $('#block-status-confirmation-group').toggleClass('border-start border-3 border-success ps-2', status === 3);
        $('#block-status-confirm-note').toggleClass('d-none', status !== 3);
    }

    // Two copy triggers: one reading a modal input, one reading a rendered link's data-value.
    $(document).on('click', '.js-copy-link', function () {
        copy(($($(this).data('target')).val() || '').trim());
    });

    $(document).on('click', '.js-copy-text', function () {
        copy(($(this).data('value') || '').toString().trim());
    });

    function copy(value) {
        if (!value) {
            toastr.info('Nothing to copy yet.');
            return;
        }

        // Requires a secure context; the app runs on https/localhost, and the catch covers the rest.
        navigator.clipboard.writeText(value)
            .then(function () { toastr.success('Link copied.'); })
            .catch(function () { toastr.error("Couldn't copy the link."); });
    }

    $(document).on('submit', '#block-status-form', function (e) {
        e.preventDefault();

        var $form = $(this);
        var $btn = $('#block-status-submit');
        $btn.prop('disabled', true);

        $.post(config.url, $form.serialize() + '&__RequestVerificationToken=' + encodeURIComponent(config.token))
            .done(function (res) {
                // Re-enabled on both paths so a rejected link doesn't leave the button dead.
                $btn.prop('disabled', false);
                if (res.success) {
                    bootstrap.Modal.getInstance(document.getElementById('block-status-modal')).hide();
                    toastr.success(res.message);
                    setTimeout(function () { location.reload(); }, 800);
                } else {
                    toastr.error(res.message);
                }
            })
            .fail(function () {
                $btn.prop('disabled', false);
                toastr.error('Something went wrong. Please try again.');
            });
    });
})();
