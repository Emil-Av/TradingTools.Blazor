using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>Performance by strategy / by symbol cards.</summary>
    public class BreakdownTests
    {
        private static List<DashboardStats.Breakdown> BySymbol(params DashboardTrade[] trades) =>
            DashboardStats.BreakdownBy(trades, t => t.Symbol);

        [Fact]
        public void Groups_count_trades_wins_losses_and_net_euro()
        {
            var rows = BySymbol(
                Win(10, 1, symbol: "DAX"), Loss(-5, 2, symbol: "DAX"), Breakeven(symbol: "DAX"),
                Win(20, 1, symbol: "US500"));

            rows.Should().BeEquivalentTo(new[]
            {
                new DashboardStats.Breakdown("DAX", Trades: 3, Wins: 1, Losses: 1, WinRate: 0.5, NetEuro: 0),   // +10 - 10
                new DashboardStats.Breakdown("US500", Trades: 1, Wins: 1, Losses: 0, WinRate: 1.0, NetEuro: 20),
            });
        }

        [Fact]
        public void Group_with_only_breakevens_has_no_win_rate()
        {
            BySymbol(Breakeven(symbol: "NASDAQ"), Breakeven(symbol: "NASDAQ")).Should().ContainSingle()
                .Which.Should().Be(new DashboardStats.Breakdown("NASDAQ", 2, 0, 0, WinRate: null, NetEuro: 0));
        }

        [Fact]
        public void Unknown_results_count_as_trades_but_not_as_money()
        {
            BySymbol(Win(10, 2, symbol: "DAX"), Win(10, volume: null, symbol: "DAX")).Should().ContainSingle()
                .Which.Should().Be(new DashboardStats.Breakdown("DAX", Trades: 2, Wins: 2, Losses: 0, WinRate: 1.0, NetEuro: 20));
        }

        [Fact]
        public void Groups_are_sorted_by_number_of_trades_most_first()
        {
            var rows = BySymbol(
                Win(symbol: "NASDAQ"),
                Win(symbol: "DAX"), Win(symbol: "DAX"), Win(symbol: "DAX"),
                Win(symbol: "US500"), Loss(symbol: "US500"));

            rows.Select(r => r.Name).Should().Equal("DAX", "US500", "NASDAQ");
        }

        [Fact]
        public void Groups_add_up_to_the_overall_summary()
        {
            List<DashboardTrade> trades =
            [
                Win(36, 0.5, symbol: "DAX"), Loss(-8, 4, symbol: "US500"), Win(20, 3, symbol: "US500"),
                Breakeven(0.5, symbol: "DAX"), Loss(-34, 0.5, symbol: "DAX"), Win(12.7, 3, symbol: "NASDAQ"),
                WithoutPoints(EOutcome.Loss),
            ];

            var rows = DashboardStats.BreakdownBy(trades, t => t.Symbol);
            var summary = DashboardStats.Summarize(trades);

            rows.Sum(r => r.Trades).Should().Be(trades.Count);
            rows.Sum(r => r.Wins).Should().Be(summary.Wins);
            rows.Sum(r => r.Losses).Should().Be(summary.Losses);
            rows.Sum(r => r.NetEuro).Should().BeApproximately(summary.NetEuro, 0.005);
        }

        [Fact]
        public void Breakdown_by_strategy_separates_srs_and_espresso()
        {
            var rows = DashboardStats.BreakdownBy(
                [Win(10, strategy: Strategy.SRS), Loss(-10, strategy: Strategy.SRS), Win(30, strategy: Strategy.Espresso)],
                t => t.Strategy.ToString());

            rows.Should().BeEquivalentTo(new[]
            {
                new DashboardStats.Breakdown("SRS", 2, 1, 1, 0.5, 0),
                new DashboardStats.Breakdown("Espresso", 1, 1, 0, 1.0, 30),
            });
        }

        [Fact]
        public void Group_net_result_that_is_really_zero_is_exactly_zero()
        {
            BySymbol(Win(0.3, symbol: "DAX"), Loss(-0.1, symbol: "DAX"), Loss(-0.2, symbol: "DAX"))
                .Should().ContainSingle().Which.NetEuro.Should().Be(0);
        }
    }
}
