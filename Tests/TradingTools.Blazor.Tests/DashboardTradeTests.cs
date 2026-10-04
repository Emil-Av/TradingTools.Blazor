using Shared.Enums;
using static TradingTools.Blazor.Tests.TestTrades;

namespace TradingTools.Blazor.Tests
{
    /// <summary>Euro result of a single trade: points x volume.</summary>
    public class DashboardTradeTests
    {
        [Theory]
        [InlineData(36, 0.5, 18)]     // real trade: DAX short, 36 pts at 0.5 volume
        [InlineData(20, 3, 60)]       // real trade: US500 long, 20 pts at 3 volume
        [InlineData(-8, 4, -32)]      // real trade: US500 loss, -8 pts at 4 volume
        [InlineData(-34, 0.5, -17)]
        [InlineData(15, 0.25, 3.75)]
        [InlineData(12, 0.75, 9)]
        public void Euro_is_points_times_volume(double points, double volume, double expectedEuro)
        {
            var trade = Trade(points >= 0 ? EOutcome.Win : EOutcome.Loss, points, volume);

            trade.Euro.Should().BeApproximately(expectedEuro, Precision);
        }

        [Fact]
        public void Loss_gives_negative_euro_and_win_gives_positive_euro()
        {
            Loss(-10, 2).Euro.Should().BeNegative();
            Win(10, 2).Euro.Should().BePositive();
        }

        [Fact]
        public void Volume_zero_gives_zero_euro_not_unknown()
        {
            Win(50, volume: 0).Euro.Should().Be(0);
        }

        [Fact]
        public void Missing_volume_makes_a_win_or_loss_result_unknown()
        {
            Win(10, volume: null).Euro.Should().BeNull();
            Loss(-10, volume: null).Euro.Should().BeNull();
        }

        [Fact]
        public void Missing_points_makes_the_result_unknown()
        {
            WithoutPoints(EOutcome.Win, volume: 2).Euro.Should().BeNull();
        }

        /// <summary>
        /// A breakeven is 0 points, and 0 x any volume is 0 euro - the result is known even when the
        /// volume wasn't recorded, so it must not be reported as "left out of the money figures".
        /// </summary>
        [Fact]
        public void Breakeven_without_volume_is_still_zero_euro()
        {
            Breakeven(volume: null).Euro.Should().Be(0);
        }
    }
}
