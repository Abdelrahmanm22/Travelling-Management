// The children/infants switches belong to a per-pax type only — showing them on a per-room one asks
// a question that has no effect. Hidden rather than removed, so toggling back and forth inside one
// edit keeps the operator's choice; ExpenseTypeController clears both on save when the basis is per
// room, which is what stops a stale setting resurfacing later.
(function () {
    'use strict';

    var basis = document.getElementById('expense-basis');
    var paxOptions = document.getElementById('pax-options');

    if (!basis || !paxOptions) return;

    var PER_PAX = '2'; // ExpenseBasis.PerPax

    function sync() {
        paxOptions.classList.toggle('d-none', basis.value !== PER_PAX);
    }

    basis.addEventListener('change', sync);
    sync();
})();
