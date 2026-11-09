// Movement type form: the two flight questions only make sense once "Is it a flight?" is on.
(function () {
    var toggle = document.getElementById('isFlightToggle');
    var block = document.getElementById('flightQuestions');
    if (!toggle || !block) return;

    function sync() { block.classList.toggle('d-none', !toggle.checked); }
    toggle.addEventListener('change', sync);
    sync();
})();
