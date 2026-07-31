// Shows the departure-days block only when the destination is a cruise.
(function () {
    function sync() {
        var toggle = document.getElementById('isCruiseToggle');
        var block = document.getElementById('departureDaysBlock');
        if (!toggle || !block) return;

        block.style.display = toggle.checked ? '' : 'none';
        if (!toggle.checked) {
            block.querySelectorAll('input[type=checkbox]').forEach(function (cb) { cb.checked = false; });
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var toggle = document.getElementById('isCruiseToggle');
        if (!toggle) return;
        toggle.addEventListener('change', sync);
        sync();
    });
})();
