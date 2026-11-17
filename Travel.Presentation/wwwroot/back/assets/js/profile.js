/*
 * Account/Profile — photo preview / remove, and the busy state on both forms.
 *
 * The photo controls live in the identity card but post with the Personal info form
 * (form="profile-info-form"): nothing is saved until Save. The server re-checks type and size;
 * these checks only stop an oversized file from being uploaded at all.
 */
(function () {
    'use strict';

    var img = document.getElementById('profile-avatar-img');
    var fileInput = document.getElementById('profile-image-input');
    var removeInput = document.getElementById('profile-remove-image');
    var removeBtn = document.getElementById('profile-photo-remove');
    var changeBtn = document.getElementById('profile-photo-change');
    var avatarBtn = document.getElementById('profile-avatar-btn');
    var genderSelect = document.getElementById('profile-gender');
    var pending = document.getElementById('profile-photo-pending');
    var errorSpan = document.getElementById('profile-photo-error');
    if (!img || !fileInput) return;

    var storedSrc = img.getAttribute('data-stored-src');
    var maxBytes = parseInt(fileInput.getAttribute('data-max-bytes'), 10) || 2 * 1024 * 1024;
    var allowed = ['.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp'];
    var previewUrl = null;

    // Female = 2 (Travel.Domain.Enums.Gender).
    function defaultSrc() {
        return genderSelect && genderSelect.value === '2'
            ? img.getAttribute('data-default-female')
            : img.getAttribute('data-default-male');
    }

    function revokePreview() {
        if (previewUrl) {
            URL.revokeObjectURL(previewUrl);
            previewUrl = null;
        }
    }

    function setPending(text) {
        if (!pending) return;
        pending.querySelector('span').textContent = text || '';
        pending.classList.toggle('d-none', !text);
    }

    function setError(text) {
        if (!errorSpan) return;
        errorSpan.textContent = text || '';
        errorSpan.classList.toggle('field-validation-error', !!text);
    }

    // What the circle should show when no new file is picked.
    function showCurrent() {
        var removed = removeInput.value === 'true';
        img.src = !removed && storedSrc ? storedSrc : defaultSrc();
        removeBtn.classList.toggle('d-none', removed || !storedSrc);
        setPending(removed && storedSrc ? 'Photo will be removed when you click Save.' : '');
    }

    function openPicker() { fileInput.click(); }

    avatarBtn.addEventListener('click', openPicker);
    changeBtn.addEventListener('click', openPicker);

    fileInput.addEventListener('change', function () {
        var file = fileInput.files && fileInput.files[0];
        revokePreview();
        setError('');

        if (!file) { showCurrent(); return; }

        var dot = file.name.lastIndexOf('.');
        var ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
        if (allowed.indexOf(ext) < 0) {
            fileInput.value = '';
            setError('Only image files (JPG, PNG, GIF, WEBP) are allowed.');
            showCurrent();
            return;
        }
        if (file.size > maxBytes) {
            fileInput.value = '';
            setError('The photo must be ' + Math.round(maxBytes / (1024 * 1024)) + ' MB or smaller.');
            showCurrent();
            return;
        }

        previewUrl = URL.createObjectURL(file);
        img.src = previewUrl;
        removeInput.value = 'false';
        removeBtn.classList.remove('d-none');
        setPending('New photo selected. Click Save to keep it.');
    });

    removeBtn.addEventListener('click', function () {
        revokePreview();
        fileInput.value = '';
        setError('');
        removeInput.value = 'true';
        showCurrent();
    });

    if (genderSelect) {
        genderSelect.addEventListener('change', function () {
            if (!previewUrl) showCurrent();
        });
    }

    // A re-render after a server error keeps RemoveImage but can't keep a picked file.
    showCurrent();

    // Busy state only once the form is valid, so a client-side failure can't leave it stuck.
    document.querySelectorAll('form.profile-form').forEach(function (form) {
        form.addEventListener('submit', function () {
            if (window.jQuery && jQuery.fn.valid && !jQuery(form).valid()) return;
            var btn = form.querySelector('button[type="submit"]');
            if (!btn || btn.disabled) return;
            btn.disabled = true;
            btn.innerHTML = '<i class="bx bx-loader-alt bx-spin me-1"></i> ' + (btn.getAttribute('data-busy-text') || 'Saving…');
        });
    });
})();
