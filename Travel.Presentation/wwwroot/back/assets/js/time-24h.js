// 24-hour time inputs. Native <input type="time"> follows the OS locale and shows AM/PM on
// en-US machines, so .time-24h text boxes get bootstrap-timepicker in 24h mode instead.
// defaultTime: false keeps an empty box empty — the bound TimeOnly? must stay null, not 00:00.
$('.time-24h').each(function () {
    $(this).timepicker({
        showMeridian: false,
        minuteStep: 1,
        defaultTime: false,
        showInputs: true,
        appendWidgetTo: $(this).closest('.time-24h-group'),
        icons: { up: 'bx bx-chevron-up', down: 'bx bx-chevron-down' }
    });
});
