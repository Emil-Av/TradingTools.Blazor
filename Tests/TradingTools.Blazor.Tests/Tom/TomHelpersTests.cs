using Models.Tom;
using TradingTools.Blazor.Services.Tom;

namespace TradingTools.Blazor.Tests.Tom
{
    public class TomHelpersTests
    {
        #region Links

        [Theory]
        [InlineData("https://www.youtube.com/watch?v=abc", true)]
        [InlineData("http://youtu.be/abc", true)]
        [InlineData("  https://www.youtube.com/live/abc  ", true)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("data:text/html,hi", false)]
        [InlineData("file:///C:/x", false)]
        [InlineData("www.youtube.com/watch?v=abc", false)]
        [InlineData("not a link", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_http_and_https_links_are_web_links(string? url, bool expected) =>
            TomLinks.IsWebLink(url).Should().Be(expected);

        [Theory]
        [InlineData("https://www.youtube.com/watch?v=abc", 5025, "https://www.youtube.com/watch?v=abc&t=5025s")]
        [InlineData("https://youtu.be/abc", 90, "https://youtu.be/abc?t=90s")]
        [InlineData("https://www.youtube.com/live/abc?si=x", 0, "https://www.youtube.com/live/abc?si=x&t=0s")]
        [InlineData("https://www.youtube.com/watch?v=abc&t=10s", 75, "https://www.youtube.com/watch?v=abc&t=75s")]
        [InlineData("https://youtu.be/abc?t=10", 75, "https://youtu.be/abc?t=75s")]
        public void A_moment_in_the_video_is_the_t_parameter_replacing_one_already_there(string url, int seconds, string expected) =>
            TomLinks.At(url, seconds).Should().Be(expected);

        [Fact]
        public void Without_a_moment_the_link_is_left_as_it_is() =>
            TomLinks.At("  https://www.youtube.com/watch?v=abc ", null).Should().Be("https://www.youtube.com/watch?v=abc");

        [Fact]
        public void A_text_that_is_not_a_web_link_never_becomes_a_link()
        {
            TomLinks.At("javascript:alert(1)", 10).Should().BeNull();
            TomLinks.At(null, 10).Should().BeNull();
        }

        #endregion

        #region Time in the video

        [Theory]
        [InlineData("45", 45)]
        [InlineData("90", 90)]
        [InlineData("1:30", 90)]
        [InlineData("23:45", 1425)]
        [InlineData("1:23:45", 5025)]
        [InlineData(" 0:05 ", 5)]
        [InlineData("10:00:00", 36000)]
        public void A_time_is_hours_minutes_and_seconds(string text, int expected)
        {
            VideoTime.TryParse(text, out var seconds).Should().BeTrue();
            seconds.Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void No_text_is_no_time(string? text)
        {
            VideoTime.TryParse(text, out var seconds).Should().BeTrue();
            seconds.Should().BeNull();
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("1:75")]
        [InlineData("1:00:60")]
        [InlineData("-5")]
        [InlineData("1::2")]
        [InlineData("1:2:3:4")]
        [InlineData("1.5")]
        [InlineData("1h20m")]
        public void Anything_else_is_not_a_time(string text) =>
            VideoTime.TryParse(text, out _).Should().BeFalse();

        [Theory]
        [InlineData(5, "0:05")]
        [InlineData(90, "1:30")]
        [InlineData(3599, "59:59")]
        [InlineData(3600, "1:00:00")]
        [InlineData(5025, "1:23:45")]
        public void A_time_is_shown_like_on_youtube(int seconds, string expected) =>
            VideoTime.Format(seconds).Should().Be(expected);

        [Fact]
        public void A_shown_time_reads_back_to_the_same_seconds()
        {
            foreach (int s in new[] { 0, 59, 61, 3599, 3600, 5025, 36000 })
            {
                VideoTime.TryParse(VideoTime.Format(s), out var back).Should().BeTrue();
                back.Should().Be(s);
            }
        }

        #endregion

        #region Months

        private static TomStream Stream(int id, int year, int month, int day) => new() { Id = id, Date = new DateOnly(year, month, day) };

        [Fact]
        public void Streams_are_grouped_by_month_newest_month_and_stream_first()
        {
            var months = TomMonths.Group([
                Stream(1, 2026, 9, 3), Stream(2, 2026, 10, 1), Stream(3, 2026, 9, 28), Stream(4, 2026, 10, 20), Stream(5, 2025, 10, 5)]);

            months.Select(m => m.Label).Should().Equal("October 2026", "September 2026", "October 2025");
            months[0].Streams.Select(s => s.Id).Should().Equal(4, 2);
            months[1].Streams.Select(s => s.Id).Should().Equal(3, 1);
            months[2].Streams.Select(s => s.Id).Should().Equal(5);
        }

        [Fact]
        public void Two_streams_on_one_day_show_the_one_added_last_first()
        {
            var months = TomMonths.Group([Stream(7, 2026, 10, 5), Stream(8, 2026, 10, 5)]);

            months.Single().Streams.Select(s => s.Id).Should().Equal(8, 7);
        }

        [Fact]
        public void No_streams_no_months() => TomMonths.Group([]).Should().BeEmpty();

        #endregion
    }
}
