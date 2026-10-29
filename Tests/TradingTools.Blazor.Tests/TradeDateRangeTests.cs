using Models.Trades;
using TradingTools.Blazor.Services;

namespace TradingTools.Blazor.Tests
{
    /// <summary>The From / To dates of the Trades page: which trades are in, and in what order.</summary>
    public class TradeDateRangeTests
    {
        private static readonly DateOnly Oct1 = new(2026, 10, 1), Oct5 = new(2026, 10, 5), Oct10 = new(2026, 10, 10);

        private static SRS Trade(int id, DateOnly date, int sampleSizeId = 1, int createdMinute = 0) =>
            new() { Id = id, Date = date, SampleSizeId = sampleSizeId, CreatedAt = date.ToDateTime(new TimeOnly(12, createdMinute)) };

        [Fact]
        public void No_dates_is_no_range()
        {
            new TradeDateRange(null, null).IsSet.Should().BeFalse();
            new TradeDateRange(Oct1, null).IsSet.Should().BeTrue();
            new TradeDateRange(null, Oct1).IsSet.Should().BeTrue();
        }

        [Fact]
        public void Both_days_are_included()
        {
            var range = new TradeDateRange(Oct1, Oct5);

            range.Includes(Oct1).Should().BeTrue();
            range.Includes(Oct5).Should().BeTrue();
            range.Includes(new DateOnly(2026, 10, 3)).Should().BeTrue();
            range.Includes(new DateOnly(2026, 9, 30)).Should().BeFalse();
            range.Includes(new DateOnly(2026, 10, 6)).Should().BeFalse();
        }

        [Fact]
        public void Either_end_can_be_open()
        {
            new TradeDateRange(Oct5, null).Includes(new DateOnly(2030, 1, 1)).Should().BeTrue();
            new TradeDateRange(Oct5, null).Includes(Oct1).Should().BeFalse();
            new TradeDateRange(null, Oct5).Includes(new DateOnly(2000, 1, 1)).Should().BeTrue();
            new TradeDateRange(null, Oct5).Includes(Oct10).Should().BeFalse();
        }

        [Fact]
        public void From_after_to_has_nothing_in_it()
        {
            var range = new TradeDateRange(Oct10, Oct1);

            range.IsBackwards.Should().BeTrue();
            range.Apply([Trade(1, Oct5)]).Should().BeEmpty();
            new TradeDateRange(Oct1, Oct1).IsBackwards.Should().BeFalse();
        }

        [Fact]
        public void The_trades_of_several_sample_sizes_are_listed_together_oldest_first()
        {
            var trades = new[]
            {
                Trade(30, Oct10, sampleSizeId: 3),
                Trade(10, Oct1, sampleSizeId: 1),
                Trade(20, Oct5, sampleSizeId: 2),
                Trade(11, new DateOnly(2026, 9, 20), sampleSizeId: 1), // before the range
            };

            new TradeDateRange(Oct1, Oct10).Apply(trades).Select(t => t.Id).Should().Equal(10, 20, 30);
        }

        [Fact]
        public void Trades_on_the_same_day_are_in_the_order_they_were_logged_then_by_id()
        {
            var trades = new[] { Trade(3, Oct5, createdMinute: 30), Trade(2, Oct5, createdMinute: 10), Trade(1, Oct5, createdMinute: 10) };

            new TradeDateRange(Oct5, Oct5).Apply(trades).Select(t => t.Id).Should().Equal(1, 2, 3);
        }

        [Theory]
        [InlineData(2026, 10, 1, 2026, 10, 15, "1 Oct 2026 - 15 Oct 2026")]
        [InlineData(2026, 10, 1, null, null, null, "from 1 Oct 2026")]
        [InlineData(null, null, null, 2026, 10, 15, "until 15 Oct 2026")]
        public void A_range_reads_as_dates(int? fy, int? fm, int? fd, int? ty, int? tm, int? td, string expected)
        {
            DateOnly? from = fy is null ? null : new DateOnly(fy.Value, fm!.Value, fd!.Value);
            DateOnly? to = ty is null ? null : new DateOnly(ty.Value, tm!.Value, td!.Value);

            new TradeDateRange(from, to).Describe().Should().Be(expected);
        }
    }
}
