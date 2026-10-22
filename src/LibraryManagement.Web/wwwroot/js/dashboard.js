(function () {
    'use strict';
    var input = document.getElementById('dashboard-data');
    if (!input) { return; }
    var failure = document.getElementById('dashboard-chart-error');
    function failed() { if (failure) { failure.hidden = false; } }
    try {
        if (typeof Chart === 'undefined') { failed(); return; }
        var data = JSON.parse(input.value);
        var number = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });
        var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        Chart.defaults.font.family = '"Segoe UI", Arial, sans-serif';
        Chart.defaults.font.size = 12;
        Chart.defaults.color = '#62746c';
        var colors = { available: '#477a6b', issued: '#42657b', overdue: '#bc7d35', returned: '#899f79' };
        var stockCanvas = document.getElementById('availability-chart');
        if (stockCanvas && data.TotalBooks > 0) {
            new Chart(stockCanvas, {
                type: 'doughnut',
                data: {
                    labels: ['Available', 'On loan · not overdue', 'Overdue'],
                    datasets: [{ data: [data.AvailableBooks, data.IssuedBooks - data.OverdueBooks, data.OverdueBooks],
                        backgroundColor: [colors.available, colors.issued, colors.overdue], borderColor: '#fff', borderWidth: 3 }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false, cutout: '74%', animation: reducedMotion ? false : { duration: 350 },
                    plugins: {
                        legend: { display: false },
                        tooltip: { callbacks: { label: function (context) {
                            return context.label + ': ' + number.format(context.parsed) + ' copies';
                        } } }
                    }
                },
                plugins: [{
                    id: 'inventoryTotal',
                    afterDraw: function (chart) {
                        var arc = chart.getDatasetMeta(0).data[0];
                        if (!arc) { return; }
                        var ctx = chart.ctx;
                        ctx.save(); ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
                        ctx.fillStyle = '#233735'; ctx.font = '600 28px "Segoe UI", Arial, sans-serif';
                        ctx.fillText(number.format(data.TotalBooks), arc.x, arc.y - 7);
                        ctx.fillStyle = '#6b7874'; ctx.font = '12px "Segoe UI", Arial, sans-serif';
                        ctx.fillText('active copies', arc.x, arc.y + 22); ctx.restore();
                    }
                }]
            });
        }
        new Chart(document.getElementById('activity-chart'), {
            type: 'bar',
            data: {
                labels: data.MonthlyActivity.map(function (month) { return month.ShortLabel; }),
                datasets: [
                    { label: 'Issued', data: data.MonthlyActivity.map(function (month) { return month.IssuedBooks; }), backgroundColor: colors.issued, maxBarThickness: 26 },
                    { label: 'Returned', data: data.MonthlyActivity.map(function (month) { return month.ReturnedBooks; }), backgroundColor: colors.returned, maxBarThickness: 26 }
                ]
            },
            options: {
                responsive: true, maintainAspectRatio: false, animation: reducedMotion ? false : { duration: 350 },
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { position: 'bottom', labels: { padding: 18, usePointStyle: true, pointStyle: 'rect' } },
                    tooltip: { callbacks: {
                        title: function (items) { return items.length ? data.MonthlyActivity[items[0].dataIndex].Label : ''; },
                        label: function (context) { return context.dataset.label + ': ' + number.format(context.parsed.y) + ' transactions'; }
                    } }
                },
                scales: {
                    x: { grid: { display: false }, ticks: { autoSkip: false, maxRotation: 0, minRotation: 0 }, border: { color: '#dce3d8' } },
                    y: { beginAtZero: true, suggestedMax: 1, ticks: { precision: 0 }, grid: { color: '#edf0e9' }, border: { display: false } }
                }
            }
        });
    } catch (_) { failed(); }
})();
