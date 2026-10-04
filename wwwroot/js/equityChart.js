// Hover read-out and full screen for the dashboard's equity curve (Components/Pages/EquityChart.razor).
// Everything runs in the browser: moving the mouse never goes over the Blazor circuit. The listeners
// sit on the document, so they keep working when Blazor re-renders or replaces the chart (switching
// account or strategy) and across enhanced navigation.
(function () {
    // The chart's points come from its data-points attribute; parsed once per distinct value.
    let cachedJson = null, cachedPoints = null;
    function pointsOf(svg) {
        const json = svg.getAttribute('data-points');
        if (json !== cachedJson) {
            cachedJson = json;
            try { cachedPoints = JSON.parse(json) || []; } catch { cachedPoints = []; }
        }
        return cachedPoints;
    }

    let active = null; // the svg currently showing the read-out

    function hide() {
        if (!active) return;
        const chart = active.closest('.app-equity');
        active.querySelector('.app-equity-hover')?.setAttribute('visibility', 'hidden');
        chart?.querySelector('.app-equity-tip')?.classList.remove('is-visible');
        active = null;
    }

    function show(svg, clientX) {
        const points = pointsOf(svg);
        const chart = svg.closest('.app-equity');
        const matrix = svg.getScreenCTM();
        if (points.length < 2 || !chart || !matrix) return;

        // Mouse position in the chart's own (viewBox) units, then the nearest point - they're evenly spaced.
        const at = new DOMPoint(clientX, 0).matrixTransform(matrix.inverse());
        const first = points[0].x, last = points[points.length - 1].x;
        const index = Math.max(0, Math.min(points.length - 1, Math.round((at.x - first) / (last - first) * (points.length - 1))));
        const p = points[index];

        const hover = svg.querySelector('.app-equity-hover');
        hover.querySelector('line').setAttribute('x1', p.x);
        hover.querySelector('line').setAttribute('x2', p.x);
        hover.querySelector('circle').setAttribute('cx', p.x);
        hover.querySelector('circle').setAttribute('cy', p.y);
        hover.setAttribute('visibility', 'visible');

        const tip = chart.querySelector('.app-equity-tip');
        tip.querySelector('.app-equity-tip-date').textContent = p.d;
        tip.querySelector('.app-equity-tip-balance').textContent = p.b;
        const result = tip.querySelector('.app-equity-tip-result');
        result.textContent = p.r ?? '';
        result.className = 'app-equity-tip-result ' + (p.t > 0 ? 'app-gain' : p.t < 0 ? 'app-loss' : '');

        // Above the point, kept inside the chart horizontally; below it when there's no room above.
        const screen = new DOMPoint(p.x, p.y).matrixTransform(matrix);
        const box = chart.getBoundingClientRect();
        tip.classList.add('is-visible');
        const half = tip.offsetWidth / 2;
        const left = Math.max(half, Math.min(box.width - half, screen.x - box.left));
        const top = screen.y - box.top;
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
        tip.classList.toggle('is-below', top - tip.offsetHeight - 14 < 0);

        active = svg;
    }

    document.addEventListener('pointermove', function (event) {
        const svg = event.target instanceof Element ? event.target.closest('svg.app-equity-plot') : null;
        if (svg) show(svg, event.clientX);
        else hide();
    });
    document.addEventListener('pointerdown', function (event) {
        const svg = event.target instanceof Element ? event.target.closest('svg.app-equity-plot') : null;
        if (svg) show(svg, event.clientX); // a tap on a touch screen
    });
    document.documentElement.addEventListener('mouseleave', hide); // the mouse left the window
    window.addEventListener('blur', hide);

    // Full screen: the browser's own full-screen mode where it exists; otherwise (e.g. iPhone Safari,
    // which only allows it for videos) the card is stretched over the window with CSS.
    window.appEquityChart = {
        toggleFullscreen: function (button) {
            const card = button.closest('.app-equity-card');
            if (!card) return;
            hide();
            if (document.fullscreenElement) {
                document.exitFullscreen();
            } else if (card.classList.contains('is-maximized')) {
                card.classList.remove('is-maximized');
            } else if (card.requestFullscreen && document.fullscreenEnabled) {
                // Some embedded browsers neither grant nor refuse the request - fall back if nothing happened.
                let settled = false;
                card.requestFullscreen().then(
                    function () { settled = true; },
                    function () { settled = true; card.classList.add('is-maximized'); });
                setTimeout(function () {
                    if (!settled && document.fullscreenElement !== card) card.classList.add('is-maximized');
                }, 800);
            } else {
                card.classList.add('is-maximized');
            }
        }
    };

    // Entering or leaving real full screen (Esc, the button, the browser's own UI) clears the fallback.
    document.addEventListener('fullscreenchange', function () {
        hide();
        document.querySelectorAll('.app-equity-card.is-maximized').forEach(function (card) { card.classList.remove('is-maximized'); });
    });

    // Esc also closes the CSS fallback (the browser handles Esc for real full screen itself).
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        hide(); // the chart changes size, so the read-out would be left in the wrong place
        document.querySelectorAll('.app-equity-card.is-maximized').forEach(function (card) { card.classList.remove('is-maximized'); });
    });
})();
