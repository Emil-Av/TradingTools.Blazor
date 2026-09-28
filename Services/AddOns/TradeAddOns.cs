using Models.Trades;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Services.AddOns
{
    /// <summary>Helpers for the add-ons of a trade (see <see cref="TradeAddOn"/>).</summary>
    public static class TradeAddOns
    {
        /// <summary>The navigation name for a repository's includeProperties.</summary>
        public const string Include = nameof(BaseTrade.AddOns);

        /// <summary>
        /// Puts the add-ons in the order they were added. EF doesn't order an included collection, and
        /// the pages, the validation messages ("Add-on 2: ...") and the numbering all rely on it.
        /// Unsaved add-ons (Id 0) stay last, in the order they were created.
        /// </summary>
        public static void SortAddOns(this BaseTrade trade) =>
            trade.AddOns = [.. trade.AddOns.OrderBy(a => a.Id == 0).ThenBy(a => a.Id)];

        /// <summary>
        /// A new add-on for the form: the exit price starts as the trade's own exit (the usual case,
        /// where the whole position is closed at once) with the P&amp;L left to be worked out once the
        /// entry is typed.
        /// </summary>
        public static TradeAddOn NewFor(BaseTrade trade) => new() { BaseTradeId = trade.Id, ExitPrice = trade.ExitPrice };

        /// <summary>
        /// The add-on's result in points, signed: positive when it made money. Unlike the trade itself
        /// (whose sign comes from its Outcome), an add-on has no outcome of its own, so the sign comes
        /// from the trade's direction and the add-on's prices: a long add-on wins when it's closed
        /// above its entry, a short one when it's closed below. Null when either price is missing.
        /// </summary>
        public static double? SignedPoints(TradeAddOn addOn, EDirection direction)
        {
            if (TradePnl.Points(addOn.EntryPrice, addOn.ExitPrice) is not { } points) return null;

            bool closedAbove = addOn.ExitPrice > addOn.EntryPrice;
            bool won = direction == EDirection.Long ? closedAbove : !closedAbove;
            return won || points == 0 ? points : -points;
        }
    }
}
