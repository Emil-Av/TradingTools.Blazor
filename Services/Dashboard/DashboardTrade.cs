using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Calculation;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>
    /// A closed SRS/Espresso trade flattened for the dashboard.
    /// </summary>
    /// <param name="Points">
    /// Signed result in points of the original position (add-ons not included, see <see cref="TotalPoints"/>),
    /// after the instrument's spread (see <see cref="TradeResults"/>): positive for a win, negative for a loss,
    /// minus the spread for a breakeven - and a small win can end up below zero.
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
            TradeResults.EuroOf(Points, Volume) is { } main && AddOns.All(a => a.Euro is not null)
                ? main + AddOns.Sum(a => a.Euro!.Value)
                : null;
    }

    /// <param name="Points">Signed result in points: positive when the add-on made money. Null when a price is missing.</param>
    public sealed record DashboardAddOn(double? Points, double? Volume)
    {
        public double? Euro => TradeResults.EuroOf(Points, Volume);
    }
}
