using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Tests
{
    /// <summary>
    /// Builders for <see cref="DashboardTrade"/>. Points are passed already signed, exactly as
    /// <see cref="DashboardService"/> produces them (+ win, - loss, 0 breakeven).
    /// </summary>
    internal static class TestTrades
    {
        /// <summary>
        /// Tolerance for arithmetic that isn't expected to be exact (averages, ratios). Money totals
        /// are asserted exactly - rounding them to the cent is the behaviour under test.
        /// </summary>
        public const double Precision = 1e-10;

        private static int _nextId;

        public static DashboardTrade Trade(
            EOutcome outcome,
            double? points,
            double? volume = 1,
            DateOnly? date = null,
            string symbol = "DAX",
            Strategy strategy = Strategy.SRS) =>
            new(
                Interlocked.Increment(ref _nextId),
                date ?? new DateOnly(2026, 1, 1),
                DateTime.UnixEpoch,
                symbol,
                EDirection.Long,
                strategy,
                TimeFrame.M15,
                SampleSizeType.DemoTrading,
                SampleSizeId: 1,
                SampleSizeNumber: 1,
                volume,
                points,
                outcome);

        public static DashboardTrade Win(double points = 10, double? volume = 1, DateOnly? date = null, string symbol = "DAX", Strategy strategy = Strategy.SRS) =>
            Trade(EOutcome.Win, points, volume, date, symbol, strategy);

        /// <param name="points">Signed (negative) loss in points.</param>
        public static DashboardTrade Loss(double points = -10, double? volume = 1, DateOnly? date = null, string symbol = "DAX", Strategy strategy = Strategy.SRS) =>
            Trade(EOutcome.Loss, points, volume, date, symbol, strategy);

        public static DashboardTrade Breakeven(double? volume = 1, string symbol = "DAX", Strategy strategy = Strategy.SRS) =>
            Trade(EOutcome.Breakeven, 0, volume, symbol: symbol, strategy: strategy);

        /// <summary>Win/loss with no points recorded (the service returns null points for those).</summary>
        public static DashboardTrade WithoutPoints(EOutcome outcome, double? volume = 1) =>
            Trade(outcome, null, volume);
    }
}
