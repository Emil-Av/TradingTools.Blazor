using Models;

namespace TradingTools.Blazor.Services
{
    /// <summary>
    /// "Sample size #n" as the Trades page shows it: the position of a sample size among all sample
    /// sizes with the same account type, strategy and timeframe, oldest (lowest id) first.
    /// </summary>
    public static class SampleSizeNumbering
    {
        /// <returns>Sample size id -> 1-based number.</returns>
        public static Dictionary<int, int> Number(IEnumerable<SampleSize> sampleSizes) =>
            sampleSizes
                .DistinctBy(s => s.Id)
                .GroupBy(s => (s.SampleSizeType, s.Strategy, s.TimeFrame))
                .SelectMany(group => group.OrderBy(s => s.Id).Select((s, index) => (s.Id, Number: index + 1)))
                .ToDictionary(x => x.Id, x => x.Number);
    }
}
