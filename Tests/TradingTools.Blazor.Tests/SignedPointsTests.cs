using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>
    /// How a stored trade becomes signed points: recorded P&amp;L (points, stored positive) first,
    /// |exit - entry| as a fallback, sign always from Outcome, Direction ignored.
    /// </summary>
    public class SignedPointsTests
    {
        private static double? Points(EOutcome outcome, double? pnl = null, double? entry = null, double? exit = null, EDirection direction = EDirection.Long) =>
            DashboardService.SignedPoints(new BaseTrade { Outcome = outcome, PnL = pnl, EntryPrice = entry, ExitPrice = exit, Direction = direction });

        [Fact]
        public void Win_uses_recorded_points_as_positive()
        {
            Points(EOutcome.Win, pnl: 36).Should().Be(36);
        }

        [Fact]
        public void Loss_uses_recorded_points_as_negative()
        {
            Points(EOutcome.Loss, pnl: 34).Should().Be(-34, "losses are stored as a positive P&L with Outcome = Loss");
        }

        [Fact]
        public void Loss_recorded_as_a_negative_number_is_not_flipped_to_a_gain()
        {
            Points(EOutcome.Loss, pnl: -34).Should().Be(-34);
        }

        [Fact]
        public void Win_recorded_as_a_negative_number_still_counts_as_a_gain()
        {
            Points(EOutcome.Win, pnl: -20).Should().Be(20);
        }

        [Fact]
        public void Recorded_points_win_over_prices_when_they_disagree()
        {
            Points(EOutcome.Loss, pnl: 44, entry: 24241, exit: 24176)
                .Should().Be(-44, "24241 -> 24176 is 65 points, but 44 was recorded as the actual result");
        }

        [Fact]
        public void Falls_back_to_price_difference_when_points_were_not_recorded()
        {
            Points(EOutcome.Win, pnl: null, entry: 7671, exit: 7692).Should().Be(21);
        }

        [Fact]
        public void Falls_back_to_price_difference_when_a_win_or_loss_has_zero_points()
        {
            Points(EOutcome.Loss, pnl: 0, entry: 24241, exit: 24176).Should().Be(-65);
        }

        [Fact]
        public void Direction_does_not_decide_the_sign()
        {
            Points(EOutcome.Win, pnl: null, entry: 26042, exit: 26078, direction: EDirection.Short)
                .Should().Be(36, "a Short with exit above entry is marked Win in the real data");
            Points(EOutcome.Loss, pnl: null, entry: 26138, exit: 26104, direction: EDirection.Long)
                .Should().Be(-34);
        }

        [Fact]
        public void Price_fallback_keeps_fractional_points()
        {
            Points(EOutcome.Loss, pnl: null, entry: 5000.25, exit: 4997.75).Should().BeApproximately(-2.5, Precision);
        }

        [Fact]
        public void Exit_price_zero_is_not_a_real_price()
        {
            Points(EOutcome.Win, pnl: 0, entry: 7655, exit: 0)
                .Should().BeNull("an exit price of 0 is missing data, not a 7655 point win");
        }

        [Fact]
        public void No_points_and_no_prices_is_unknown()
        {
            Points(EOutcome.Win).Should().BeNull();
            Points(EOutcome.Loss, entry: 26000).Should().BeNull();
            Points(EOutcome.Loss, exit: 26000).Should().BeNull();
        }

        [Fact]
        public void Equal_entry_and_exit_on_a_win_or_loss_is_unknown_not_zero()
        {
            Points(EOutcome.Win, pnl: null, entry: 26019, exit: 26019).Should().BeNull();
        }

        [Theory]
        [InlineData(null, null, null)]
        [InlineData(0.0, 26019.0, 26019.0)]
        [InlineData(12.0, 25315.0, 25312.0)] // breakeven with a recorded move still counts as 0
        public void Breakeven_is_always_zero_points(double? pnl, double? entry, double? exit)
        {
            Points(EOutcome.Breakeven, pnl, entry, exit).Should().Be(0);
        }
    }
}
