using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Reviews;

namespace TradingTools.Blazor.Tests.Reviews
{
    /// <summary>Which sample sizes of the default account have reviews to do, against an in-memory database.</summary>
    public class ReviewServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;

        public ReviewServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"reviews-{Guid.NewGuid()}")
                .Options;
            _db = new ApplicationDbContext(options);
        }

        public void Dispose() => _db.Dispose();

        private ReviewService Service(SampleSizeType defaultAccount = SampleSizeType.DemoTrading) =>
            new(_db, TestSettings.WithDefaultAccount(defaultAccount));

        /// <summary>A sample size with its review record and the given number of trades.</summary>
        private async Task<SampleSize> AddSampleSize(
            int id, int trades, Review? review, SampleSizeType type = SampleSizeType.DemoTrading,
            Strategy strategy = Strategy.SRS, TimeFrame timeFrame = TimeFrame.M15)
        {
            if (review is not null)
            {
                review.Id = id;
                _db.Reviews.Add(review);
            }

            var sampleSize = new SampleSize
            {
                Id = id, SampleSizeType = type, Strategy = strategy, TimeFrame = timeFrame, ReviewId = review?.Id
            };
            _db.SampleSizes.Add(sampleSize);

            for (int i = 0; i < trades; i++)
            {
                _db.Add(new SRS { SampleSizeId = id, Date = new DateOnly(2026, 9, 1).AddDays(i), Status = EStatus.Closed });
            }

            await _db.SaveChangesAsync();
            return sampleSize;
        }

        private static Review WithFirstWritten() => new() { First = "<p>Reviewed.</p>" };

        [Fact]
        public async Task A_sample_size_with_five_trades_has_its_first_review_due()
        {
            await AddSampleSize(1, trades: 5, new Review());

            var due = (await Service().GetDueAsync()).Should().ContainSingle().Subject;

            due.SampleSizeId.Should().Be(1);
            due.Reviews.Should().Equal(ReviewKind.First);
            due.TradeCount.Should().Be(5);
            due.IsComplete.Should().BeFalse();
        }

        [Fact]
        public async Task Fewer_than_five_trades_have_no_review_due()
        {
            await AddSampleSize(1, trades: 4, new Review());

            (await Service().GetDueAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task A_complete_sample_size_has_the_rest_of_its_reviews_and_the_summary_due()
        {
            await AddSampleSize(1, trades: 20, WithFirstWritten());

            var due = (await Service().GetDueAsync()).Single();

            due.Reviews.Should().Equal(ReviewKind.Second, ReviewKind.Third, ReviewKind.Fourth, ReviewKind.Summary);
            due.IsComplete.Should().BeTrue();
        }

        [Fact]
        public async Task A_fully_reviewed_sample_size_is_not_listed()
        {
            var review = new Review { First = "a", Second = "b", Third = "c", Forth = "d", Summary = "e" };
            await AddSampleSize(1, trades: 20, review);

            (await Service().GetDueAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Only_the_default_accounts_sample_sizes_are_listed()
        {
            await AddSampleSize(1, trades: 5, new Review(), SampleSizeType.DemoTrading);
            await AddSampleSize(2, trades: 10, new Review(), SampleSizeType.Trade);

            (await Service(SampleSizeType.DemoTrading).GetDueAsync()).Select(d => d.SampleSizeId).Should().Equal(1);
            (await Service(SampleSizeType.Trade).GetDueAsync()).Select(d => d.SampleSizeId).Should().Equal(2);
        }

        [Fact]
        public async Task Research_sample_sizes_are_never_reviewed_here()
        {
            await AddSampleSize(1, trades: 20, new Review(), SampleSizeType.Research);

            (await Service().GetDueAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task A_sample_size_without_a_review_record_is_left_out_because_it_cannot_be_reviewed()
        {
            await AddSampleSize(1, trades: 20, review: null);

            (await Service().GetDueAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task The_newest_sample_size_comes_first()
        {
            await AddSampleSize(1, trades: 5, new Review());
            await AddSampleSize(2, trades: 5, new Review());
            await AddSampleSize(3, trades: 5, new Review());

            (await Service().GetDueAsync()).Select(d => d.SampleSizeId).Should().Equal(3, 2, 1);
        }

        [Fact]
        public async Task The_sample_size_is_numbered_among_its_strategy_and_timeframe_like_on_the_trades_page()
        {
            await AddSampleSize(1, trades: 20, new Review { First = "x", Second = "x", Third = "x", Forth = "x", Summary = "x" });
            await AddSampleSize(2, trades: 5, new Review());                                                       // #2 of SRS 15M
            await AddSampleSize(3, trades: 5, new Review(), strategy: Strategy.Espresso, timeFrame: TimeFrame.M5); // #1 of Espresso 5M

            var due = (await Service().GetDueAsync()).ToDictionary(d => d.SampleSizeId);

            due[2].SampleSizeNumber.Should().Be(2);
            due[3].SampleSizeNumber.Should().Be(1);
        }

        [Fact]
        public async Task The_trades_of_other_sample_sizes_do_not_count()
        {
            await AddSampleSize(1, trades: 3, new Review());
            await AddSampleSize(2, trades: 3, new Review());

            (await Service().GetDueAsync()).Should().BeEmpty("3 + 3 trades in two sample sizes is not 5 in one");
        }

        [Fact]
        public async Task One_sample_size_can_be_asked_for_directly()
        {
            await AddSampleSize(1, trades: 10, new Review());
            await AddSampleSize(2, trades: 3, new Review());

            var due = await Service().GetDueForSampleSizeAsync(1);

            due!.Reviews.Should().Equal(ReviewKind.First, ReviewKind.Second);
            (await Service().GetDueForSampleSizeAsync(2)).Should().BeNull("nothing is due yet");
            (await Service().GetDueForSampleSizeAsync(999)).Should().BeNull("there is no such sample size");
        }

        [Fact]
        public async Task A_review_that_gets_written_is_no_longer_due()
        {
            await AddSampleSize(1, trades: 5, new Review());
            (await Service().GetDueAsync()).Should().ContainSingle();

            var review = await _db.Reviews.SingleAsync();
            review.First = "<p>Looked at the five trades.</p>";
            await _db.SaveChangesAsync();

            (await Service().GetDueAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task A_new_trade_makes_the_next_review_due()
        {
            await AddSampleSize(1, trades: 9, new Review { First = "done" });
            (await Service().GetDueAsync()).Should().BeEmpty();

            _db.Add(new SRS { SampleSizeId = 1, Date = new DateOnly(2026, 10, 1), Status = EStatus.Closed });
            await _db.SaveChangesAsync();

            (await Service().GetDueAsync()).Single().Reviews.Should().Equal(ReviewKind.Second);
        }

        [Fact]
        public async Task Open_trades_count_too_the_review_is_ready_as_soon_as_the_trade_is_logged()
        {
            await AddSampleSize(1, trades: 4, new Review());
            _db.Add(new SRS { SampleSizeId = 1, Date = new DateOnly(2026, 10, 1), Status = EStatus.Opened });
            await _db.SaveChangesAsync();

            (await Service().GetDueAsync()).Single().Reviews.Should().Equal(ReviewKind.First);
        }
    }
}
