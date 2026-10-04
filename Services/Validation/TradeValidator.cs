using System.Globalization;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Services.Validation
{
    /// <param name="Field">The <see cref="BaseTrade"/> (or research) property the problem is about, so a form can mark that field.</param>
    public sealed record TradeValidationIssue(string Field, string Message);

    /// <summary>
    /// Checks a single trade: every Trade Data and Research field is filled, the exit price is on the
    /// right side of the entry price for the direction and outcome, and the P&amp;L is the positive
    /// number of points between entry and exit. For a trade with add-ons the outcome is checked against
    /// the net result of the whole trade instead of the main exit, and each add-on is checked too.
    /// </summary>
    public static class TradeValidator
    {
        public static IReadOnlyList<TradeValidationIssue> Validate(BaseTrade trade)
        {
            var issues = new List<TradeValidationIssue>();

            CheckTradeData(trade, issues);
            CheckResearch(trade, issues);
            // A trade with add-ons has one net result, which decides its outcome - the main position's exit
            // can be on the "wrong" side for the outcome (it won, the add-ons lost more).
            if (trade.AddOns.Count == 0) CheckExitSide(trade, issues);
            else CheckNetOutcome(trade, issues);
            CheckPnl(trade, issues);
            CheckAddOns(trade, issues);

            return issues;
        }

        /// <summary>The <see cref="TradeValidationIssue.Field"/> of an add-on's property, e.g. "AddOns[0].ExitPrice".</summary>
        public static string AddOnField(int index, string property) => $"AddOns[{index}].{property}";

        #region Required fields

        private static void CheckTradeData(BaseTrade trade, List<TradeValidationIssue> issues)
        {
            if (trade.Date == default)
                issues.Add(new(nameof(BaseTrade.Date), "Date is missing."));

            if (string.IsNullOrWhiteSpace(trade.Symbol))
                issues.Add(new(nameof(BaseTrade.Symbol), "Symbol is missing."));

            if (!Enum.IsDefined(trade.Direction))
                issues.Add(new(nameof(BaseTrade.Direction), $"Direction has an unknown value ({(int)trade.Direction})."));

            if (!Enum.IsDefined(trade.Outcome))
                issues.Add(new(nameof(BaseTrade.Outcome), $"Outcome has an unknown value ({(int)trade.Outcome})."));

            if (!Enum.IsDefined(trade.TradeRating))
                issues.Add(new(nameof(BaseTrade.TradeRating), $"Rating has an unknown value ({(int)trade.TradeRating})."));

            CheckPositive(trade.Amount, nameof(BaseTrade.Amount), "Amount", issues);
            CheckPrice(trade.EntryPrice, nameof(BaseTrade.EntryPrice), "Entry price", issues);
            CheckPrice(trade.StopPrice, nameof(BaseTrade.StopPrice), "Stop price", issues);
            CheckPrice(trade.ExitPrice, nameof(BaseTrade.ExitPrice), "Exit price", issues);
            CheckPrice(trade.MaxPrice, nameof(BaseTrade.MaxPrice), "Max price", issues);

            if (trade.PnL is null)
                issues.Add(new(nameof(BaseTrade.PnL), "P&L is missing."));
        }

        private static void CheckPrice(double? price, string field, string label, List<TradeValidationIssue> issues)
        {
            if (price is null)
                issues.Add(new(field, $"{label} is missing."));
            else if (price <= 0)
                issues.Add(new(field, $"{label} must be above 0 (is {Format(price.Value)})."));
        }

        private static void CheckPositive(double? value, string field, string label, List<TradeValidationIssue> issues)
        {
            if (value is null)
                issues.Add(new(field, $"{label} is missing."));
            else if (value <= 0)
                issues.Add(new(field, $"{label} must be above 0 (is {Format(value.Value)})."));
        }

        /// <summary>
        /// The research fields (candle type, overnight range, flipped the switch) are non-nullable in the
        /// model, so they can't be empty - only an out-of-range candle type can be wrong.
        /// </summary>
        private static void CheckResearch(BaseTrade trade, List<TradeValidationIssue> issues)
        {
            ECandleType? candleType = trade switch
            {
                SRS srs => srs.CandleType,
                Espresso espresso => espresso.CandleType,
                BrunchBreak brunchBreak => brunchBreak.CandleType,
                _ => null
            };

            if (candleType is { } value && !Enum.IsDefined(value))
                issues.Add(new("CandleType", $"Candle type has an unknown value ({(int)value})."));
        }

        #endregion

        /// <summary>
        /// Long win / short loss: exit above entry. Long loss / short win: exit below entry.
        /// Breakeven has no required side.
        /// </summary>
        private static void CheckExitSide(BaseTrade trade, List<TradeValidationIssue> issues)
        {
            if (trade.EntryPrice is not > 0 || trade.ExitPrice is not > 0) return;
            if (trade.Outcome is not (EOutcome.Win or EOutcome.Loss)) return;
            if (!Enum.IsDefined(trade.Direction)) return;

            double entry = trade.EntryPrice.Value, exit = trade.ExitPrice.Value;
            bool exitMustBeAbove = (trade.Direction == EDirection.Long) == (trade.Outcome == EOutcome.Win);

            bool correct = exitMustBeAbove ? exit > entry : exit < entry;
            if (correct) return;

            string outcome = trade.Outcome == EOutcome.Win ? "Winning" : "Losing";
            string direction = trade.Direction == EDirection.Long ? "Long" : "Short";
            string side = exitMustBeAbove ? "above" : "below";

            issues.Add(new(nameof(BaseTrade.ExitPrice),
                $"{outcome} {direction} trade: the exit price ({Format(exit)}) must be {side} the entry price ({Format(entry)})."));
        }

        private static void CheckPnl(BaseTrade trade, List<TradeValidationIssue> issues)
        {
            if (trade.PnL is not { } pnl) return;

            if (pnl < 0)
            {
                issues.Add(new(nameof(BaseTrade.PnL), $"P&L must be a positive number of points (is {Format(pnl)})."));
                return;
            }

            if (TradePnl.Points(trade.EntryPrice, trade.ExitPrice) is { } expected && Math.Abs(pnl - expected) > TradePnl.Tolerance)
            {
                issues.Add(new(nameof(BaseTrade.PnL),
                    $"P&L should be {Format(expected)} points (|exit {Format(trade.ExitPrice!.Value)} − entry {Format(trade.EntryPrice!.Value)}|), but is {Format(pnl)}."));
            }
        }

        /// <summary>
        /// With add-ons the outcome is the sign of the whole trade's result in euro (see <see cref="TradeNet"/>):
        /// a winning main position doesn't make a Win when the add-ons lose more. Nothing to check while a
        /// price, the amount or a volume is still missing - those are reported on their own.
        /// </summary>
        private static void CheckNetOutcome(BaseTrade trade, List<TradeValidationIssue> issues)
        {
            if (!Enum.IsDefined(trade.Outcome) || TradeNet.Calculate(trade) is not { } net) return;
            if (net.Outcome == trade.Outcome) return;

            issues.Add(new(nameof(BaseTrade.Outcome),
                $"Net result is {DashboardFormat.SignedEuro(net.NetEuro)} (main {DashboardFormat.SignedEuro(net.MainEuro)}, add-ons {DashboardFormat.SignedEuro(net.AddOnsEuro)}), " +
                $"so the outcome should be {net.Outcome}, not {trade.Outcome}."));
        }

        /// <summary>
        /// Each add-on needs its entry, exit, volume and P&amp;L, with the P&amp;L worked out like the trade's
        /// own (the positive number of points between entry and exit). There's no exit-side rule: an
        /// add-on can lose on a winning trade (added high, trade closed lower but above the first entry).
        /// </summary>
        private static void CheckAddOns(BaseTrade trade, List<TradeValidationIssue> issues)
        {
            for (int i = 0; i < trade.AddOns.Count; i++)
            {
                var addOn = trade.AddOns[i];
                var addOnIssues = new List<TradeValidationIssue>();

                CheckPrice(addOn.EntryPrice, AddOnField(i, nameof(TradeAddOn.EntryPrice)), "entry price", addOnIssues);
                CheckPrice(addOn.ExitPrice, AddOnField(i, nameof(TradeAddOn.ExitPrice)), "exit price", addOnIssues);
                CheckPositive(addOn.Volume, AddOnField(i, nameof(TradeAddOn.Volume)), "volume", addOnIssues);

                string pnlField = AddOnField(i, nameof(TradeAddOn.PnL));
                if (addOn.PnL is not { } pnl)
                    addOnIssues.Add(new(pnlField, "P&L is missing."));
                else if (pnl < 0)
                    addOnIssues.Add(new(pnlField, $"P&L must be a positive number of points (is {Format(pnl)})."));
                else if (TradePnl.Points(addOn.EntryPrice, addOn.ExitPrice) is { } expected && Math.Abs(pnl - expected) > TradePnl.Tolerance)
                    addOnIssues.Add(new(pnlField,
                        $"P&L should be {Format(expected)} points (|exit {Format(addOn.ExitPrice!.Value)} − entry {Format(addOn.EntryPrice!.Value)}|), but is {Format(pnl)}."));

                // Prefixed so the Data check page and the summary above the tabs say which add-on it is.
                issues.AddRange(addOnIssues.Select(issue => issue with { Message = $"Add-on {i + 1}: {issue.Message}" }));
            }
        }

        private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
