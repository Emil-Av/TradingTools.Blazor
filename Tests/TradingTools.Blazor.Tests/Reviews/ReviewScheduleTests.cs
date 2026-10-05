using Models;
using TradingTools.Blazor.Services.Reviews;

namespace TradingTools.Blazor.Tests.Reviews
{
    /// <summary>
    /// A sample size has 20 trades. A review of every 5 trades is due as soon as the trade that completes the
    /// block is logged, a summary once the sample size is complete; each stays due until it has text.
    /// </summary>
    public class ReviewScheduleTests
    {
        private static Review Reviewed(params ReviewKind[] filled)
        {
            var review = new Review();
            foreach (var kind in filled) ReviewSchedule.SetText(review, kind, "<p>Done.</p>");
            return review;
        }

        #region What is due

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void Nothing_is_due_before_the_fifth_trade(int trades) =>
            ReviewSchedule.Due(trades, new Review()).Should().BeEmpty();

        [Theory]
        [InlineData(5, new[] { ReviewKind.First })]
        [InlineData(9, new[] { ReviewKind.First })]
        [InlineData(10, new[] { ReviewKind.First, ReviewKind.Second })]
        [InlineData(14, new[] { ReviewKind.First, ReviewKind.Second })]
        [InlineData(15, new[] { ReviewKind.First, ReviewKind.Second, ReviewKind.Third })]
        [InlineData(19, new[] { ReviewKind.First, ReviewKind.Second, ReviewKind.Third })]
        public void A_review_is_due_once_its_block_of_five_trades_is_there(int trades, ReviewKind[] expected) =>
            ReviewSchedule.Due(trades, new Review()).Should().Equal(expected);

        [Fact]
        public void The_twentieth_trade_makes_the_fourth_review_and_the_summary_due()
        {
            ReviewSchedule.Due(20, new Review()).Should().Equal(
                ReviewKind.First, ReviewKind.Second, ReviewKind.Third, ReviewKind.Fourth, ReviewKind.Summary);
        }

        [Fact]
        public void A_complete_sample_size_never_has_more_reviews_due_than_there_are()
        {
            ReviewSchedule.Due(25, new Review()).Should().HaveCount(5);
        }

        [Fact]
        public void A_written_review_is_no_longer_due_and_the_others_stay_due()
        {
            ReviewSchedule.Due(15, Reviewed(ReviewKind.Second)).Should().Equal(ReviewKind.First, ReviewKind.Third);
        }

        [Fact]
        public void The_summary_waits_for_the_sample_size_to_be_complete_even_when_all_blocks_are_reviewed()
        {
            var review = Reviewed(ReviewKind.First, ReviewKind.Second, ReviewKind.Third);

            ReviewSchedule.Due(19, review).Should().BeEmpty();
            ReviewSchedule.Due(20, review).Should().Equal(ReviewKind.Fourth, ReviewKind.Summary);
        }

        [Fact]
        public void Everything_written_means_nothing_is_due()
        {
            ReviewSchedule.Due(20, Reviewed(ReviewSchedule.All.ToArray())).Should().BeEmpty();
        }

        [Fact]
        public void Empty_editor_html_does_not_count_as_a_written_review()
        {
            var review = new Review { First = "<p><br></p>", Second = "<p>&nbsp;</p>" };

            ReviewSchedule.Due(10, review).Should().Equal(ReviewKind.First, ReviewKind.Second);
        }

        [Theory]
        [InlineData(4, new ReviewKind[0])]
        [InlineData(5, new[] { ReviewKind.First })]
        [InlineData(6, new ReviewKind[0])]
        [InlineData(10, new[] { ReviewKind.Second })]
        [InlineData(15, new[] { ReviewKind.Third })]
        [InlineData(19, new ReviewKind[0])]
        [InlineData(20, new[] { ReviewKind.Fourth, ReviewKind.Summary })]
        [InlineData(21, new ReviewKind[0])]
        public void A_review_becomes_ready_exactly_when_the_trade_completing_its_block_is_logged(int trades, ReviewKind[] expected) =>
            ReviewSchedule.ReachedAt(trades).Should().Equal(expected);

        #endregion

        #region Written or not

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<p></p>")]
        [InlineData("<p><br></p>")]
        [InlineData("<p><br/></p>")]
        [InlineData("<p>&nbsp;</p>")]
        [InlineData("&nbsp;")]
        [InlineData("<p> </p><p><br></p><p>&nbsp;</p>")]
        [InlineData("<div><span></span></div>")]
        public void Empty_texts_are_not_filled(string? html) => ReviewSchedule.IsFilled(html).Should().BeFalse();

        [Theory]
        [InlineData("Good")]
        [InlineData("<p>Good</p>")]
        [InlineData("<p><br></p><p>Second line</p>")]
        [InlineData("<p><strong>x</strong></p>")]
        [InlineData("<ul><li>one</li></ul>")]
        [InlineData("<p>caf&eacute;</p>")]
        public void A_text_with_something_in_it_is_filled(string html) => ReviewSchedule.IsFilled(html).Should().BeTrue();

        #endregion

        #region Labels and fields

        [Fact]
        public void The_reviews_cover_blocks_of_five_trades_and_then_the_whole_sample_size()
        {
            ReviewSchedule.All.Select(ReviewSchedule.Covers).Should().Equal(
                "trades 1-5", "trades 6-10", "trades 11-15", "trades 16-20", "the whole sample size");
            ReviewSchedule.All.Select(ReviewSchedule.TradesNeeded).Should().Equal(5, 10, 15, 20, 20);
            ReviewSchedule.All.Select(ReviewSchedule.Label).Should().Equal(
                "First review", "Second review", "Third review", "Fourth review", "Summary");
        }

        [Fact]
        public void Each_review_is_its_own_field_of_the_review_record()
        {
            var review = new Review();
            foreach (var kind in ReviewSchedule.All) ReviewSchedule.SetText(review, kind, kind.ToString());

            review.First.Should().Be("First");
            review.Second.Should().Be("Second");
            review.Third.Should().Be("Third");
            review.Forth.Should().Be("Fourth"); // the field is still spelled Forth
            review.Summary.Should().Be("Summary");
            ReviewSchedule.All.Select(kind => ReviewSchedule.TextOf(review, kind)).Should().Equal("First", "Second", "Third", "Fourth", "Summary");
        }

        #endregion
    }
}
