namespace TradingTools.Blazor.Services.Settings
{
    /// <summary>
    /// The spread per instrument, in points. Looked up by the symbol as it's stored on a trade (any case, extra
    /// spaces ignored); an instrument without a spread has none (0).
    /// </summary>
    public sealed class SpreadTable
    {
        public static readonly SpreadTable None = new([]);

        private readonly Dictionary<string, double> _spreads;

        public SpreadTable(IEnumerable<KeyValuePair<string, double>> spreads) =>
            _spreads = spreads.ToDictionary(pair => Normalize(pair.Key), pair => pair.Value);

        /// <summary>The spread of the instrument in points; 0 when it has none or the symbol is missing.</summary>
        public double For(string? symbol) =>
            string.IsNullOrWhiteSpace(symbol) ? 0 : _spreads.GetValueOrDefault(Normalize(symbol));

        private static string Normalize(string symbol) => symbol.Trim().ToUpperInvariant();
    }
}
