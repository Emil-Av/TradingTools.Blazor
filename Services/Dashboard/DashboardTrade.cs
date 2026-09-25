using Shared.Enums;
using SharedEnums.Enums;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>
    /// A closed SRS/Espresso trade flattened for the dashboard.
    /// </summary>
    /// <param name="Points">
    /// Signed result in points: positive for a win, negative for a loss, 0 for breakeven.
    /// Null when the trade has no usable points recorded (see <see cref="DashboardService"/>).
    /// </param>
    /// <param name="SampleSizeNumber">1-based position of the sample size among those with the same account type, strategy and timeframe.</param>
    public sealed record DashboardTrade(
        int Id,
        DateOnly Date,
        DateTime CreatedAt,
        string Symbol,
        EDirection Direction,
        Strategy Strategy,
        TimeFrame TimeFrame,
        SampleSizeType AccountType,
        int SampleSizeId,
        int SampleSizeNumber,
        double? Volume,
        double? Points,
        EOutcome Outcome)
    {
        /// <summary>
        /// Euro result of the trade: points multiplied by the volume. Null when either is unknown -
        /// except 0 points (breakeven), which is 0 euro whatever the volume, so it's known even
        /// without a recorded volume.
        /// </summary>
        public double? Euro => Points switch
        {
            null => null,
            0d => 0,
            { } points when Volume is { } volume => points * volume,
            _ => null
        };
    }
}
