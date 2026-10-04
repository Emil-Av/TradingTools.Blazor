// Full screen for any element marked data-fullscreen (the dashboard's equity curve, the trade's screenshot
// carousel). A button inside it calls onclick="appFullscreen.toggle(this)". Everything runs in the browser, so
// Blazor's own event handlers inside the element (e.g. the carousel's previous/next buttons) keep working.
//
// The browser's own full-screen mode is used where it exists. Where it doesn't (iPhone Safari only allows it
// for videos) or a browser neither grants nor refuses it, the element is stretched over the window with CSS
// instead (class is-maximized, see app.css). Esc closes both.
(function () {
    const MAXIMIZED = 'is-maximized';

    // Lets other scripts react (the equity chart hides its hover read-out when the chart changes size).
    function changed() {
        document.dispatchEvent(new CustomEvent('appfullscreenchange'));
    }

    function clearMaximized() {
        document.querySelectorAll('[data-fullscreen].' + MAXIMIZED).forEach(function (element) {
            element.classList.remove(MAXIMIZED);
        });
    }

    window.appFullscreen = {
        toggle: function (button) {
            const element = button.closest('[data-fullscreen]');
            if (!element) return;
            changed();

            if (document.fullscreenElement) {
                document.exitFullscreen();
            } else if (element.classList.contains(MAXIMIZED)) {
                element.classList.remove(MAXIMIZED);
            } else if (element.requestFullscreen && document.fullscreenEnabled) {
                // Some embedded browsers neither grant nor refuse the request - fall back if nothing happened.
                let settled = false;
                element.requestFullscreen().then(
                    function () { settled = true; },
                    function () { settled = true; element.classList.add(MAXIMIZED); });
                setTimeout(function () {
                    if (!settled && document.fullscreenElement !== element) element.classList.add(MAXIMIZED);
                }, 800);
            } else {
                element.classList.add(MAXIMIZED);
            }
        }
    };

    // Entering or leaving real full screen (Esc, the button, the browser's own UI) clears the CSS fallback.
    document.addEventListener('fullscreenchange', function () {
        clearMaximized();
        changed();
    });

    document.addEventListener('keydown', function (event) {
        const open = document.fullscreenElement ?? document.querySelector('[data-fullscreen].' + MAXIMIZED);

        if (event.key === 'Escape') {
            // The browser handles Esc for real full screen itself; this closes the CSS fallback.
            changed();
            clearMaximized();
        } else if (open && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
            // Browse with the arrow keys while full screen: they press the element's own previous/next buttons.
            const button = open.querySelector(event.key === 'ArrowLeft' ? '[data-fullscreen-prev]' : '[data-fullscreen-next]');
            if (button) {
                event.preventDefault();
                button.click();
            }
        }
    });
})();
