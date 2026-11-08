using Shared.Enums;
using TradingTools.Blazor.Services.Calculation;
using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Services
{
    /// <summary>
    /// Where a step through the trades leads: another trade of the sample size on screen, or another sample size
    /// (its first or last trade).
    /// </summary>
    /// <param name="TradeIndex">The trade to show in the sample size on screen; null when the step goes to another sample size.</param>
    /// <param name="SampleSizePosition">The sample size to open; null when the step stays in the one on screen.</param>
    /// <param name="AtLastTrade">In the other sample size, start at its last trade (going back) instead of its first.</param>
    public sealed record BrowseStep(int? TradeIndex, int? SampleSizePosition = null, bool AtLastTrade = false);

    /// <summary>What the full-screen result box shows for a trade.</summary>
    /// <param name="Points">The result in points after the spread, signed ("+12.5 pts"); null while it isn't known.</param>
    /// <param name="Euro">The result in euro after the spread, signed ("+€25.00"); null while it isn't known.</param>
    /// <param name="ToneClass">app-gain, app-loss or app-flat, for the colour.</param>
    public sealed record ResultBadge(string Outcome, string? Points, string? Euro, string ToneClass);

    /// <summary>
    /// Going through all the trades in full screen, to review them: the trade arrows go on into the next or previous
    /// sample size at the ends, and the sample size arrows open the first trade of a sample size.
    /// </summary>
    public static class TradeBrowsing
    {
        /// <param name="acrossSampleSizes">False when the trades on screen already come from several sample sizes (a date range).</param>
        public static BrowseStep? NextTrade(int tradeIndex, int tradeCount, int sampleSizePosition, int sampleSizeCount, bool acrossSampleSizes = true)
        {
            if (tradeIndex < tradeCount - 1) return new BrowseStep(tradeIndex + 1);
            if (acrossSampleSizes && sampleSizePosition >= 0 && sampleSizePosition < sampleSizeCount - 1) return new BrowseStep(null, sampleSizePosition + 1);
            return null;
        }

        public static BrowseStep? PrevTrade(int tradeIndex, int sampleSizePosition, bool acrossSampleSizes = true)
        {
            if (tradeIndex > 0) return new BrowseStep(tradeIndex - 1);
            if (acrossSampleSizes && sampleSizePosition > 0) return new BrowseStep(null, sampleSizePosition - 1, AtLastTrade: true);
            return null;
        }

        /// <summary>The next sample size, from its first trade.</summary>
        public static BrowseStep? NextSampleSize(int sampleSizePosition, int sampleSizeCount) =>
            sampleSizePosition >= 0 && sampleSizePosition < sampleSizeCount - 1 ? new BrowseStep(null, sampleSizePosition + 1) : null;

        /// <summary>The previous sample size, from its first trade too: a sample size is reviewed from the start.</summary>
        public static BrowseStep? PrevSampleSize(int sampleSizePosition) =>
            sampleSizePosition > 0 ? new BrowseStep(null, sampleSizePosition - 1) : null;

        /// <summary>
        /// The trade's outcome and its result after the spread, as on the Trades page and the dashboard. The colour
        /// follows the result in euro when it is known, otherwise the outcome.
        /// </summary>
        public static ResultBadge Result(EOutcome outcome, TradeResultSummary? result)
        {
            string? points = result?.Points is { } p ? $"{DashboardFormat.SignedPoints(p)}" : null;
            string? euro = result?.Euro is { } e ? DashboardFormat.SignedEuro(e) : null;
            string tone = result?.Euro is { } value
                ? DashboardFormat.ToneClass(value)
                : outcome switch { EOutcome.Win => "app-gain", EOutcome.Loss => "app-loss", _ => "app-flat" };
            return new ResultBadge(outcome.ToString(), points, euro, tone);
        }
    }
}
