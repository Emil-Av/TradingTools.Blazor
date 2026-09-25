using Shared.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>The KPI tiles: win rate, net P&amp;L / balance, average win and loss in points.</summary>
    public class SummaryTests
    {
        [Fact]
        public void No_trades_gives_an_empty_summary_on_the_starting_balance()
        {
            var s = DashboardStats.Summarize([]);

            s.Wins.Should().Be(0);
            s.Losses.Should().Be(0);
            s.Breakevens.Should().Be(0);
            s.WinRate.Should().BeNull();
            s.NetEuro.Should().Be(0);
            s.Balance.Should().Be(2000);
            s.AvgWinPoints.Should().BeNull();
            s.AvgLossPoints.Should().BeNull();
            s.TradesWithoutResult.Should().Be(0);
        }

        [Fact]
        public void Starting_balance_is_2000()
        {
            DashboardStats.StartingBalance.Should().Be(2000);
        }

        #region Win rate

        [Fact]
        public void Win_rate_ignores_breakevens()
        {
            var trades = new List<DashboardTrade> { Win(), Win(), Win(), Loss() };
            trades.AddRange(Enumerable.Range(0, 10).Select(_ => Breakeven()));

            var s = DashboardStats.Summarize(trades);

            s.WinRate.Should().BeApproximately(0.75, Precision, "3 wins out of 4 decided trades; the 10 breakevens don't count");
            s.Wins.Should().Be(3);
            s.Losses.Should().Be(1);
            s.Breakevens.Should().Be(10);
        }

        [Fact]
        public void Win_rate_is_unknown_when_every_trade_is_breakeven()
        {
            DashboardStats.Summarize([Breakeven(), Breakeven()]).WinRate.Should().BeNull();
        }

        [Fact]
        public void Win_rate_is_zero_with_only_losses_and_one_with_only_wins()
        {
            DashboardStats.Summarize([Loss(), Loss()]).WinRate.Should().Be(0);
            DashboardStats.Summarize([Win(), Win()]).WinRate.Should().Be(1);
        }

        [Fact]
        public void Win_rate_counts_trades_even_without_points_recorded()
        {
            DashboardStats.Summarize([Win(), WithoutPoints(EOutcome.Win), Loss()])
                .WinRate.Should().BeApproximately(2.0 / 3.0, Precision);
        }

        #endregion

        #region Net P&L and balance

        [Fact]
        public void Net_result_sums_points_times_volume_of_every_trade()
        {
            // +36 x 0.5 = +18, -8 x 4 = -32, +20 x 3 = +60, breakeven 0 -> +46
            var s = DashboardStats.Summarize([Win(36, 0.5), Loss(-8, 4), Win(20, 3), Breakeven(0.5)]);

            s.NetEuro.Should().Be(46);
            s.Balance.Should().Be(2046);
        }

        [Fact]
        public void Losing_overall_gives_a_balance_below_the_start()
        {
            // -30 x 2 = -60, +10 x 1 = +10 -> -50
            var s = DashboardStats.Summarize([Loss(-30, 2), Win(10, 1)]);

            s.NetEuro.Should().Be(-50);
            s.Balance.Should().Be(1950);
        }

        [Fact]
        public void Trades_with_unknown_result_do_not_change_the_money_but_are_counted_as_left_out()
        {
            var s = DashboardStats.Summarize([Win(36, 0.5), WithoutPoints(EOutcome.Win), Win(10, volume: null)]);

            s.NetEuro.Should().Be(18);
            s.TradesWithoutResult.Should().Be(2);
        }

        [Fact]
        public void Breakeven_without_volume_is_not_reported_as_left_out()
        {
            DashboardStats.Summarize([Win(10, 1), Breakeven(volume: null)])
                .TradesWithoutResult.Should().Be(0, "0 points x unknown volume is still 0 euro");
        }

        /// <summary>
        /// Floating point: 0.3 - 0.1 - 0.2 is -2.8e-17 in doubles. A result that is really 0 must be
        /// exactly 0 - otherwise the dashboard shows "−€0.00" in red for a flat account.
        /// </summary>
        [Fact]
        public void Net_result_that_is_really_zero_is_exactly_zero()
        {
            var s = DashboardStats.Summarize([Win(0.3), Loss(-0.1), Loss(-0.2)]);

            s.NetEuro.Should().Be(0);
            s.Balance.Should().Be(2000);
        }

        [Fact]
        public void Net_result_with_decimal_points_is_exact_to_the_cent()
        {
            // 12.7 x 3 = 38.10, -3.2 x 0.75 = -2.40, 7.1 x 1.5 = 10.65, -0.1 x 7 = -0.70 -> 45.65
            var s = DashboardStats.Summarize([Win(12.7, 3), Loss(-3.2, 0.75), Win(7.1, 1.5), Loss(-0.1, 7)]);

            s.NetEuro.Should().Be(45.65);
            s.Balance.Should().Be(2045.65);
        }

        #endregion

        #region Average win / loss in points

        [Fact]
        public void Average_win_is_the_mean_of_winning_points()
        {
            DashboardStats.Summarize([Win(36), Win(20), Win(10), Loss(-50)])
                .AvgWinPoints.Should().BeApproximately(22, Precision);
        }

        [Fact]
        public void Average_loss_is_the_mean_of_losing_points_and_negative()
        {
            DashboardStats.Summarize([Loss(-8), Loss(-34), Win(100)])
                .AvgLossPoints.Should().BeApproximately(-21, Precision);
        }

        [Fact]
        public void Averages_are_in_points_so_volume_does_not_weight_them()
        {
            DashboardStats.Summarize([Win(10, volume: 100), Win(30, volume: 1)])
                .AvgWinPoints.Should().BeApproximately(20, Precision, "in euro this would be 1000 and 30; in points it's a plain (10 + 30) / 2");
        }

        [Fact]
        public void Averages_skip_trades_without_points_instead_of_counting_them_as_zero()
        {
            var s = DashboardStats.Summarize([Win(40), WithoutPoints(EOutcome.Win), Loss(-10), WithoutPoints(EOutcome.Loss)]);

            s.AvgWinPoints.Should().BeApproximately(40, Precision);
            s.AvgLossPoints.Should().BeApproximately(-10, Precision);
        }

        [Fact]
        public void Averages_include_trades_without_volume_since_points_are_known()
        {
            DashboardStats.Summarize([Win(40, volume: null), Win(20, volume: 1)])
                .AvgWinPoints.Should().BeApproximately(30, Precision);
        }

        [Fact]
        public void Breakevens_do_not_pull_the_averages_toward_zero()
        {
            var s = DashboardStats.Summarize([Win(30), Breakeven(), Breakeven(), Loss(-10), Breakeven()]);

            s.AvgWinPoints.Should().BeApproximately(30, Precision);
            s.AvgLossPoints.Should().BeApproximately(-10, Precision);
        }

        [Fact]
        public void Average_is_unknown_when_there_are_no_trades_of_that_kind()
        {
            DashboardStats.Summarize([Win(10), Win(20)]).AvgLossPoints.Should().BeNull();
        }

        #endregion
    }
}
