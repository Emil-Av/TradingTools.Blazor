using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Tests.Validation
{
    public class TradePnlTests
    {
        [Theory]
        [InlineData(7671, 7692, 21)]       // real trade: long win
        [InlineData(26042, 26078, 36)]     // exit above entry
        [InlineData(26138, 26104, 34)]     // exit below entry: still positive
        [InlineData(26019, 26019, 0)]      // breakeven
        public void Points_are_the_positive_distance_between_entry_and_exit(double entry, double exit, double expected)
        {
            TradePnl.Points(entry, exit).Should().Be(expected);
        }

        [Fact]
        public void Points_are_rounded_to_two_decimals_without_float_residue()
        {
            // 7671.25 - 7655.1 is 16.150000000000546 in doubles.
            TradePnl.Points(7655.1, 7671.25).Should().Be(16.15);
            TradePnl.Points(5000.25, 4997.75).Should().Be(2.5);
        }

        [Theory]
        [InlineData(null, 7692.0)]
        [InlineData(7671.0, null)]
        [InlineData(null, null)]
        [InlineData(7655.0, 0.0)]     // real data: exit price 0 means "not filled in"
        [InlineData(0.0, 7692.0)]
        [InlineData(-5.0, 7692.0)]
        public void Points_are_unknown_without_two_real_prices(double? entry, double? exit)
        {
            TradePnl.Points(entry, exit).Should().BeNull();
        }
    }
}
