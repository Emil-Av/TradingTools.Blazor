using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>The From/To and Period filters of the dashboard's Recent Trades grid.</summary>
    public class RecentTradesFilterTests
    {
        private static readonly DateOnly Today = new(2026, 9, 28);

        private static DashboardTrade On(int month, int day) => Win(date: new DateOnly(2026, month, day));

        private static readonly DashboardTrade Aug31 = On(8, 31);
        private static readonly DashboardTrade Sep01 = On(9, 1);
        private static readonly DashboardTrade Sep10 = On(9, 10);
        private static readonly DashboardTrade Sep20 = On(9, 20);
        private static readonly DashboardTrade Sep21 = On(9, 21);
        private static readonly DashboardTrade Sep28 = On(9, 28);

        private static readonly DashboardTrade[] Trades = [Aug31, Sep01, Sep10, Sep20, Sep21, Sep28];

        [Fact]
        public void No_dates_keeps_every_trade() =>
            RecentTradesFilter.InDateRange(Trades, null, null).Should().Equal(Trades);

        [Fact]
        public void Both_bound_days_are_included()
        {
            RecentTradesFilter.InDateRange(Trades, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20))
                .Should().Equal(Sep01, Sep10, Sep20);
        }

        [Fact]
        public void From_only_keeps_that_day_and_later() =>
            RecentTradesFilter.InDateRange(Trades, new DateOnly(2026, 9, 21), null).Should().Equal(Sep21, Sep28);

        [Fact]
        public void To_only_keeps_that_day_and_earlier() =>
            RecentTradesFilter.InDateRange(Trades, null, new DateOnly(2026, 9, 1)).Should().Equal(Aug31, Sep01);

        [Fact]
        public void Same_from_and_to_is_a_single_day() =>
            RecentTradesFilter.InDateRange(Trades, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10)).Should().Equal(Sep10);

        [Fact]
        public void Range_without_trades_is_empty() =>
            RecentTradesFilter.InDateRange(Trades, new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 19)).Should().BeEmpty();

        [Fact]
        public void All_time_keeps_every_trade() =>
            RecentTradesFilter.InPeriod(Trades, PeriodFilter.AllTime, 5, Today).Should().Equal(Trades);

        [Fact]
        public void Last_week_starts_seven_days_back_inclusive() =>
            RecentTradesFilter.InPeriod(Trades, PeriodFilter.LastWeek, 5, Today).Should().Equal(Sep21, Sep28);

        [Fact]
        public void Last_month_starts_one_calendar_month_back_inclusive() =>
            // 28 Sep - 1 month = 28 Aug, so 31 Aug is in.
            RecentTradesFilter.InPeriod(Trades, PeriodFilter.LastMonth, 5, Today).Should().Equal(Trades);

        [Fact]
        public void Last_x_trades_keeps_the_newest_in_order() =>
            RecentTradesFilter.InPeriod(Trades, PeriodFilter.LastXTrades, 2, Today).Should().Equal(Sep21, Sep28);

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void Last_x_below_one_keeps_one_trade(int lastX) =>
            RecentTradesFilter.InPeriod(Trades, PeriodFilter.LastXTrades, lastX, Today).Should().Equal(Sep28);

        [Fact]
        public void Last_x_trades_counts_within_the_date_range()
        {
            // The last 2 trades up to 10 Sep - not the last 2 overall, cut down to the range afterwards (which would be none).
            RecentTradesFilter.Apply(Trades, null, new DateOnly(2026, 9, 10), PeriodFilter.LastXTrades, 2, Today)
                .Should().Equal(Sep01, Sep10);
        }

        [Fact]
        public void Date_range_and_period_both_apply()
        {
            // Last week is 21-28 Sep; the range cuts it to 21 Sep.
            RecentTradesFilter.Apply(Trades, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 21), PeriodFilter.LastWeek, 5, Today)
                .Should().Equal(Sep21);
        }
    }
}
