using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests.AddOns
{
    /// <summary>
    /// On the dashboard a trade with add-ons is still one trade, but its points and euro are the
    /// original position's plus every add-on's.
    /// </summary>
    public class AddOnStatsTests
    {
        private static DashboardTrade With(DashboardTrade trade, params DashboardAddOn[] addOns) => trade with { AddOns = addOns };

        [Fact]
        public void Total_points_add_the_add_ons_points_to_the_trades()
        {
            var trade = With(Win(20, volume: 1), new DashboardAddOn(10, 1), new DashboardAddOn(-5, 2));

            trade.TotalPoints.Should().Be(25);
            trade.Points.Should().Be(20, "Points stays the original position's result");
        }

        [Fact]
        public void Euro_uses_each_positions_own_volume()
        {
            // 20 pts × 1 + 10 pts × 0.5 − 5 pts × 2 = 20 + 5 − 10
            With(Win(20, volume: 1), new DashboardAddOn(10, 0.5), new DashboardAddOn(-5, 2)).Euro.Should().Be(15);
        }

        [Fact]
        public void Without_add_ons_nothing_changes()
        {
            var trade = Win(20, volume: 2);

            trade.TotalPoints.Should().Be(20);
            trade.Euro.Should().Be(40);
        }

        [Fact]
        public void An_add_on_without_prices_makes_the_whole_result_unknown()
        {
            var trade = With(Win(20, volume: 1), new DashboardAddOn(10, 1), new DashboardAddOn(null, 1));

            trade.TotalPoints.Should().BeNull();
            trade.Euro.Should().BeNull();
        }

        [Fact]
        public void An_add_on_without_volume_makes_the_euro_unknown_but_not_the_points()
        {
            var trade = With(Win(20, volume: 1), new DashboardAddOn(10, null));

            trade.TotalPoints.Should().Be(30);
            trade.Euro.Should().BeNull();
        }

        [Fact]
        public void A_breakeven_add_on_counts_as_zero_euro_even_without_volume() =>
            With(Win(20, volume: 1), new DashboardAddOn(0, null)).Euro.Should().Be(20);

        [Fact]
        public void Add_ons_on_a_breakeven_trade_still_count()
        {
            var trade = With(Breakeven(volume: 1), new DashboardAddOn(12, 1));

            trade.TotalPoints.Should().Be(12);
            trade.Euro.Should().Be(12);
        }

        [Fact]
        public void The_summary_counts_a_trade_with_add_ons_once_with_its_combined_result()
        {
            var trades = new[]
            {
                With(Win(20, volume: 1), new DashboardAddOn(10, 1), new DashboardAddOn(10, 1)), // 40 pts, 40 €
                Win(10, volume: 1),                               // 10 pts, 10 €
                With(Loss(-10, volume: 1), new DashboardAddOn(-6, 1)),           // −16 pts, −16 €
            };

            var summary = DashboardStats.Summarize(trades);

            summary.Wins.Should().Be(2);
            summary.Losses.Should().Be(1);
            summary.AvgWinPoints.Should().Be(25);   // (40 + 10) / 2
            summary.AvgLossPoints.Should().Be(-16);
            summary.NetEuro.Should().Be(34);
            summary.Balance.Should().Be(DashboardStats.DefaultStartingBalance + 34);
        }

        [Fact]
        public void The_equity_curve_moves_by_the_combined_euro_of_each_trade()
        {
            var curve = DashboardStats.EquityCurve([With(Win(20, volume: 1), new DashboardAddOn(10, 2)), Loss(-5, volume: 1)]);

            curve.Select(p => p.Balance).Should().Equal(
                DashboardStats.DefaultStartingBalance, DashboardStats.DefaultStartingBalance + 40, DashboardStats.DefaultStartingBalance + 35);
        }

        [Fact]
        public void Breakdowns_include_the_add_ons_euro() =>
            DashboardStats.BreakdownBy([With(Win(20, volume: 1), new DashboardAddOn(10, 2))], t => t.Symbol)
                .Should().ContainSingle().Which.NetEuro.Should().Be(40);

        [Fact]
        public async Task The_service_signs_add_on_points_from_the_trades_direction()
        {
            var sampleSize = new SampleSize { Id = 1, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.DemoTrading };
            var trade = new SRS
            {
                Id = 7, SampleSize = sampleSize, SampleSizeId = 1, Date = new DateOnly(2026, 9, 1), Status = EStatus.Closed,
                Symbol = "DAX", Direction = EDirection.Short, Outcome = EOutcome.Win, Amount = 1, EntryPrice = 1000, ExitPrice = 980, PnL = 20,
                // Out of order on purpose: the service orders them by id.
                AddOns = [new() { Id = 3, EntryPrice = 975, ExitPrice = 980, Volume = 1, PnL = 5 },   // short, closed higher: −5
                          new() { Id = 2, EntryPrice = 995, ExitPrice = 980, Volume = 2, PnL = 15 }], // short, closed lower: +15
            };

            var result = (await new DashboardService(TestUnitOfWork.Create([sampleSize], [trade]), TestSettings.NoSpreads()).GetTradesAsync()).Should().ContainSingle().Subject;

            result.AddOns.Should().Equal(new DashboardAddOn(15, 2), new DashboardAddOn(-5, 1));
            result.TotalPoints.Should().Be(30);  // 20 + 15 − 5
            result.Euro.Should().Be(45);         // 20 × 1 + 15 × 2 − 5 × 1
        }
    }
}
