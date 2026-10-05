// Closes the <details>-based dropdowns (the topbar account menu and the Data check menu) when the user
// clicks outside them or presses Escape - a native <details> only closes by clicking its <summary> again.
// The listeners sit on the document, so they keep working across Blazor's enhanced navigation (which swaps
// the page content but never reloads this script). MudBlazor's own dropdowns close via their overlay,
// see Program.cs.
(function () {
    const selector = 'details.app-account-menu[open], details.app-nav-menu[open]';

    document.addEventListener('pointerdown', function (event) {
        document.querySelectorAll(selector).forEach(function (menu) {
            if (!menu.contains(event.target)) menu.removeAttribute('open');
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        document.querySelectorAll(selector).forEach(function (menu) {
            menu.removeAttribute('open');
        });
    });

    // Choosing an entry closes the Data check menu (enhanced navigation keeps the header in place).
    document.addEventListener('click', function (event) {
        const item = event.target.closest && event.target.closest('details.app-nav-menu a');
        if (item) item.closest('details').removeAttribute('open');
    });

    // The Data check panel is fixed-position (the nav scrolls and would clip it): place it under its summary.
    document.addEventListener('toggle', function (event) {
        const menu = event.target;
        if (!(menu instanceof HTMLDetailsElement) || !menu.classList.contains('app-nav-menu') || !menu.open) return;
        const rect = menu.querySelector('summary').getBoundingClientRect();
        const panel = menu.querySelector('.app-nav-menu-panel');
        panel.style.top = (rect.bottom + 8) + 'px';
        panel.style.left = Math.max(8, Math.min(rect.left, window.innerWidth - panel.offsetWidth - 8)) + 'px';
    }, true);
})();
