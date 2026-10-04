using Shared.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    public class EquityCurveTests
    {
        private static IEnumerable<double> Balances(IEnumerable<DashboardStats.EquityPoint> curve) => curve.Select(p => p.Balance);

        [Fact]
        public void Curve_starts_at_2000_with_no_date()
        {
            DashboardStats.EquityCurve([Win(10)])[0].Should().Be(new DashboardStats.EquityPoint(null, 2000));
        }

        [Fact]
        public void No_trades_is_just_the_starting_point()
        {
            DashboardStats.EquityCurve([]).Should().ContainSingle()
                .Which.Balance.Should().Be(2000);
        }

        [Fact]
        public void Each_trade_moves_the_balance_by_its_euro_result()
        {
            // +36 x 0.5 = +18, -8 x 4 = -32, +20 x 3 = +60
            Balances(DashboardStats.EquityCurve([Win(36, 0.5), Loss(-8, 4), Win(20, 3)]))
                .Should().Equal(2000, 2018, 1986, 2046);
        }

        [Fact]
        public void Points_carry_the_trade_dates_in_the_given_order()
        {
            var d1 = new DateOnly(2026, 3, 16);
            var d2 = new DateOnly(2026, 3, 17);

            DashboardStats.EquityCurve([Win(10, date: d1), Loss(-5, date: d2)])
                .Select(p => p.Date).Should().Equal(null, d1, d2);
        }

        [Fact]
        public void Trades_with_unknown_result_are_skipped_without_moving_the_balance()
        {
            Balances(DashboardStats.EquityCurve([Win(10), WithoutPoints(EOutcome.Loss), Win(10, volume: null), Loss(-4)]))
                .Should().Equal(2000, 2010, 2006);
        }

        [Fact]
        public void Breakevens_add_a_flat_point()
        {
            Balances(DashboardStats.EquityCurve([Win(10), Breakeven(), Loss(-4)]))
                .Should().Equal(2000, 2010, 2010, 2006);
        }

        [Fact]
        public void Last_point_matches_the_summary_balance()
        {
            List<DashboardTrade> trades = [Win(12.7, 3), Loss(-3.3, 0.75), Win(7.1, 1.5), Loss(-0.1, 7), WithoutPoints(EOutcome.Win), Breakeven(null)];

            DashboardStats.EquityCurve(trades)[^1].Balance
                .Should().Be(DashboardStats.Summarize(trades).Balance, "the chart and the balance in its header must agree to the cent");
        }

        [Fact]
        public void Ten_wins_of_ten_cents_end_exactly_one_euro_up()
        {
            DashboardStats.EquityCurve(Enumerable.Range(0, 10).Select(_ => Win(0.1)))[^1].Balance
                .Should().Be(2001);
        }

        /// <summary>
        /// Over many trades the floating point error grows large enough to survive being added to the
        /// 2000 start (1000 x 0.1 sums to 99.9999999999986), so every point must be rounded to the cent.
        /// </summary>
        [Fact]
        public void A_thousand_wins_of_ten_cents_end_exactly_one_hundred_euro_up()
        {
            var curve = DashboardStats.EquityCurve(Enumerable.Range(0, 1000).Select(_ => Win(0.1)));

            curve[^1].Balance.Should().Be(2100);
            curve[500].Balance.Should().Be(2050);
        }
    }

    public class MaxDrawdownTests
    {
        private static double Drawdown(params double[] balances) =>
            DashboardStats.MaxDrawdown(balances.Select(b => new DashboardStats.EquityPoint(null, b)));

        [Fact]
        public void Only_rising_balance_has_no_drawdown()
        {
            Drawdown(2000, 2010, 2050, 2100).Should().Be(0);
        }

        [Fact]
        public void Flat_single_and_empty_curves_have_no_drawdown()
        {
            Drawdown(2000, 2000, 2000).Should().Be(0);
            Drawdown(2000).Should().Be(0);
            Drawdown().Should().Be(0);
        }

        [Fact]
        public void Drawdown_is_measured_from_the_highest_peak_before_the_low()
        {
            Drawdown(2000, 2100, 1900, 2200, 2050)
                .Should().Be(200, "peak 2100 -> low 1900 is 200; the later 2200 -> 2050 is only 150");
        }

        [Fact]
        public void A_later_bigger_drop_wins()
        {
            Drawdown(2000, 2100, 2000, 2400, 2050).Should().Be(350);
        }

        [Fact]
        public void Dropping_right_from_the_start_counts()
        {
            Drawdown(2000, 1800, 1900).Should().Be(200);
        }

        [Fact]
        public void Recovering_above_the_old_peak_does_not_reduce_an_earlier_drawdown()
        {
            Drawdown(2000, 1500, 2500, 2400).Should().Be(500);
        }

        [Fact]
        public void Drawdown_from_real_trades()
        {
            // 2000 -> 2100 -> 1800 -> 1850 -> 1750: peak 2100, lowest after it 1750.
            var curve = DashboardStats.EquityCurve([Win(100), Loss(-300), Win(50), Loss(-100)]);

            DashboardStats.MaxDrawdown(curve).Should().Be(350);
        }
    }
}
