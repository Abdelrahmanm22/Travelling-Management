// Travel voucher dialog on Booking/Details — download the PDF or email it to the client.
//
// Markup: Views/Booking/_BookingVoucherModal.cshtml (URLs and the booking id ride on the modal's data-*).
// The antiforgery token is the page's own @Html.AntiForgeryToken().
(function () {
    'use strict';

    var el = document.getElementById('voucher-modal');
    if (!el) return;

    // One instance at init: this Bootstrap build (5.0.0-beta2) has no getOrCreateInstance, and a new
    // Modal per click leaks the previous instance's handlers.
    var modal = new bootstrap.Modal(el);
    var form = document.getElementById('voucher-form');
    var download = document.getElementById('voucher-download');
    var send = document.getElementById('voucher-send');
    var error = document.getElementById('voucher-error');
    var message = document.getElementById('voucher-message');
    var count = document.getElementById('voucher-message-count');
    var STORAGE_KEY = 'mt-voucher-language';

    function language() {
        var checked = form.querySelector('input[name="language"]:checked');
        return checked ? checked.value : '2';
    }

    function syncDownload() {
        download.href = el.dataset.downloadUrl + '?language=' + encodeURIComponent(language());
    }

    function showError(text) {
        error.textContent = text;
        error.classList.toggle('d-none', !text);
    }

    // The last language this operator chose — a per-browser convenience, so it may be unavailable.
    try {
        var saved = window.localStorage.getItem(STORAGE_KEY);
        var radio = saved && form.querySelector('input[name="language"][value="' + saved + '"]');
        if (radio) radio.checked = true;
    } catch (e) { /* private mode / blocked storage: keep the default */ }
    syncDownload();

    form.addEventListener('change', function (e) {
        if (e.target.name !== 'language') return;
        syncDownload();
        try { window.localStorage.setItem(STORAGE_KEY, language()); } catch (err) { /* ignore */ }
    });

    message.addEventListener('input', function () { count.textContent = message.value.length; });

    // Enter in a field must not send an email.
    form.addEventListener('submit', function (e) { e.preventDefault(); });

    document.querySelectorAll('.js-open-voucher').forEach(function (btn) {
        btn.addEventListener('click', function () {
            showError('');
            modal.show();
        });
    });

    el.addEventListener('shown.bs.modal', function () { document.getElementById('voucher-to').focus(); });

    var emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    send.addEventListener('click', function () {
        var to = document.getElementById('voucher-to').value.trim();
        var cc = document.getElementById('voucher-cc').value.trim();

        // Quick checks only; the server validates again and owns the rules.
        if (!emailPattern.test(to)) { showError('Enter a valid email address for the client.'); return; }
        showError('');

        var original = send.innerHTML;
        send.disabled = true;
        download.classList.add('disabled');
        send.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>Sending…';

        $.post(el.dataset.sendUrl, {
            id: el.dataset.bookingId,
            to: to,
            cc: cc,
            language: language(),
            message: message.value,
            __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').first().val()
        })
            .done(function (res) {
                if (res.success) {
                    toastr.success(res.message);
                    modal.hide();
                    // Refresh the "last sent" line and the audit trail.
                    setTimeout(function () { location.reload(); }, 900);
                } else {
                    showError(res.message);
                    toastr.error(res.message);
                }
            })
            .fail(function () {
                showError('Something went wrong. Please try again.');
                toastr.error('Something went wrong. Please try again.');
            })
            .always(function () {
                send.disabled = false;
                download.classList.remove('disabled');
                send.innerHTML = original;
            });
    });
})();
