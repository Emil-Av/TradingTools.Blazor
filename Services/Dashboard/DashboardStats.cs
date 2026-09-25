using Shared.Enums;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>
    /// Pure calculations behind the dashboard. Every method expects trades in chronological order
    /// (see <see cref="DashboardService.GetTradesAsync"/>).
    /// </summary>
    public static class DashboardStats
    {
        /// <summary>The account's starting value in euro.</summary>
        public const double StartingBalance = 2000;

        public sealed record Streak(EOutcome? Outcome, int Length);

        public sealed record Summary(
            int Wins,
            int Losses,
            int Breakevens,
            double? WinRate,
            double NetEuro,
            double Balance,
            double? AvgWinPoints,
            double? AvgLossPoints,
            Streak CurrentStreak,
            int BestWinStreak,
            int WorstLossStreak,
            int TradesWithoutResult);

        public sealed record EquityPoint(DateOnly? Date, double Balance);

        public sealed record Breakdown(string Name, int Trades, int Wins, int Losses, double? WinRate, double NetEuro);

        public static Summary Summarize(IReadOnlyList<DashboardTrade> trades)
        {
            int wins = trades.Count(t => t.Outcome == EOutcome.Win);
            int losses = trades.Count(t => t.Outcome == EOutcome.Loss);
            int breakevens = trades.Count(t => t.Outcome == EOutcome.Breakeven);

            // Breakeven trades are left out of the win rate: it's wins out of decided trades.
            double? winRate = wins + losses == 0 ? null : (double)wins / (wins + losses);

            double netEuro = trades.Sum(t => t.Euro ?? 0); // unrounded; rounded to cents only when reported

            var winPoints = trades.Where(t => t.Outcome == EOutcome.Win && t.Points is not null).Select(t => t.Points!.Value).ToList();
            var lossPoints = trades.Where(t => t.Outcome == EOutcome.Loss && t.Points is not null).Select(t => t.Points!.Value).ToList();

            var (current, bestWin, worstLoss) = Streaks(trades);

            return new Summary(
                wins,
                losses,
                breakevens,
                winRate,
                Cents(netEuro),
                Cents(StartingBalance + netEuro),
                winPoints.Count == 0 ? null : winPoints.Average(),
                lossPoints.Count == 0 ? null : lossPoints.Average(),
                current,
                bestWin,
                worstLoss,
                trades.Count(t => t.Euro is null));
        }

        /// <summary>
        /// Breakeven trades are skipped entirely. A trade whose outcome differs from the previous
        /// (non-breakeven) trade starts a new streak.
        /// </summary>
        public static (Streak Current, int BestWin, int WorstLoss) Streaks(IEnumerable<DashboardTrade> trades)
        {
            EOutcome? outcome = null;
            int length = 0, bestWin = 0, worstLoss = 0;

            foreach (var trade in trades)
            {
                if (trade.Outcome == EOutcome.Breakeven) 
                    continue;

                if (trade.Outcome == outcome)
                {
                    length++;
                }
                else
                {
                    outcome = trade.Outcome;
                    length = 1;
                }

                if (outcome == EOutcome.Win) 
                    bestWin = Math.Max(bestWin, length);

                else 
                    worstLoss = Math.Max(worstLoss, length);
            }

            return (new Streak(outcome, length), bestWin, worstLoss);
        }

        /// <summary>
        /// Account balance after each trade, starting from <see cref="StartingBalance"/>. The first
        /// point is the starting balance itself (no date). Trades without a euro result don't move
        /// the balance and don't add a point.
        /// </summary>
        public static List<EquityPoint> EquityCurve(IEnumerable<DashboardTrade> trades)
        {
            var points = new List<EquityPoint> { new(null, StartingBalance) };

            // Accumulate the net result exactly the way Summarize does (from 0, in the same order) and
            // add the starting balance per point, so the last point always equals Summary.Balance.
            double net = 0;
            foreach (var trade in trades)
            {
                if (trade.Euro is not { } euro) continue;
                net += euro;
                points.Add(new EquityPoint(trade.Date, Cents(StartingBalance + net)));
            }

            return points;
        }

        /// <summary>Largest peak-to-trough drop of the balance, in euro (0 or positive).</summary>
        public static double MaxDrawdown(IEnumerable<EquityPoint> curve)
        {
            double peak = double.MinValue, maxDrawdown = 0;
            foreach (var point in curve)
            {
                peak = Math.Max(peak, point.Balance);
                maxDrawdown = Math.Max(maxDrawdown, peak - point.Balance);
            }
            return maxDrawdown;
        }

        public static List<Breakdown> BreakdownBy(IEnumerable<DashboardTrade> trades, Func<DashboardTrade, string> key) =>
            [.. trades
                .GroupBy(key)
                .Select(group =>
                {
                    int wins = group.Count(t => t.Outcome == EOutcome.Win);
                    int losses = group.Count(t => t.Outcome == EOutcome.Loss);
                    return new Breakdown(
                        group.Key,
                        group.Count(),
                        wins,
                        losses,
                        wins + losses == 0 ? null : (double)wins / (wins + losses),
                        Cents(group.Sum(t => t.Euro ?? 0)));
                })
                .OrderByDescending(b => b.Trades)];

        /// <summary>
        /// Money is reported to the cent. Summing doubles leaves residue (0.3 - 0.1 - 0.2 is -2.8e-17),
        /// which would otherwise show a flat result as a negative "−€0.00". Adding 0.0 turns a
        /// rounded -0.0 into +0.0.
        /// </summary>
        private static double Cents(double euro) => Math.Round(euro, 2, MidpointRounding.AwayFromZero) + 0.0;
    }
}
