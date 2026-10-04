using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Services.Calculation
{
    /// <summary>
    /// What a trade won or lost, with the instrument's spread taken off. The one place this is worked out: the
    /// dashboard, the History page and the Trades page all use it, so they can't disagree.
    ///
    /// The spread is what opening a position costs, in points, so it's taken off every position of the trade -
    /// the main position and each add-on - and then multiplied by that position's volume like the rest of its
    /// result. A breakeven therefore costs the spread too, and a small win can end up below zero.
    /// </summary>
    public static class TradeResults
    {
        /// <summary>
        /// The main position's result in points before the spread, signed. The recorded P&amp;L field holds the
        /// points as a positive number (the sign lives in Outcome), so it's the primary source. It falls back to
        /// |exit - entry| only when P&amp;L wasn't filled in - the Direction field isn't reliable enough to derive
        /// the sign from prices, so Outcome decides it either way. A win/loss with 0 points is treated as not
        /// filled in rather than as a 0-point result.
        ///
        /// With add-ons the Outcome belongs to the whole trade (its net result), not to the main position: the
        /// main position can win while the add-ons lose more. So there its sign comes from its prices and the
        /// direction, like the add-ons' do - and a "breakeven" trade still counts the main position's own points.
        /// </summary>
        public static double? MainPoints(BaseTrade trade)
        {
            if (trade.AddOns.Count > 0) return MainPositionPoints(trade);

            if (trade.Outcome == EOutcome.Breakeven) return 0;

            double? magnitude =
                trade.PnL is { } pnl && pnl != 0 ? Math.Abs(pnl)
                : trade.EntryPrice is > 0 && trade.ExitPrice is > 0 && trade.ExitPrice != trade.EntryPrice
                    ? Math.Abs(trade.ExitPrice.Value - trade.EntryPrice.Value)
                    : null;

            if (magnitude is null) return null;
            return trade.Outcome == EOutcome.Win ? magnitude : -magnitude;
        }

        /// <summary>The main position of a trade with add-ons: signed by its prices and direction; null when they don't say.</summary>
        private static double? MainPositionPoints(BaseTrade trade)
        {
            if (TradeNet.ResultSign(trade.EntryPrice, trade.ExitPrice, trade.Direction) is not { } sign) return null;
            if (sign == 0) return 0;

            double? magnitude = trade.PnL is { } pnl && pnl != 0 ? Math.Abs(pnl) : TradePnl.Points(trade.EntryPrice, trade.ExitPrice);
            return sign * magnitude;
        }

        /// <summary>The main position's result in points after the spread; null when the points aren't known.</summary>
        public static double? MainPointsAfterSpread(BaseTrade trade, double spread) =>
            AfterSpread(MainPoints(trade), spread);

        /// <summary>An add-on's result in points after the spread: signed from its prices and the trade's direction.</summary>
        public static double? AddOnPointsAfterSpread(TradeAddOn addOn, EDirection direction, double spread) =>
            AfterSpread(TradeAddOns.SignedPoints(addOn, direction), spread);

        private static double? AfterSpread(double? points, double spread) =>
            points is { } p ? Math.Round(p - spread, 6) : null;

        /// <summary>
        /// Points multiplied by the volume. Null when either is unknown - except 0 points (a breakeven without
        /// any spread), which is 0 euro whatever the volume, so it's known even without a recorded volume.
        /// </summary>
        public static double? EuroOf(double? points, double? volume) => points switch
        {
            null => null,
            0d => 0,
            { } p when volume is { } v => p * v,
            _ => null
        };

        /// <summary>The whole trade - main position and add-ons - with the spread taken off; null parts mean not known.</summary>
        public static TradeResultSummary Calculate(BaseTrade trade, double spread)
        {
            double? mainPoints = MainPointsAfterSpread(trade, spread);
            var addOns = trade.AddOns
                .Select(a => (Points: AddOnPointsAfterSpread(a, trade.Direction, spread), a.Volume))
                .ToList();

            // One unknown part makes the whole result unknown, so a partly recorded trade isn't shown as smaller than it was.
            double? points = mainPoints is { } m && addOns.All(a => a.Points is not null)
                ? m + addOns.Sum(a => a.Points!.Value)
                : null;

            double? euro = EuroOf(mainPoints, trade.Amount) is { } mainEuro && addOns.All(a => EuroOf(a.Points, a.Volume) is not null)
                ? mainEuro + addOns.Sum(a => EuroOf(a.Points, a.Volume)!.Value)
                : null;

            double totalVolume = (trade.Amount ?? 0) + addOns.Sum(a => a.Volume ?? 0);
            return new TradeResultSummary(points, euro, spread * totalVolume);
        }
    }

    /// <summary>A trade's result after the spread.</summary>
    /// <param name="Points">All positions together in points; null while any is unknown.</param>
    /// <param name="Euro">All positions together in euro; null while any is unknown.</param>
    /// <param name="SpreadEuro">What the spread cost in euro over all positions (already included in <paramref name="Euro"/>).</param>
    public sealed record TradeResultSummary(double? Points, double? Euro, double SpreadEuro);
}
