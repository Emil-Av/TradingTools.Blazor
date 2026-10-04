using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Services.AddOns
{
    /// <summary>The result of a whole trade in euro: the original position plus every add-on.</summary>
    /// <param name="MainEuro">The original position: its points (signed by its prices and the direction) × the trade's amount.</param>
    /// <param name="AddOnsEuro">Every add-on: its points (signed) × its volume.</param>
    public sealed record TradeNetResult(double MainEuro, double AddOnsEuro)
    {
        /// <summary>The whole trade, rounded to the cent.</summary>
        public double NetEuro => TradeNet.Cents(MainEuro + AddOnsEuro);

        /// <summary>What the trade's Outcome should be: a net gain is a Win, a net loss a Loss, exactly nothing a Breakeven.</summary>
        public EOutcome Outcome => NetEuro > 0 ? EOutcome.Win : NetEuro < 0 ? EOutcome.Loss : EOutcome.Breakeven;
    }

    /// <summary>
    /// A trade with add-ons is one trade with one result. A winning main position can be outweighed by a
    /// losing add-on (win 50 points, add-on loses 60 on the same volume: the trade lost), so the trade's
    /// Outcome is the sign of the net result in euro, not of the main position alone. Euro, because the
    /// positions can have different volumes.
    /// </summary>
    public static class TradeNet
    {
        public static double Cents(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Whether a position made money: +1 when it closed on the right side of its entry for the
        /// direction (a long above it, a short below it), -1 when on the wrong side, 0 when at the entry.
        /// Null when a price is missing or the direction isn't a known one.
        /// </summary>
        public static int? ResultSign(double? entryPrice, double? exitPrice, EDirection direction)
        {
            if (!Enum.IsDefined(direction) || entryPrice is not > 0 || exitPrice is not > 0) return null;
            if (exitPrice == entryPrice) return 0;
            return (exitPrice > entryPrice) == (direction == EDirection.Long) ? 1 : -1;
        }

        /// <summary>A position's result in points, signed from its prices and the direction; null when they don't say.</summary>
        public static double? SignedPoints(double? entryPrice, double? exitPrice, EDirection direction)
        {
            if (ResultSign(entryPrice, exitPrice, direction) is not { } sign) return null;
            return sign * TradePnl.Points(entryPrice, exitPrice);
        }

        /// <summary>
        /// The net result of the trade, or null when a part of it is unknown (a missing price, amount or
        /// volume) - a partly filled in trade has no result yet.
        /// </summary>
        public static TradeNetResult? Calculate(BaseTrade trade) =>
            Calculate(trade.Direction, trade.EntryPrice, trade.ExitPrice, trade.Amount, trade.AddOns);

        public static TradeNetResult? Calculate(EDirection direction, double? entryPrice, double? exitPrice, double? amount, IEnumerable<TradeAddOn> addOns)
        {
            if (amount is not > 0 || SignedPoints(entryPrice, exitPrice, direction) is not { } mainPoints) return null;

            double addOnsEuro = 0;
            foreach (var addOn in addOns)
            {
                if (addOn.Volume is not > 0 || SignedPoints(addOn.EntryPrice, addOn.ExitPrice, direction) is not { } points) return null;
                addOnsEuro += points * addOn.Volume.Value;
            }

            return new TradeNetResult(mainPoints * amount.Value, addOnsEuro);
        }
    }
}
