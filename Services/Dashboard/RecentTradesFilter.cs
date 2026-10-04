namespace TradingTools.Blazor.Services.Dashboard
{
    public enum PeriodFilter { AllTime, LastWeek, LastMonth, LastXTrades }

    /// <summary>
    /// The date filters of the dashboard's Recent Trades grid. Trades are expected oldest first, and
    /// are returned in the same order.
    /// </summary>
    public static class RecentTradesFilter
    {
        /// <summary>
        /// The date range first, then the period within it - so "Last X trades" with a range is the
        /// last X trades of that range.
        /// </summary>
        public static IEnumerable<DashboardTrade> Apply(
            IEnumerable<DashboardTrade> trades, DateOnly? from, DateOnly? to, PeriodFilter period, int lastX, DateOnly today) =>
            InPeriod(InDateRange(trades, from, to), period, lastX, today);

        /// <summary>Trades dated from <paramref name="from"/> to <paramref name="to"/>, both days included. A missing bound is open.</summary>
        public static IEnumerable<DashboardTrade> InDateRange(IEnumerable<DashboardTrade> trades, DateOnly? from, DateOnly? to) =>
            trades.Where(t => (from is null || t.Date >= from) && (to is null || t.Date <= to));

        public static IEnumerable<DashboardTrade> InPeriod(IEnumerable<DashboardTrade> trades, PeriodFilter period, int lastX, DateOnly today) =>
            period switch
            {
                PeriodFilter.LastWeek => trades.Where(t => t.Date >= today.AddDays(-7)),
                PeriodFilter.LastMonth => trades.Where(t => t.Date >= today.AddMonths(-1)),
                PeriodFilter.LastXTrades => trades.TakeLast(Math.Max(1, lastX)),
                _ => trades
            };
    }
}
