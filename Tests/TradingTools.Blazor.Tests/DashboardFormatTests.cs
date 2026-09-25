using System.Globalization;
using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Tests
{
    /// <summary>What the dashboard actually shows for the numbers.</summary>
    public class DashboardFormatTests
    {
        [Theory]
        [InlineData(1234.5, "+€1,234.50")]
        [InlineData(827.75, "+€827.75")]
        [InlineData(-80, "−€80.00")]
        [InlineData(-1234.567, "−€1,234.57")]
        [InlineData(0, "€0.00")]
        public void Signed_euro(double value, string expected)
        {
            DashboardFormat.SignedEuro(value).Should().Be(expected);
        }

        [Theory]
        [InlineData(-0.001)]
        [InlineData(-2.7755575615628914E-17)] // 0.3 - 0.1 - 0.2 in doubles
        [InlineData(0.004)]
        public void Amounts_that_round_to_zero_have_no_sign(double value)
        {
            DashboardFormat.SignedEuro(value).Should().Be("€0.00");
        }

        [Theory]
        [InlineData(2827.75, "€2,827.75")]
        [InlineData(2000, "€2,000.00")]
        public void Unsigned_euro(double value, string expected)
        {
            DashboardFormat.Euro(value).Should().Be(expected);
        }

        [Theory]
        [InlineData(45.37, "+45.4 pts")]
        [InlineData(-34.1, "−34.1 pts")]
        [InlineData(0, "0.0 pts")]
        [InlineData(-0.04, "0.0 pts")] // rounds to zero -> no sign
        public void Signed_points(double value, string expected)
        {
            DashboardFormat.SignedPoints(value).Should().Be(expected);
        }

        [Theory]
        [InlineData(0.5132, "51.3%")]
        [InlineData(1.0, "100.0%")]
        [InlineData(0.0, "0.0%")]
        public void Percent(double ratio, string expected)
        {
            DashboardFormat.Percent(ratio).Should().Be(expected);
        }

        [Fact]
        public void Percent_of_nothing_is_a_dash()
        {
            DashboardFormat.Percent(null).Should().Be("—");
        }

        [Theory]
        [InlineData(0.5, "0.5")]
        [InlineData(3, "3")]
        [InlineData(0.25, "0.25")]
        public void Volume_number(double value, string expected)
        {
            DashboardFormat.Number(value).Should().Be(expected);
        }

        [Fact]
        public void Output_does_not_depend_on_the_server_culture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                // German dev machine: would otherwise give "1.234,50".
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                DashboardFormat.SignedEuro(1234.5).Should().Be("+€1,234.50");
                DashboardFormat.Percent(0.5132).Should().Be("51.3%");
                DashboardFormat.Number(0.5).Should().Be("0.5");
                DashboardFormat.ShortDate(new DateOnly(2026, 8, 24)).Should().Be("24 Aug 2026");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
