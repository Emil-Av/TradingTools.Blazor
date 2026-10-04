using TradingTools.Blazor.Services.Settings;
using static TradingTools.Blazor.Services.Dashboard.DashboardStats;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>Which part of an account's history the equity curve shows.</summary>
    public enum EquityScope
    {
        /// <summary>From the last reset on (from the beginning while there was none) - the curve that started at the account amount.</summary>
        SinceLastReset,

        /// <summary>Everything, with every reset marked: the balance jumps to the amount entered at each one.</summary>
        AllHistory
    }

    /// <summary>An equity curve and the figures printed with it.</summary>
    /// <param name="StartBalance">The balance at the left end of the curve.</param>
    /// <param name="EndBalance">The balance at the right end.</param>
    /// <param name="NetEuro">What the trades in view won or lost together. A reset's jump to a new amount is not a result.</param>
    /// <param name="TradeCount">The trades on the curve (those with a euro result).</param>
    public sealed record EquityView(IReadOnlyList<EquityPoint> Points, double StartBalance, double EndBalance, double NetEuro, double MaxDrawdown, int TradeCount);

    /// <summary>
    /// Builds an account's equity curve from its trades, its account amount and its resets.
    ///
    /// A reset starts a new curve at the amount entered then. Trades created after it belong to the new curve
    /// ("from the next trade"); nothing is deleted, so <see cref="EquityScope.AllHistory"/> still draws the old
    /// curves, each starting where it started. A date range cuts the curve to those trade dates but keeps the real
    /// balance levels: it starts at the balance the account had just before the range.
    /// </summary>
    public static class EquityCurves
    {
        public static EquityView Build(IReadOnlyList<DashboardTrade> trades, AccountEquitySettings account, EquityScope scope, DateOnly? from = null, DateOnly? to = null)
        {
            var resets = account.Resets;
            int resetCount = resets.Count;

            // Curve k runs from reset k-1 to reset k; the last one is the current curve. A closed curve started at the
            // amount the account had when the next reset was made, the current one starts at the account amount.
            double StartOf(int curve) => curve < resetCount ? resets[curve].PreviousStartingBalance : account.Amount;

            int CurveOf(DashboardTrade trade)
            {
                int curve = 0;
                while (curve < resetCount && trade.CreatedAt > resets[curve].ResetAtUtc) curve++;
                return curve;
            }

            int firstCurve = scope == EquityScope.SinceLastReset ? resetCount : 0;
            var byCurve = trades.ToLookup(CurveOf);

            var points = new List<EquityPoint>();
            for (int curve = firstCurve; curve <= resetCount; curve++)
            {
                var curvePoints = EquityCurve(byCurve[curve], StartOf(curve));

                if (curve > firstCurve)
                {
                    // Where this curve began: the balance jumps to the amount entered at the reset.
                    curvePoints[0] = new EquityPoint(DateOnly.FromDateTime(resets[curve - 1].ResetAtUtc), StartOf(curve), IsReset: true);
                }

                points.AddRange(curvePoints);
            }

            bool InRange(DateOnly date) => (from is null || date >= from) && (to is null || date <= to);

            if (from is not null || to is not null)
            {
                double start = from is { } f
                    ? points.LastOrDefault(p => p.Date is { } d && d < f)?.Balance ?? points[0].Balance
                    : points[0].Balance;

                points = [new EquityPoint(null, start), .. points.Where(p => p.Date is { } d && InRange(d))];
            }

            var inView = trades.Where(t => CurveOf(t) >= firstCurve && InRange(t.Date)).ToList();
            double net = inView.Sum(t => t.Euro ?? 0);

            return new EquityView(
                points,
                points[0].Balance,
                points[^1].Balance,
                Cents(net),
                MaxDrawdown(points),
                inView.Count(t => t.Euro is not null));
        }
    }
}
