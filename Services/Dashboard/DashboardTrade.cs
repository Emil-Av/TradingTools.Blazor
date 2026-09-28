using Shared.Enums;
using SharedEnums.Enums;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>
    /// A closed SRS/Espresso trade flattened for the dashboard.
    /// </summary>
    /// <param name="Points">
    /// Signed result in points of the original position (add-ons not included, see <see cref="TotalPoints"/>):
    /// positive for a win, negative for a loss, 0 for breakeven.
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
        /// <summary>Positions added to the trade while it was open; they count towards this one trade.</summary>
        public IReadOnlyList<DashboardAddOn> AddOns { get; init; } = [];

        /// <summary>
        /// Points of the whole trade: the original position's plus every add-on's. Null when any of
        /// them is unknown, so a partly recorded trade isn't shown as a smaller result than it was.
        /// </summary>
        public double? TotalPoints =>
            Points is { } points && AddOns.All(a => a.Points is not null)
                ? points + AddOns.Sum(a => a.Points!.Value)
                : null;

        /// <summary>
        /// Euro result of the whole trade: the original position's points × volume plus each
        /// add-on's points × volume. Null when any part is unknown.
        /// </summary>
        public double? Euro =>
            EuroOf(Points, Volume) is { } main && AddOns.All(a => a.Euro is not null)
                ? main + AddOns.Sum(a => a.Euro!.Value)
                : null;

        /// <summary>
        /// Points multiplied by the volume. Null when either is unknown - except 0 points
        /// (breakeven), which is 0 euro whatever the volume, so it's known even without a recorded volume.
        /// </summary>
        internal static double? EuroOf(double? points, double? volume) => points switch
        {
            null => null,
            0d => 0,
            { } p when volume is { } v => p * v,
            _ => null
        };
    }

    /// <param name="Points">Signed result in points: positive when the add-on made money. Null when a price is missing.</param>
    public sealed record DashboardAddOn(double? Points, double? Volume)
    {
        public double? Euro => DashboardTrade.EuroOf(Points, Volume);
    }
}
