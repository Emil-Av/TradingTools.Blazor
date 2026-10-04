using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Settings;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests.Spreads
{
    /// <summary>
    /// The equity curve of an account: its account amount, resets ("start again from the next trade", nothing
    /// deleted), the "all history" view and the date range.
    /// </summary>
    public class EquityCurvesTests
    {
        private static DateTime Utc(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

        /// <summary>A trade of the given euro result (volume 1), dated and created on the given day.</summary>
        private static DashboardTrade T(double euro, int month, int day, int createdMonth = 0, int createdDay = 0) =>
            (euro >= 0 ? Win(euro, 1, new DateOnly(2026, month, day)) : Loss(euro, 1, new DateOnly(2026, month, day))) with
            {
                CreatedAt = Utc(createdMonth == 0 ? month : createdMonth, createdDay == 0 ? day : createdDay)
            };

        private static EquityResetInfo Reset(int month, int day, double startingBalance, double previousStartingBalance) =>
            new(Utc(month, day, 18), startingBalance, previousStartingBalance);

        private static AccountEquitySettings Account(double amount, params EquityResetInfo[] resets) =>
            new(SampleSizeType.DemoTrading, amount, resets);

        private static double[] Balances(EquityView view) => [.. view.Points.Select(p => p.Balance)];

        #region Without a reset

        [Fact]
        public void The_curve_starts_at_the_account_amount_not_at_a_fixed_2000()
        {
            var view = EquityCurves.Build([T(100, 9, 1), T(-30, 9, 2)], Account(5000), EquityScope.SinceLastReset);

            Balances(view).Should().Equal(5000, 5100, 5070);
            view.StartBalance.Should().Be(5000);
            view.EndBalance.Should().Be(5070);
            view.NetEuro.Should().Be(70);
            view.TradeCount.Should().Be(2);
        }

        [Fact]
        public void Without_a_reset_both_views_are_the_same()
        {
            var trades = new[] { T(100, 9, 1), T(-30, 9, 2) };

            Balances(EquityCurves.Build(trades, Account(2000), EquityScope.AllHistory))
                .Should().Equal(Balances(EquityCurves.Build(trades, Account(2000), EquityScope.SinceLastReset)));
        }

        [Fact]
        public void No_trades_is_just_the_starting_point()
        {
            var view = EquityCurves.Build([], Account(2000), EquityScope.AllHistory);

            Balances(view).Should().Equal(2000);
            view.NetEuro.Should().Be(0);
            view.TradeCount.Should().Be(0);
        }

        [Fact]
        public void Trades_without_a_euro_result_are_not_on_the_curve()
        {
            var view = EquityCurves.Build([T(100, 9, 1), WithoutPoints(EOutcome.Win)], Account(2000), EquityScope.SinceLastReset);

            Balances(view).Should().Equal(2000, 2100);
            view.TradeCount.Should().Be(1);
        }

        #endregion

        #region Resets

        [Fact]
        public void A_reset_starts_a_new_curve_from_the_next_trade()
        {
            // Trades on 1 and 2 Sep, reset on 5 Sep to 3000 (the account was 2000 before), trades on 6 and 7 Sep.
            var trades = new[] { T(100, 9, 1), T(-20, 9, 2), T(50, 9, 6), T(10, 9, 7) };
            var account = Account(3000, Reset(9, 5, startingBalance: 3000, previousStartingBalance: 2000));

            var view = EquityCurves.Build(trades, account, EquityScope.SinceLastReset);

            Balances(view).Should().Equal(3000, 3050, 3060);
            view.StartBalance.Should().Be(3000);
            view.NetEuro.Should().Be(60, "only the trades since the reset");
            view.TradeCount.Should().Be(2);
        }

        [Fact]
        public void All_history_keeps_the_old_curve_and_marks_the_reset()
        {
            var trades = new[] { T(100, 9, 1), T(-20, 9, 2), T(50, 9, 6), T(10, 9, 7) };
            var account = Account(3000, Reset(9, 5, startingBalance: 3000, previousStartingBalance: 2000));

            var view = EquityCurves.Build(trades, account, EquityScope.AllHistory);

            // Start 2000 -> 2100 -> 2080, then the reset: the balance becomes 3000, then 3050 -> 3060.
            Balances(view).Should().Equal(2000, 2100, 2080, 3000, 3050, 3060);
            view.Points.Where(p => p.IsReset).Should().ContainSingle().Which.Balance.Should().Be(3000);
            view.Points.Single(p => p.IsReset).Date.Should().Be(new DateOnly(2026, 9, 5));
        }

        [Fact]
        public void The_jump_at_a_reset_is_not_a_trading_result()
        {
            var trades = new[] { T(100, 9, 1), T(50, 9, 6) };
            var account = Account(9000, Reset(9, 5, startingBalance: 9000, previousStartingBalance: 2000));

            var view = EquityCurves.Build(trades, account, EquityScope.AllHistory);

            view.NetEuro.Should().Be(150, "the 6,900 from 2,100 to 9,000 is not something the trades won");
            view.TradeCount.Should().Be(2);
        }

        [Fact]
        public void A_reset_to_a_lower_amount_is_not_a_drawdown()
        {
            // The curve peaks at 2500, then the account is reset to 1000 (e.g. after a withdrawal): not a loss of 1500.
            var trades = new[] { T(500, 9, 1), T(-100, 9, 6) };
            var account = Account(1000, Reset(9, 5, startingBalance: 1000, previousStartingBalance: 2000));

            var view = EquityCurves.Build(trades, account, EquityScope.AllHistory);

            Balances(view).Should().Equal(2000, 2500, 1000, 900);
            view.MaxDrawdown.Should().Be(100, "only the -100 trade after the reset");
        }

        [Fact]
        public void Several_resets_make_several_curves()
        {
            var trades = new[] { T(100, 9, 1), T(40, 9, 6), T(-10, 9, 11) };
            var account = Account(500,
                Reset(9, 5, startingBalance: 1000, previousStartingBalance: 2000),
                Reset(9, 10, startingBalance: 500, previousStartingBalance: 1000));

            var all = EquityCurves.Build(trades, account, EquityScope.AllHistory);
            var current = EquityCurves.Build(trades, account, EquityScope.SinceLastReset);

            Balances(all).Should().Equal(2000, 2100, 1000, 1040, 500, 490);
            all.Points.Count(p => p.IsReset).Should().Be(2);
            Balances(current).Should().Equal(500, 490);
        }

        [Fact]
        public void A_trade_belongs_to_the_curve_that_was_running_when_it_was_logged_not_by_its_date()
        {
            // Logged on 6 Sep (after the reset on 5 Sep) but dated 1 Sep: it counts for the new curve.
            var backdated = T(80, month: 9, day: 1, createdMonth: 9, createdDay: 6);
            var account = Account(3000, Reset(9, 5, startingBalance: 3000, previousStartingBalance: 2000));

            Balances(EquityCurves.Build([backdated], account, EquityScope.SinceLastReset)).Should().Equal(3000, 3080);
            Balances(EquityCurves.Build([backdated], account, EquityScope.AllHistory)).Should().Equal(2000, 3000, 3080);
        }

        [Fact]
        public void A_trade_logged_in_the_same_instant_as_the_reset_is_still_on_the_old_curve()
        {
            var resetAt = Utc(9, 5, 18);
            var trade = T(80, 9, 5) with { CreatedAt = resetAt };
            var account = Account(3000, new EquityResetInfo(resetAt, 3000, 2000));

            Balances(EquityCurves.Build([trade], account, EquityScope.SinceLastReset)).Should().Equal(3000);
        }

        [Fact]
        public void Changing_the_account_amount_moves_the_start_of_the_current_curve_only()
        {
            // The account amount was edited from 3000 to 3500 after the reset; the closed curve keeps its own start.
            var trades = new[] { T(100, 9, 1), T(50, 9, 6) };
            var account = Account(3500, Reset(9, 5, startingBalance: 3000, previousStartingBalance: 2000));

            Balances(EquityCurves.Build(trades, account, EquityScope.AllHistory)).Should().Equal(2000, 2100, 3500, 3550);
        }

        [Fact]
        public void A_reset_with_no_trades_after_it_shows_a_curve_that_has_only_started()
        {
            var account = Account(3000, Reset(9, 5, startingBalance: 3000, previousStartingBalance: 2000));

            var view = EquityCurves.Build([T(100, 9, 1)], account, EquityScope.SinceLastReset);

            Balances(view).Should().Equal(3000);
            view.TradeCount.Should().Be(0);
        }

        #endregion

        #region Date range

        private static readonly DashboardTrade[] Trades = [T(100, 9, 1), T(-20, 9, 10), T(50, 9, 20), T(10, 9, 28)];

        [Fact]
        public void The_range_keeps_the_real_balance_levels()
        {
            // From 15 Sep: the account had 2080 just before, so the curve starts there rather than at 2000.
            var view = EquityCurves.Build(Trades, Account(2000), EquityScope.SinceLastReset, from: new DateOnly(2026, 9, 15));

            Balances(view).Should().Equal(2080, 2130, 2140);
            view.StartBalance.Should().Be(2080);
            view.NetEuro.Should().Be(60, "only the trades inside the range");
            view.TradeCount.Should().Be(2);
        }

        [Fact]
        public void The_end_of_the_range_cuts_off_later_trades()
        {
            var view = EquityCurves.Build(Trades, Account(2000), EquityScope.SinceLastReset, to: new DateOnly(2026, 9, 10));

            Balances(view).Should().Equal(2000, 2100, 2080);
            view.NetEuro.Should().Be(80);
        }

        [Fact]
        public void Both_ends_of_the_range_include_their_own_day()
        {
            var view = EquityCurves.Build(Trades, Account(2000), EquityScope.SinceLastReset, from: new DateOnly(2026, 9, 10), to: new DateOnly(2026, 9, 20));

            Balances(view).Should().Equal(2100, 2080, 2130);
            view.TradeCount.Should().Be(2);
        }

        [Fact]
        public void A_range_without_trades_is_just_the_balance_it_starts_at()
        {
            var view = EquityCurves.Build(Trades, Account(2000), EquityScope.SinceLastReset, from: new DateOnly(2026, 9, 21), to: new DateOnly(2026, 9, 27));

            Balances(view).Should().Equal(2130);
            view.NetEuro.Should().Be(0);
            view.TradeCount.Should().Be(0);
        }

        [Fact]
        public void A_range_before_the_first_trade_starts_at_the_account_amount()
        {
            var view = EquityCurves.Build(Trades, Account(2000), EquityScope.SinceLastReset, to: new DateOnly(2026, 8, 1));

            Balances(view).Should().Equal(2000);
        }

        [Fact]
        public void The_range_works_on_the_whole_history_and_keeps_a_reset_inside_it()
        {
            var trades = new[] { T(100, 9, 1), T(50, 9, 6), T(10, 9, 8) };
            var account = Account(3000, Reset(9, 5, startingBalance: 3000, previousStartingBalance: 2000));

            var view = EquityCurves.Build(trades, account, EquityScope.AllHistory, from: new DateOnly(2026, 9, 4));

            // Just before the range the balance was 2100; then the reset to 3000, then the two later trades.
            Balances(view).Should().Equal(2100, 3000, 3050, 3060);
            view.Points.Count(p => p.IsReset).Should().Be(1);
            view.NetEuro.Should().Be(60);
        }

        [Fact]
        public void The_drawdown_is_measured_inside_the_range()
        {
            var trades = new[] { T(500, 9, 1), T(-300, 9, 10), T(100, 9, 20) };

            var whole = EquityCurves.Build(trades, Account(2000), EquityScope.SinceLastReset);
            var later = EquityCurves.Build(trades, Account(2000), EquityScope.SinceLastReset, from: new DateOnly(2026, 9, 15));

            whole.MaxDrawdown.Should().Be(300);
            later.MaxDrawdown.Should().Be(0, "inside the range the balance only rises");
        }

        #endregion

        #region Statistics helpers

        [Fact]
        public void The_starting_balance_is_a_parameter_of_the_curve_and_the_summary()
        {
            var trades = new[] { T(100, 9, 1) };

            DashboardStats.EquityCurve(trades, 700).Select(p => p.Balance).Should().Equal(700, 800);
            DashboardStats.Summarize(trades, 700).Balance.Should().Be(800);
            DashboardStats.EquityCurve(trades).Select(p => p.Balance).Should().Equal(2000, 2100); // the old 2000 is the default
        }

        [Fact]
        public void The_max_drawdown_starts_its_peak_over_at_a_reset_point()
        {
            DashboardStats.MaxDrawdown([new(null, 2000), new(null, 3000), new(null, 1000, IsReset: true), new(null, 900)]).Should().Be(100);
            DashboardStats.MaxDrawdown([new(null, 2000), new(null, 3000), new(null, 1000), new(null, 900)]).Should().Be(2100, "without the reset it is a loss");
        }

        #endregion
    }
}
