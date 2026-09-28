// Closes the <details>-based dropdowns (the topbar account menu) when the user clicks outside them or
// presses Escape - a native <details> only closes by clicking its <summary> again. The listeners sit on
// the document, so they keep working across Blazor's enhanced navigation (which swaps the page content
// but never reloads this script). MudBlazor's own dropdowns close via their overlay, see Program.cs.
(function () {
    const selector = 'details.app-account-menu[open]';

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
})();
