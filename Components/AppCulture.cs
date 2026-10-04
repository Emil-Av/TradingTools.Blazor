using System.Globalization;

namespace TradingTools.Blazor.Components
{
    /// <summary>
    /// Cultures for the UI. Only the UI culture is pinned app-wide (Program.cs), so number and date
    /// formatting otherwise follow the OS (German) - which gave the date pickers German weekday names
    /// under an English month title.
    /// </summary>
    public static class AppCulture
    {
        /// <summary>For the date pickers: English names, dates typed and shown as MM/DD/YYYY.</summary>
        public static readonly CultureInfo DatePicker = CultureInfo.GetCultureInfo("en-US");
    }
}
