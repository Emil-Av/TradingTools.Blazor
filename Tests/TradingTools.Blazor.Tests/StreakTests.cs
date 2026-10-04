using Shared.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>
    /// Streak rules: breakevens are ignored; the first trade with a different result than the
    /// previous (non-breakeven) trade starts a new streak.
    /// </summary>
    public class StreakTests
    {
        private static (DashboardStats.Streak Current, int BestWin, int WorstLoss) Run(params DashboardTrade[] trades) =>
            DashboardStats.Streaks(trades);

        private static DashboardStats.Streak Wins(int length) => new(EOutcome.Win, length);
        private static DashboardStats.Streak Losses(int length) => new(EOutcome.Loss, length);
        private static readonly DashboardStats.Streak None = new(null, 0);

        [Fact]
        public void No_trades_means_no_streak()
        {
            Run().Should().Be((None, 0, 0));
        }

        [Fact]
        public void Only_breakevens_means_no_streak()
        {
            Run(Breakeven(), Breakeven(), Breakeven()).Should().Be((None, 0, 0));
        }

        [Fact]
        public void Consecutive_wins_build_one_streak()
        {
            Run(Win(), Win(), Win()).Should().Be((Wins(3), 3, 0));
        }

        [Fact]
        public void First_different_result_restarts_the_streak()
        {
            Run(Win(), Win(), Loss()).Should().Be((Losses(1), 2, 1));
        }

        [Fact]
        public void Breakeven_between_wins_does_not_break_the_streak()
        {
            var (current, bestWin, _) = Run(Win(), Breakeven(), Win(), Breakeven(), Breakeven(), Win());

            current.Should().Be(Wins(3));
            bestWin.Should().Be(3);
        }

        [Fact]
        public void Breakeven_between_a_win_and_a_loss_does_not_merge_or_extend_anything()
        {
            Run(Win(), Win(), Breakeven(), Loss()).Should().Be((Losses(1), 2, 1));
        }

        [Fact]
        public void Trailing_breakevens_keep_the_last_real_streak_current()
        {
            Run(Loss(), Loss(), Win(), Breakeven(), Breakeven()).Current.Should().Be(Wins(1));
        }

        [Fact]
        public void Best_and_worst_streaks_are_remembered_after_they_end()
        {
            // W W W L L L L W L W W
            var result = Run(
                Win(), Win(), Win(),
                Loss(), Loss(), Loss(), Loss(),
                Win(),
                Loss(),
                Win(), Win());

            result.Should().Be((Wins(2), 3, 4));
        }

        [Fact]
        public void Alternating_results_never_go_above_one()
        {
            Run(Win(), Loss(), Win(), Loss(), Win()).Should().Be((Wins(1), 1, 1));
        }

        [Fact]
        public void Starting_with_losses_is_counted_correctly()
        {
            Run(Loss(), Loss(), Breakeven(), Loss()).Should().Be((Losses(3), 0, 3));
        }

        [Fact]
        public void Trades_without_points_still_count_for_streaks()
        {
            Run(Win(), WithoutPoints(EOutcome.Win), Win()).Current
                .Should().Be(Wins(3), "the outcome is known even when the points weren't recorded");
        }

        [Fact]
        public void Summary_reports_the_same_streaks()
        {
            var summary = DashboardStats.Summarize([Win(), Win(), Loss(), Breakeven(), Loss(), Loss()]);

            summary.CurrentStreak.Should().Be(Losses(3));
            summary.BestWinStreak.Should().Be(2);
            summary.WorstLossStreak.Should().Be(3);
        }
    }
}
