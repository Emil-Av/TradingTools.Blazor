using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Calculation;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Settings;
using TradingTools.Blazor.Services.Validation;
using TradingTools.Blazor.Tests.Validation;

namespace TradingTools.Blazor.Tests.Spreads
{
    /// <summary>
    /// The spread of an instrument (in points) is taken off the result of every position of a trade, before the
    /// volume is applied - in one calculation shared by the dashboard, the history and the Trades page.
    /// </summary>
    public class SpreadResultTests
    {
        private static TradeAddOn AddOn(double entry, double exit, double volume = 1) =>
            new() { EntryPrice = entry, ExitPrice = exit, Volume = volume, PnL = Math.Abs(exit - entry) };

        /// <summary>A long main position of the given gross points (entry 1000), amount 1, with the outcome the points imply.</summary>
        private static SRS Trade(double points, EOutcome? outcome = null, double amount = 1, params TradeAddOn[] addOns) => new()
        {
            Direction = EDirection.Long, EntryPrice = 1000, ExitPrice = 1000 + points, Amount = amount, PnL = Math.Abs(points), Symbol = "DAX",
            Outcome = outcome ?? (points > 0 ? EOutcome.Win : points < 0 ? EOutcome.Loss : EOutcome.Breakeven), AddOns = [.. addOns]
        };

        #region SpreadTable

        [Theory]
        [InlineData("DAX", 1.2)]
        [InlineData("dax", 1.2)]
        [InlineData("  Dax ", 1.2)]
        [InlineData("US500", 0.8)]
        [InlineData("NASDAQ", 0)]      // not set
        [InlineData("OTHER", 0)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void The_spread_is_found_by_symbol_whatever_its_case_and_spacing(string? symbol, double expected)
        {
            var table = new SpreadTable([KeyValuePair.Create("DAX", 1.2), KeyValuePair.Create("US500", 0.8)]);

            table.For(symbol).Should().Be(expected);
        }

        [Fact]
        public void No_spreads_means_no_spread() => SpreadTable.None.For("DAX").Should().Be(0);

        #endregion

        #region Points after the spread

        [Theory]
        [InlineData(EOutcome.Win, 50, 1.2, 48.8)]        // a win earns less
        [InlineData(EOutcome.Loss, 50, 1.2, -51.2)]      // a loss costs more
        [InlineData(EOutcome.Breakeven, 0, 1.2, -1.2)]   // a breakeven costs the spread
        [InlineData(EOutcome.Win, 0.5, 1, -0.5)]         // a small win ends below zero
        public void The_main_position_loses_the_spread(EOutcome outcome, double pnl, double spread, double expected)
        {
            var trade = new SRS { Outcome = outcome, PnL = pnl, Amount = 1 };

            TradeResults.MainPointsAfterSpread(trade, spread).Should().BeApproximately(expected, 1e-9);
        }

        [Fact]
        public void Without_a_spread_the_points_are_the_plain_points()
        {
            TradeResults.MainPointsAfterSpread(Trade(50), 0).Should().Be(50);
            TradeResults.MainPointsAfterSpread(Trade(-30), 0).Should().Be(-30);
            TradeResults.MainPointsAfterSpread(Trade(0), 0).Should().Be(0);
        }

        [Fact]
        public void Points_that_are_not_recorded_stay_unknown_whatever_the_spread()
        {
            var trade = new SRS { Outcome = EOutcome.Win, PnL = null, EntryPrice = null, ExitPrice = null };

            TradeResults.MainPointsAfterSpread(trade, 1.2).Should().BeNull();
        }

        [Theory]
        [InlineData(EDirection.Long, 1000, 1020, 18.8)]    // long closed higher: +20 - 1.2
        [InlineData(EDirection.Long, 1020, 1000, -21.2)]   // long closed lower: -20 - 1.2
        [InlineData(EDirection.Short, 1020, 1000, 18.8)]
        [InlineData(EDirection.Short, 1000, 1020, -21.2)]
        [InlineData(EDirection.Long, 1000, 1000, -1.2)]    // at the entry: just the spread
        public void An_add_on_loses_the_spread_too(EDirection direction, double entry, double exit, double expected) =>
            TradeResults.AddOnPointsAfterSpread(AddOn(entry, exit), direction, 1.2).Should().BeApproximately(expected, 1e-9);

        #endregion

        #region The whole trade

        [Fact]
        public void The_spread_is_paid_on_each_position_with_its_own_volume()
        {
            // Main: +50 points, amount 2, spread 1 -> (50 - 1) × 2 = 98. Add-on: -10 points, volume 3 -> (-10 - 1) × 3 = -33.
            var trade = Trade(50, amount: 2, addOns: AddOn(1060, 1050, volume: 3));

            var result = TradeResults.Calculate(trade, 1);

            result.Euro.Should().BeApproximately(65, 1e-9);
            result.Points.Should().BeApproximately(49 - 11, 1e-9);
            result.SpreadEuro.Should().Be(5, "1 point on 2 + 3 contracts");
        }

        [Fact]
        public void A_trade_without_add_ons_is_one_position()
        {
            var result = TradeResults.Calculate(Trade(50, amount: 4), 1.5);

            result.Points.Should().BeApproximately(48.5, 1e-9);
            result.Euro.Should().BeApproximately(194, 1e-9);
            result.SpreadEuro.Should().Be(6);
        }

        [Fact]
        public void A_breakeven_costs_the_spread_in_euro()
        {
            var result = TradeResults.Calculate(Trade(0, amount: 2), 1.2);

            result.Euro.Should().BeApproximately(-2.4, 1e-9);
        }

        [Fact]
        public void Gross_euro_is_the_result_plus_the_spread()
        {
            var result = TradeResults.Calculate(Trade(50, amount: 2), 1);

            (result.Euro!.Value + result.SpreadEuro).Should().BeApproximately(100, 1e-9);
        }

        [Fact]
        public void A_missing_volume_makes_the_euro_unknown_but_not_the_points()
        {
            var result = TradeResults.Calculate(Trade(50, amount: 1, addOns: new TradeAddOn { EntryPrice = 1060, ExitPrice = 1050, Volume = null }), 1);

            result.Points.Should().BeApproximately(49 - 11, 1e-9);
            result.Euro.Should().BeNull();
        }

        [Fact]
        public void An_add_on_without_prices_makes_the_whole_result_unknown()
        {
            var result = TradeResults.Calculate(Trade(50, addOns: new TradeAddOn { EntryPrice = null, ExitPrice = 1050, Volume = 1 }), 1);

            result.Points.Should().BeNull();
            result.Euro.Should().BeNull();
        }

        [Fact]
        public void Zero_spread_changes_nothing()
        {
            var trade = Trade(50, amount: 2, addOns: AddOn(1060, 1050, volume: 3));

            var result = TradeResults.Calculate(trade, 0);

            result.Euro.Should().Be(100 - 30);
            result.SpreadEuro.Should().Be(0);
        }

        #endregion

        #region The net result that decides the outcome of a trade with add-ons

        [Fact]
        public void The_spread_can_turn_a_net_gain_into_a_net_loss()
        {
            // +50 on the main position, -45 on the add-on: +5 before the spread. With a spread of 3 on both positions: -1.
            var trade = Trade(50, addOns: AddOn(1095, 1050));

            TradeNet.Calculate(trade)!.Outcome.Should().Be(EOutcome.Win);
            var withSpread = TradeNet.Calculate(trade, 3)!;

            withSpread.NetEuro.Should().Be(-1);
            withSpread.Outcome.Should().Be(EOutcome.Loss);
            withSpread.SpreadEuro.Should().Be(6);
        }

        [Fact]
        public void The_net_result_and_the_shared_calculation_agree()
        {
            var trade = Trade(50, amount: 2, addOns: AddOn(1060, 1050, volume: 3));

            TradeNet.Calculate(trade, 1.2)!.NetEuro.Should().BeApproximately(TradeResults.Calculate(trade, 1.2).Euro!.Value, 0.005);
        }

        [Fact]
        public void The_data_check_judges_the_outcome_by_the_net_result_after_the_spread()
        {
            var trade = TradeValidatorTests.ValidTrade();       // long 7671 -> 7692: +21 × 3
            trade.AddOns = [AddOn(7692.5, 7692, volume: 1)];    // -0.5 × 1
            trade.Outcome = EOutcome.Win;

            TradeValidator.Validate(trade, spread: 0).Should().BeEmpty();

            // A spread of 30 points on both positions turns the +62.5 into a loss: the Win is wrong.
            var issue = TradeValidator.Validate(trade, spread: 30).Should().ContainSingle().Subject;
            issue.Field.Should().Be(nameof(BaseTrade.Outcome));
            issue.Message.Should().Contain("so the outcome should be Loss, not Win");
        }

        #endregion

        #region The dashboard and the history

        private static async Task<List<DashboardTrade>> Load(ISettingsService settings, params SRS[] trades)
        {
            var sampleSize = new SampleSize { Id = 1, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.DemoTrading };
            int id = 1;
            foreach (var trade in trades)
            {
                trade.Id = id++;
                trade.SampleSize = sampleSize;
                trade.SampleSizeId = 1;
                trade.Date = new DateOnly(2026, 9, id);
                trade.Status = EStatus.Closed;
            }
            return await new DashboardService(TestUnitOfWork.Create([sampleSize], trades), settings).GetTradesAsync();
        }

        [Fact]
        public async Task The_dashboards_points_and_euro_have_the_spread_taken_off()
        {
            var trade = Trade(50, amount: 2);

            var result = (await Load(TestSettings.WithSpreads(("DAX", 1)), trade)).Single();

            result.Points.Should().Be(49);
            result.Euro.Should().Be(98);
        }

        [Fact]
        public async Task Each_instrument_has_its_own_spread()
        {
            var dax = Trade(50);
            var us500 = Trade(50); us500.Symbol = "US500";
            var nasdaq = Trade(50); nasdaq.Symbol = "NASDAQ";

            var result = await Load(TestSettings.WithSpreads(("DAX", 1), ("US500", 0.5)), dax, us500, nasdaq);

            result.Select(t => t.Points).Should().Equal(49, 49.5, 50);
        }

        [Fact]
        public async Task The_dashboard_and_the_trades_page_get_the_same_result()
        {
            var trade = Trade(50, amount: 2, addOns: AddOn(1060, 1050, volume: 3));

            var dashboard = (await Load(TestSettings.WithSpreads(("DAX", 1.2)), trade)).Single();
            var tradesPage = TradeResults.Calculate(trade, 1.2);

            dashboard.Euro.Should().BeApproximately(tradesPage.Euro!.Value, 1e-9);
            dashboard.TotalPoints.Should().BeApproximately(tradesPage.Points!.Value, 1e-9);
        }

        [Fact]
        public async Task The_statistics_use_the_results_after_the_spread()
        {
            // A win of 10 and a loss of 10, spread 2: +8 and -12.
            var trades = await Load(TestSettings.WithSpreads(("DAX", 2)), Trade(10), Trade(-10));

            var summary = DashboardStats.Summarize(trades);

            summary.AvgWinPoints.Should().Be(8);
            summary.AvgLossPoints.Should().Be(-12);
            summary.NetEuro.Should().Be(-4);
            DashboardStats.EquityCurve(trades).Select(p => p.Balance).Should().Equal(2000, 2008, 1996);
        }

        [Fact]
        public async Task A_breakeven_costs_the_spread_on_the_dashboard_but_stays_out_of_the_win_rate()
        {
            var trades = await Load(TestSettings.WithSpreads(("DAX", 2)), Trade(0), Trade(10));

            var summary = DashboardStats.Summarize(trades);

            summary.Breakevens.Should().Be(1);
            summary.WinRate.Should().Be(1, "breakevens are still not counted for or against the win rate");
            summary.NetEuro.Should().Be(6, "-2 for the breakeven, +8 for the win");
        }

        [Fact]
        public async Task The_win_loss_labels_are_left_as_recorded()
        {
            // A "win" of 0.5 points with a spread of 1 is below zero in euro, but is still recorded as a win.
            var result = (await Load(TestSettings.WithSpreads(("DAX", 1)), Trade(0.5))).Single();

            result.Outcome.Should().Be(EOutcome.Win);
            result.Euro.Should().BeApproximately(-0.5, 1e-9);
        }

        #endregion
    }
}
