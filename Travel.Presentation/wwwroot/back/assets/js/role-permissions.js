// Drives the grouped permission picker: counter, per-module bulk links and live search.
(function ($) {
    'use strict';

    var SELECT_ALL = 'SELECT ALL';
    var DESELECT_ALL = 'DESELECT ALL';

    // Bulk actions only ever touch rows the current search leaves visible, and never
    // role-inherited rows (those are disabled so they can't post).
    function actionable($scope) {
        return $scope.find('.permission-item:not(.d-none) .js-permission').not(':disabled');
    }

    function refreshModuleLabel($module) {
        var $actionable = actionable($module);
        var allOn = $actionable.length > 0 && $actionable.filter(':checked').length === $actionable.length;

        $module.find('.js-module-toggle').text(allOn ? DESELECT_ALL : SELECT_ALL);
    }

    function refresh() {
        var $picker = $('.permission-picker');
        if (!$picker.length) return;

        $('#permission-counter').text($picker.find('.js-permission:checked').length);
        $picker.find('.permission-module').each(function () { refreshModuleLabel($(this)); });
    }

    function filterPermissions(term) {
        var query = (term || '').trim().toLowerCase();
        var anyVisible = false;

        $('.permission-module').each(function () {
            var $module = $(this);
            var visible = 0;

            $module.find('.permission-item').each(function () {
                var match = !query || ($(this).data('search') || '').toString().indexOf(query) !== -1;
                $(this).toggleClass('d-none', !match);
                if (match) visible++;
            });

            $module.toggleClass('d-none', visible === 0);
            if (visible > 0) anyVisible = true;
        });

        $('#permission-empty').toggleClass('d-none', anyVisible);

        // Visibility changes what a bulk link would act on, so the labels must follow.
        refresh();
    }

    $(function () {
        if (!$('.permission-picker').length) return;

        refresh();

        $(document).on('change', '.js-permission', refresh);

        $(document).on('click', '.js-module-toggle', function () {
            var $module = $('.permission-module[data-module="' + $(this).data('module') + '"]');
            var turnOn = $(this).text().trim() === SELECT_ALL;

            actionable($module).prop('checked', turnOn);
            refresh();
        });

        $('#select-all-permissions').on('click', function () {
            actionable($('.permission-picker')).prop('checked', true);
            refresh();
        });

        $('#clear-all-permissions').on('click', function () {
            actionable($('.permission-picker')).prop('checked', false);
            refresh();
        });

        $('#permission-search').on('input', function () {
            filterPermissions($(this).val());
        });
    });
})(jQuery);
