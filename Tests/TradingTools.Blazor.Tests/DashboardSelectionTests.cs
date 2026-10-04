using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>The dashboard's account and strategy switches.</summary>
    public class DashboardSelectionTests
    {
        private static DashboardTrade On(SampleSizeType account, Strategy strategy, int day) =>
            Win(date: new DateOnly(2026, 9, day), strategy: strategy) with { AccountType = account };

        private static readonly DashboardTrade DemoSrs1 = On(SampleSizeType.DemoTrading, Strategy.SRS, 1);
        private static readonly DashboardTrade DemoEspresso2 = On(SampleSizeType.DemoTrading, Strategy.Espresso, 2);
        private static readonly DashboardTrade LiveSrs3 = On(SampleSizeType.Trade, Strategy.SRS, 3);
        private static readonly DashboardTrade DemoSrs4 = On(SampleSizeType.DemoTrading, Strategy.SRS, 4);
        private static readonly DashboardTrade PaperSrs5 = On(SampleSizeType.PaperTrade, Strategy.SRS, 5);

        private static readonly DashboardTrade[] Trades = [DemoSrs1, DemoEspresso2, LiveSrs3, DemoSrs4, PaperSrs5];

        [Fact]
        public void Both_strategies_of_one_account_in_order() =>
            DashboardSelection.Select(Trades, SampleSizeType.DemoTrading, strategy: null).Should().Equal(DemoSrs1, DemoEspresso2, DemoSrs4);

        [Fact]
        public void A_single_strategy_of_one_account() =>
            DashboardSelection.Select(Trades, SampleSizeType.DemoTrading, Strategy.SRS).Should().Equal(DemoSrs1, DemoSrs4);

        [Fact]
        public void Accounts_never_mix() =>
            DashboardSelection.Select(Trades, SampleSizeType.Trade, Strategy.SRS).Should().Equal(LiveSrs3);

        [Fact]
        public void An_account_without_trades_of_that_strategy_is_empty() =>
            DashboardSelection.Select(Trades, SampleSizeType.Trade, Strategy.Espresso).Should().BeEmpty();

        [Fact]
        public void The_switch_offers_trade_and_demo_trading_but_not_paper_trading()
        {
            DashboardSelection.Accounts.Should().Equal(SampleSizeType.Trade, SampleSizeType.DemoTrading);
            DashboardSelection.DefaultAccount.Should().Be(SampleSizeType.DemoTrading);
        }

        [Fact]
        public void The_strategy_switch_offers_both_then_each()
        {
            DashboardSelection.Strategies.Should().Equal(null, Strategy.SRS, Strategy.Espresso);
            DashboardSelection.Strategies.Select(DashboardSelection.StrategyLabel).Should().Equal("SRS & Espresso", "SRS", "Espresso");
        }

        [Fact]
        public void Stats_follow_the_selected_strategy()
        {
            var espressoOnly = DashboardSelection.Select([Win(10, strategy: Strategy.SRS), Loss(-4, strategy: Strategy.Espresso)],
                SampleSizeType.DemoTrading, Strategy.Espresso);

            var summary = DashboardStats.Summarize(espressoOnly);
            summary.Wins.Should().Be(0);
            summary.Losses.Should().Be(1);
            summary.NetEuro.Should().Be(-4);
        }
    }
}
