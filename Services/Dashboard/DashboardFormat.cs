using System.Globalization;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>
    /// Display formatting for dashboard numbers. Uses the invariant culture on purpose so the
    /// output doesn't depend on the server's OS locale (German on Windows dev, whatever the Linux
    /// host is set to in production).
    /// </summary>
    public static class DashboardFormat
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>Signed euro amount, e.g. "+€1,234.50" / "−€80.00"; "€0.00" for anything that rounds to zero.</summary>
        public static string SignedEuro(double value)
        {
            double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return $"{Sign(rounded)}€{Math.Abs(rounded).ToString("N2", Culture)}";
        }

        /// <summary>Unsigned euro amount, e.g. "€3,234.50".</summary>
        public static string Euro(double value) => $"€{value.ToString("N2", Culture)}";

        /// <summary>Signed points, e.g. "+35.5 pts"; "0.0 pts" for anything that rounds to zero.</summary>
        public static string SignedPoints(double value)
        {
            double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
            return $"{Sign(rounded)}{Math.Abs(rounded).ToString("N1", Culture)} pts";
        }

        /// <summary>A 0..1 ratio as a percentage, e.g. "54.2%"; "—" when there's nothing to compute.</summary>
        public static string Percent(double? ratio) => ratio is { } r ? $"{(r * 100).ToString("N1", Culture)}%" : "—";

        public static string Number(double value) => value.ToString("0.##", Culture);

        public static string ShortDate(DateOnly date) => date.ToString("dd MMM yyyy", Culture);

        /// <summary>Expects an already rounded value, so a sign is never shown on something displayed as zero.</summary>
        private static string Sign(double rounded) => rounded > 0 ? "+" : rounded < 0 ? "−" : "";
    }
}
