using Shared.Enums;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.Calculation;

namespace TradingTools.Blazor.Tests
{
    /// <summary>
    /// Going through all the trades in full screen: within a sample size, on into the next or previous one at the ends,
    /// and the result box of the trade on screen.
    /// </summary>
    public class TradeBrowsingTests
    {
        #region Trades

        [Fact]
        public void Next_trade_stays_in_the_sample_size_while_there_is_one()
        {
            TradeBrowsing.NextTrade(tradeIndex: 2, tradeCount: 20, sampleSizePosition: 3, sampleSizeCount: 9)
                .Should().Be(new BrowseStep(3));
        }

        [Fact]
        public void After_the_last_trade_comes_the_first_trade_of_the_next_sample_size()
        {
            TradeBrowsing.NextTrade(tradeIndex: 19, tradeCount: 20, sampleSizePosition: 3, sampleSizeCount: 9)
                .Should().Be(new BrowseStep(null, 4, AtLastTrade: false));
        }

        [Fact]
        public void After_the_last_trade_of_the_last_sample_size_there_is_nothing()
        {
            TradeBrowsing.NextTrade(tradeIndex: 15, tradeCount: 16, sampleSizePosition: 8, sampleSizeCount: 9).Should().BeNull();
        }

        [Fact]
        public void Previous_trade_stays_in_the_sample_size_while_there_is_one()
        {
            TradeBrowsing.PrevTrade(tradeIndex: 5, sampleSizePosition: 3).Should().Be(new BrowseStep(4));
        }

        [Fact]
        public void Before_the_first_trade_comes_the_last_trade_of_the_previous_sample_size()
        {
            TradeBrowsing.PrevTrade(tradeIndex: 0, sampleSizePosition: 3).Should().Be(new BrowseStep(null, 2, AtLastTrade: true));
        }

        [Fact]
        public void Before_the_first_trade_of_the_first_sample_size_there_is_nothing()
        {
            TradeBrowsing.PrevTrade(tradeIndex: 0, sampleSizePosition: 0).Should().BeNull();
        }

        [Fact]
        public void In_a_date_range_the_trades_already_span_sample_sizes_so_the_ends_are_the_ends()
        {
            TradeBrowsing.NextTrade(13, 14, sampleSizePosition: 3, sampleSizeCount: 9, acrossSampleSizes: false).Should().BeNull();
            TradeBrowsing.PrevTrade(0, sampleSizePosition: 3, acrossSampleSizes: false).Should().BeNull();
            TradeBrowsing.NextTrade(5, 14, 3, 9, acrossSampleSizes: false).Should().Be(new BrowseStep(6));
        }

        [Fact]
        public void A_sample_size_with_a_single_trade_is_passed_through_both_ways()
        {
            TradeBrowsing.NextTrade(0, 1, sampleSizePosition: 1, sampleSizeCount: 3).Should().Be(new BrowseStep(null, 2));
            TradeBrowsing.PrevTrade(0, sampleSizePosition: 1).Should().Be(new BrowseStep(null, 0, AtLastTrade: true));
        }

        [Fact]
        public void Going_forward_through_everything_visits_every_trade_once_in_order()
        {
            int[] tradesPerSampleSize = [3, 1, 2];
            var visited = new List<(int SampleSize, int Trade)>();
            int position = 0, index = 0;

            for (int guard = 0; guard < 20; guard++)
            {
                visited.Add((position, index));
                var step = TradeBrowsing.NextTrade(index, tradesPerSampleSize[position], position, tradesPerSampleSize.Length);
                if (step is null) break;
                if (step.TradeIndex is { } i) index = i;
                else { position = step.SampleSizePosition!.Value; index = step.AtLastTrade ? tradesPerSampleSize[position] - 1 : 0; }
            }

            visited.Should().Equal((0, 0), (0, 1), (0, 2), (1, 0), (2, 0), (2, 1));
        }

        [Fact]
        public void Going_back_through_everything_visits_every_trade_once_in_reverse()
        {
            int[] tradesPerSampleSize = [3, 1, 2];
            var visited = new List<(int SampleSize, int Trade)>();
            int position = 2, index = 1;

            for (int guard = 0; guard < 20; guard++)
            {
                visited.Add((position, index));
                var step = TradeBrowsing.PrevTrade(index, position);
                if (step is null) break;
                if (step.TradeIndex is { } i) index = i;
                else { position = step.SampleSizePosition!.Value; index = step.AtLastTrade ? tradesPerSampleSize[position] - 1 : 0; }
            }

            visited.Should().Equal((2, 1), (2, 0), (1, 0), (0, 2), (0, 1), (0, 0));
        }

        #endregion

        #region Sample sizes

        [Fact]
        public void The_sample_size_arrows_open_a_sample_size_at_its_first_trade()
        {
            TradeBrowsing.NextSampleSize(3, 9).Should().Be(new BrowseStep(null, 4));
            TradeBrowsing.PrevSampleSize(3).Should().Be(new BrowseStep(null, 2));
        }

        [Fact]
        public void There_is_no_sample_size_before_the_first_or_after_the_last()
        {
            TradeBrowsing.PrevSampleSize(0).Should().BeNull();
            TradeBrowsing.NextSampleSize(8, 9).Should().BeNull();
            TradeBrowsing.NextSampleSize(-1, 9).Should().BeNull("a sample size that isn't in the list has no neighbours");
        }

        #endregion

        #region Result box

        [Fact]
        public void A_win_shows_its_result_in_points_and_euro_in_green()
        {
            var badge = TradeBrowsing.Result(EOutcome.Win, new TradeResultSummary(12.5, 25, 2));

            badge.Should().Be(new ResultBadge("Win", "+12.5 pts", "+€25.00", "app-gain"));
        }

        [Fact]
        public void A_loss_shows_its_result_in_red()
        {
            var badge = TradeBrowsing.Result(EOutcome.Loss, new TradeResultSummary(-30, -60, 2));

            badge.Should().Be(new ResultBadge("Loss", "−30.0 pts", "−€60.00", "app-loss"));
        }

        [Fact]
        public void A_breakeven_is_neither_green_nor_red()
        {
            TradeBrowsing.Result(EOutcome.Breakeven, new TradeResultSummary(0, 0, 0)).ToneClass.Should().Be("app-flat");
        }

        [Fact]
        public void While_the_result_is_not_known_the_outcome_still_shows_in_its_colour()
        {
            TradeBrowsing.Result(EOutcome.Win, new TradeResultSummary(null, null, 0)).Should().Be(new ResultBadge("Win", null, null, "app-gain"));
            TradeBrowsing.Result(EOutcome.Loss, null).Should().Be(new ResultBadge("Loss", null, null, "app-loss"));
        }

        [Fact]
        public void The_colour_follows_the_result_after_the_spread_not_the_label()
        {
            // A "win" of half a point with a spread of one point is a small loss in euro.
            TradeBrowsing.Result(EOutcome.Win, new TradeResultSummary(-0.5, -0.5, 1)).ToneClass.Should().Be("app-loss");
        }

        [Fact]
        public void Points_without_a_known_euro_amount_are_still_shown()
        {
            TradeBrowsing.Result(EOutcome.Win, new TradeResultSummary(10, null, 0)).Should().Be(new ResultBadge("Win", "+10.0 pts", null, "app-gain"));
        }

        #endregion
    }
}
