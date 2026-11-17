/*
 * Home dashboard charts (ApexCharts, already loaded on every page by _Scripts).
 *
 * The figures come from <script type="application/json" id="dashboard-data">, which the server
 * writes only for the sections the user may see — a chart whose data or container is missing is
 * simply not drawn. Colours are read from the --mt-* tokens in app-extras.css, never written here,
 * and re-read when the topbar toggle flips data-theme (it switches without a page reload).
 *
 * One y-axis per chart, always: margin is a percentage and lives in the tooltip and the Margin
 * tile, never on a second scale beside the money.
 */
(function () {
    'use strict';

    var source = document.getElementById('dashboard-data');
    if (!source || typeof ApexCharts === 'undefined') return;

    var data = JSON.parse(source.textContent || '{}');
    var currency = data.currency || 'USD';
    var charts = [];

    var money = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    var compact = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });

    function token(name) {
        return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    }

    function palette() {
        return {
            dark: document.documentElement.getAttribute('data-theme') === 'dark',
            series1: token('--mt-chart-1'),
            series2: token('--mt-chart-2'),
            text: token('--mt-text'),
            muted: token('--mt-text-muted'),
            border: token('--mt-border'),
            surface: token('--mt-surface'),
            font: token('--mt-font-sans') || 'Poppins, sans-serif',
            tone: {
                success: token('--mt-success'),
                warning: token('--mt-warning'),
                danger: token('--mt-danger'),
                info: token('--mt-info'),
                secondary: token('--mt-neutral')
            }
        };
    }

    function formatMoney(value) {
        return value === null || value === undefined ? '—' : currency + ' ' + money.format(value);
    }

    // What every chart shares: transparent on the card, no toolbar or zoom, a recessive dashed grid.
    function base(p) {
        return {
            chart: {
                height: 280,
                fontFamily: p.font,
                foreColor: p.muted,
                background: 'transparent',
                toolbar: { show: false },
                zoom: { enabled: false },
                animations: { enabled: true, speed: 300 }
            },
            theme: { mode: p.dark ? 'dark' : 'light' },
            dataLabels: { enabled: false },
            grid: {
                borderColor: p.border,
                strokeDashArray: 3,
                xaxis: { lines: { show: false } },
                padding: { left: 4, right: 4 }
            },
            legend: { labels: { colors: p.muted }, markers: { radius: 3 } },
            tooltip: { theme: p.dark ? 'dark' : 'light' },
            states: { active: { filter: { type: 'none' } } },
            // Twelve month labels don't fit side by side on a phone; slant them rather than overlap.
            responsive: [{
                breakpoint: 576,
                options: { xaxis: { labels: { rotate: -45, rotateAlways: true, hideOverlappingLabels: true } } }
            }]
        };
    }

    // Thin bars with rounded ends and a 2px surface gap between neighbours.
    function bars(p) {
        return {
            plotOptions: { bar: { columnWidth: '55%', endingShape: 'rounded' } },
            stroke: { show: true, width: 2, colors: ['transparent'] }
        };
    }

    function merge(target) {
        for (var i = 1; i < arguments.length; i++) {
            var src = arguments[i];
            Object.keys(src).forEach(function (key) {
                var value = src[key];
                if (value && typeof value === 'object' && !Array.isArray(value) && target[key] && typeof target[key] === 'object') {
                    target[key] = merge({}, target[key], value);
                } else {
                    target[key] = value;
                }
            });
        }
        return target;
    }

    // A bar chart whose every value is 0 has no scale — ApexCharts labels its axis "Infinity".
    // Say so in words instead of drawing an empty frame.
    var emptyText = {
        'chart-arrivals': 'No arrivals in the next 30 days.',
        'chart-sales': 'No sales or costs in the last 12 months.',
        'chart-payments': 'No payments received in the last 12 months.'
    };

    function allZero() {
        for (var i = 0; i < arguments.length; i++) {
            if ((arguments[i] || []).some(function (v) { return v; })) return false;
        }
        return true;
    }

    function isEmpty(id) {
        if (id === 'chart-arrivals') return data.arrivals && allZero(data.arrivals.pax);
        if (id === 'chart-sales') return data.sales && allZero(data.sales.sale, data.sales.expenses);
        if (id === 'chart-payments') return data.payments && allZero(data.payments.received);
        return false;
    }

    var builders = {
        'chart-arrivals': function (p) {
            if (!data.arrivals) return null;
            var bookings = data.arrivals.bookings;
            return merge(base(p), bars(p), {
                chart: { type: 'bar' },
                series: [{ name: 'PAX arriving', data: data.arrivals.pax }],
                colors: [p.series1],
                xaxis: {
                    categories: data.arrivals.labels,
                    tickAmount: 10,
                    labels: {
                        rotate: 0,
                        hideOverlappingLabels: true,
                        // "Tue 17 Nov" in the tooltip, "17 Nov" on the axis.
                        formatter: function (value) { return typeof value === 'string' ? value.slice(4) : value; }
                    },
                    axisBorder: { color: p.border },
                    axisTicks: { show: false }
                },
                yaxis: { forceNiceScale: true, min: 0, labels: { formatter: function (v) { return Math.round(v); } } },
                tooltip: {
                    x: { formatter: function (value, opts) { return data.arrivals.labels[opts.dataPointIndex]; } },
                    y: {
                        formatter: function (value, opts) {
                            var n = bookings[opts.dataPointIndex];
                            return value + ' pax · ' + n + ' booking' + (n === 1 ? '' : 's');
                        }
                    }
                }
            });
        },

        'chart-statuses': function (p) {
            if (!data.statuses) return null;
            var total = data.statuses.reduce(function (sum, s) { return sum + s.value; }, 0);
            if (total === 0) return null;
            return merge(base(p), {
                chart: { type: 'donut', height: 260 },
                series: data.statuses.map(function (s) { return s.value; }),
                labels: data.statuses.map(function (s) { return s.label; }),
                colors: data.statuses.map(function (s) { return p.tone[s.tone] || p.tone.secondary; }),
                // The surface-coloured stroke is the 2px gap between slices.
                stroke: { width: 2, colors: [p.surface] },
                legend: { position: 'bottom', fontSize: '12px' },
                plotOptions: {
                    pie: {
                        donut: {
                            size: '68%',
                            labels: {
                                show: true,
                                value: { color: p.text, fontSize: '20px', fontWeight: 600 },
                                name: { color: p.muted },
                                total: {
                                    show: true,
                                    label: 'Bookings',
                                    color: p.muted,
                                    formatter: function () { return total; }
                                }
                            }
                        }
                    }
                },
                tooltip: { y: { formatter: function (v) { return v + ' booking' + (v === 1 ? '' : 's'); } } }
            });
        },

        'chart-sales': function (p) {
            if (!data.sales) return null;
            var s = data.sales;
            return merge(base(p), bars(p), {
                chart: { type: 'bar' },
                series: [
                    { name: 'Sale', data: s.sale },
                    { name: 'Expenses', data: s.expenses }
                ],
                colors: [p.series1, p.series2],
                legend: { position: 'top', horizontalAlign: 'right' },
                xaxis: {
                    categories: s.labels,
                    labels: { rotate: 0, hideOverlappingLabels: true },
                    axisBorder: { color: p.border },
                    axisTicks: { show: false }
                },
                yaxis: { min: 0, forceNiceScale: true, labels: { formatter: function (v) { return compact.format(v); } } },
                tooltip: {
                    shared: true,
                    intersect: false,
                    y: { formatter: function (v) { return formatMoney(v); } },
                    // Margin and the unpriced caveat ride in the tooltip title — no second axis.
                    x: {
                        formatter: function (value, opts) {
                            var i = opts.dataPointIndex;
                            var margin = s.margin[i];
                            var text = s.labels[i] + ' · margin ' + (margin === null ? '—' : margin.toFixed(1) + '%');
                            if (s.unpriced[i] > 0) text += ' · ' + s.unpriced[i] + ' unpriced';
                            return text;
                        }
                    }
                }
            });
        },

        'chart-payments': function (p) {
            if (!data.payments) return null;
            return merge(base(p), bars(p), {
                chart: { type: 'bar' },
                series: [{ name: 'Received', data: data.payments.received }],
                colors: [p.series1],
                xaxis: {
                    categories: data.payments.labels,
                    labels: { rotate: 0, hideOverlappingLabels: true },
                    axisBorder: { color: p.border },
                    axisTicks: { show: false }
                },
                yaxis: { forceNiceScale: true, labels: { formatter: function (v) { return compact.format(v); } } },
                tooltip: { y: { formatter: function (v) { return formatMoney(v); } } }
            });
        }
    };

    function render() {
        var p = palette();
        Object.keys(builders).forEach(function (id) {
            var el = document.getElementById(id);
            if (!el) return;
            if (isEmpty(id)) {
                el.classList.remove('dash-chart');
                el.innerHTML = '<div class="dash-empty"><i class="bx bx-info-circle"></i></div>';
                el.firstChild.appendChild(document.createTextNode(' ' + emptyText[id]));
                return;
            }
            var options = builders[id](p);
            if (!options) return;
            var chart = new ApexCharts(el, options);
            chart.render();
            charts.push({ id: id, chart: chart });
        });
    }

    // Restyle in place rather than destroy-and-recreate: a fresh chart measures its container while
    // theme-sync.js is still swapping the vendor stylesheets, and comes out the wrong width with no bars.
    function restyle() {
        var p = palette();
        charts.forEach(function (entry) {
            var options = builders[entry.id](p);
            if (options) entry.chart.updateOptions(options, true, false);
        });
    }

    render();

    // ApexCharts re-measures only on a window resize. A card can change width without one: the theme
    // swap briefly drops Bootstrap's grid (every column goes full width while the new stylesheet
    // loads), and collapsing the sidebar widens the content. Re-measure when a card really changes.
    if (window.ResizeObserver) {
        var widths = new Map();
        var pending = null;
        var observer = new ResizeObserver(function (entries) {
            var changed = entries.some(function (entry) {
                var width = Math.round(entry.contentRect.width);
                var previous = widths.get(entry.target);
                widths.set(entry.target, width);
                return previous !== undefined && previous !== width;
            });
            if (!changed) return;
            clearTimeout(pending);
            pending = setTimeout(function () { window.dispatchEvent(new Event('resize')); }, 100);
        });
        charts.forEach(function (entry) {
            observer.observe(document.getElementById(entry.id).closest('.card-body'));
        });
    }

    // The topbar theme toggle flips data-theme in place; recolour with that theme's tokens.
    var lastTheme = document.documentElement.getAttribute('data-theme');
    new MutationObserver(function () {
        var theme = document.documentElement.getAttribute('data-theme');
        if (theme === lastTheme) return;
        lastTheme = theme;
        requestAnimationFrame(restyle);
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
})();
