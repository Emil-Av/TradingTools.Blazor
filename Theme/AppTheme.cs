using MudBlazor;

namespace TradingTools.Blazor.Theme
{
    /// <summary>
    /// Single source of truth for the app's color palette. Change a value here and it flows to
    /// both the MudBlazor theme (<see cref="Theme"/>) and the custom CSS variables emitted by
    /// <c>Components/App.razor</c> — nothing else in the app should hard-code a color.
    /// </summary>
    public static class AppTheme
    {
        // Backgrounds (page canvas is heaviest black; cards sit a step lighter for contrast)
        public const string Background = "#0a0f0d";
        public const string Surface = "#16211b";
        public const string SurfaceVariant = "#1c2a22";
        public const string SurfaceHighlight = "#26362d";

        // Borders
        public const string Border = "#2c3b34";
        public const string BorderAccent = "#22c55e66";

        // Text
        public const string TextPrimary = "#f2f7f4";
        public const string TextSecondary = "#9fb0a7";
        public const string TextDisabled = "#5f7168";

        // Accent (primary actions, profit) and loss
        public const string Accent = "#19d472";
        public const string AccentDarken = "#12a85a";
        public const string AccentLighten = "#4fe897";
        public const string AccentContrastText = "#052813";
        public const string Error = "#ef4a3a";

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
                Warning = "#f0b429",
                Info = "#3aa0ef",
                TextPrimary = TextPrimary,
                TextSecondary = TextSecondary,
                TextDisabled = TextDisabled,
                ActionDefault = TextSecondary,
                ActionDisabled = TextDisabled,
                Divider = Border,
                LinesDefault = Border,
                TableLines = Border,
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
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "12px",
            },
        };
    }
}
