using Microsoft.AspNetCore.Http;
using Models.Screenshots;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    /// <summary>The old Screenshots folder's root and the helpers for screenshots in the database.</summary>
    public class ScreenshotStorageTests
    {
        [Fact]
        public void There_is_a_single_screenshots_root_in_every_environment()
        {
            ScreenshotStorage.GetRoot("/web").Should().Be(Path.Combine("/web", "Screenshots"));
        }

        [Theory]
        [InlineData("chart.png", "chart.png")]
        [InlineData("../../evil.png", "evil.png")]
        [InlineData(@"C:\Users\me\chart.png", "chart.png")]
        [InlineData("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/DE40.png", "DE40.png")]
        [InlineData(@"/Screenshots\Research\Trade 3\BTC.png", "BTC.png")]
        [InlineData("", "screenshot.png")]
        public void Only_the_file_name_is_kept(string name, string expected)
        {
            TradeScreenshots.FileNameOf(name).Should().Be(expected);
        }

        [Fact]
        public async Task Uploaded_files_become_screenshots_in_the_order_given_with_their_images()
        {
            static IFormFile File(string name, string content)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name);
            }

            var screenshots = await TradeScreenshots.FromFilesAsync([File("b.png", "B"), File("a.png", "A")], firstPosition: 3);

            screenshots.Select(s => (s.Position, s.FileName, System.Text.Encoding.UTF8.GetString(s.Content!.Data)))
                .Should().Equal((3, "b.png", "B"), (4, "a.png", "A"));
        }

        [Fact]
        public void New_screenshots_go_after_the_last_position()
        {
            TradeScreenshots.NextPosition([]).Should().Be(0);
            TradeScreenshots.NextPosition([new Screenshot { Position = 0 }, new Screenshot { Position = 4 }]).Should().Be(5);
        }

        [Theory]
        [InlineData("chart.png", "image/png")]
        [InlineData("chart.JPG", "image/jpeg")]
        [InlineData("chart.webp", "image/webp")]
        [InlineData("chart", "application/octet-stream")]
        public void The_image_type_comes_from_the_file_name(string fileName, string expected)
        {
            TradeScreenshots.ContentTypeOf(fileName).Should().Be(expected);
        }
    }
}
