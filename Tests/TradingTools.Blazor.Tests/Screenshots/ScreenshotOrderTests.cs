using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    /// <summary>Screenshots are uploaded and saved by date, the oldest first.</summary>
    public class ScreenshotOrderTests
    {
        private static IBrowserFile File(string name, DateTimeOffset modified)
        {
            var file = Substitute.For<IBrowserFile>();
            file.Name.Returns(name);
            file.LastModified.Returns(modified);
            return file;
        }

        private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 5, 6, hour, minute, 0, TimeSpan.Zero);

        [Fact]
        public void The_oldest_screenshot_comes_first()
        {
            var newest = File("c.png", At(16, 55));
            var oldest = File("a.png", At(16, 36));
            var middle = File("b.png", At(16, 41));

            ScreenshotOrder.OldestFirst([newest, oldest, middle]).Should().Equal(oldest, middle, newest);
        }

        [Fact]
        public void The_date_decides_not_the_name()
        {
            var firstTaken = File("z.png", At(9));
            var lastTaken = File("a.png", At(10));

            ScreenshotOrder.OldestFirst([lastTaken, firstTaken]).Should().Equal(firstTaken, lastTaken);
        }

        [Fact]
        public void Screenshots_from_different_days_are_ordered_by_day_first()
        {
            var yesterdayEvening = File("a.png", At(22).AddDays(-1));
            var todayMorning = File("b.png", At(8));

            ScreenshotOrder.OldestFirst([todayMorning, yesterdayEvening]).Should().Equal(yesterdayEvening, todayMorning);
        }

        [Fact]
        public void Files_with_the_same_date_are_ordered_by_name_and_stay_in_a_fixed_order()
        {
            var b = File("US500_2026-05-06_16-41-04.png", At(16));
            var a = File("US500_2026-05-06_16-36-05.png", At(16));
            var c = File("US500_2026-05-06_16-55-14.png", At(16));

            ScreenshotOrder.OldestFirst([b, c, a]).Should().Equal(a, b, c);
            ScreenshotOrder.OldestFirst([c, a, b]).Should().Equal(a, b, c);
        }

        [Fact]
        public void Adding_more_files_later_sorts_them_in_with_the_ones_already_picked()
        {
            var first = File("a.png", At(10));
            var third = File("c.png", At(12));
            var second = File("b.png", At(11));

            var picked = ScreenshotOrder.OldestFirst([first, third]);
            picked.Add(second);

            ScreenshotOrder.OldestFirst(picked).Should().Equal(first, second, third);
        }

        [Fact]
        public void Nothing_picked_is_an_empty_list() => ScreenshotOrder.OldestFirst([]).Should().BeEmpty();
    }
}
