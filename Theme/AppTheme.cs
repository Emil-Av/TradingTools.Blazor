using MudBlazor;

namespace TradingTools.Blazor.Theme
{
    /// <summary>
    /// Single source of truth for the app's color palette. Change a value here and it flows to
    /// both the MudBlazor theme (<see cref="Theme"/>) and the custom CSS variables emitted by
    /// <c>Components/Layout/ThemeStyles.razor</c> — nothing else in the app should hard-code a color.
    /// </summary>
    /// <remarks>
    /// "Mirror ledger" palette: gains and losses are a complementary pair (blue 207° / orange 27°)
    /// rather than green/red, so they stay distinguishable with red-green color blindness.
    /// </remarks>
    public static class AppTheme
    {
        // Backgrounds: cool graphite, cards a step lighter, "sunk" wells (inputs, hover rows) a step more
        public const string Background = "#0d1217";
        public const string Surface = "#141b22";
        public const string SurfaceVariant = "#1b242d";
        public const string SurfaceHighlight = "#25303b";

        // Borders
        public const string Border = "#25303b";
        public const string BorderAccent = "#34414e";

        // Text
        public const string TextPrimary = "#e2e8ee";
        public const string TextSecondary = "#8694a2";
        public const string TextDisabled = "#56626f";

        // Accent = gain (primary actions, profit). Error = loss.
        public const string Accent = "#5aa8ea";
        public const string AccentDarken = "#3e8fd3";
        public const string AccentLighten = "#86c0f0";
        public const string AccentContrastText = "#06121c";
        public const string Error = "#f08b3e";

        // The "spine": the one inverted, high-contrast element (win rate block, primary button).
        public const string Spine = TextPrimary;
        public const string SpineText = Background;

        public const string FontDisplay = "'Bodoni Moda', Didot, 'Bodoni 72', Georgia, serif";
        public const string FontUi = "'Schibsted Grotesk', system-ui, -apple-system, 'Segoe UI', sans-serif";
        public const string FontNumbers = "'Spline Sans Mono', ui-monospace, 'Cascadia Mono', Consolas, monospace";

        private static readonly string[] UiFontStack = ["Schibsted Grotesk", "system-ui", "-apple-system", "Segoe UI", "sans-serif"];

        public static MudTheme Theme => new()
        {
            PaletteDark = new PaletteDark
            {
                Black = "#000000",
                White = TextPrimary,
                Background = Background,
                Surface = Surface,
                DrawerBackground = Surface,
                DrawerText = TextSecondary,
                AppbarBackground = Background,
                AppbarText = TextPrimary,
                Primary = Accent,
                PrimaryDarken = AccentDarken,
                PrimaryLighten = AccentLighten,
                PrimaryContrastText = AccentContrastText,
                Success = Accent,
                SuccessContrastText = AccentContrastText,
                Error = Error,
                Warning = "#e8c547",
                Info = Accent,
                TextPrimary = TextPrimary,
                TextSecondary = TextSecondary,
                TextDisabled = TextDisabled,
                ActionDefault = TextSecondary,
                ActionDisabled = TextDisabled,
                Divider = Border,
                LinesDefault = Border,
                LinesInputs = BorderAccent,
                TableLines = Border,
                TableHover = SurfaceVariant,
            },
            PaletteLight = new PaletteLight
            {
                // The app is dark-only for now; light palette mirrors dark so a future
                // light-mode toggle only needs new values here, not new plumbing.
                Background = Background,
                Surface = Surface,
                Primary = Accent,
                Error = Error,
                TextPrimary = TextPrimary,
                TextSecondary = TextSecondary,
            },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = UiFontStack },
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "8px",
            },
        };
    }
}
