using Microsoft.AspNetCore.Http;
using Models.Trades;
using Models.ViewModels;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    public class ScreenshotStorageTests : IDisposable
    {
        private readonly TempFolder _webRoot = new();

        public void Dispose() => _webRoot.Dispose();

        private static IFormFile File(string name, string content = "png")
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name);
        }

        private static NewTradeVM ViewModel(SampleSizeType type = SampleSizeType.DemoTrading, TimeFrame timeFrame = TimeFrame.M15) =>
            new() { SampleSizeViewData = new SampleSizeViewData { SampleSizeType = type, TimeFrame = timeFrame, Strategy = Strategy.SRS } };

        [Fact]
        public void There_is_a_single_screenshots_root_in_every_environment()
        {
            ScreenshotStorage.GetRoot(_webRoot.Path).Should().Be(_webRoot["Screenshots"]);
        }

        [Fact]
        public async Task A_new_trade_goes_to_the_first_trade_of_the_first_sample_size()
        {
            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("chart.png")], isSampleSizeFull: false);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/chart.png");
            _webRoot.Files("Screenshots").Should().Equal("Demo Trading/SRS/15M/Sample Size 1/Trade 1/chart.png");
        }

        [Fact]
        public async Task The_next_trade_goes_into_the_latest_sample_size()
        {
            _webRoot.File("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/a.png");
            _webRoot.File("Screenshots/Demo Trading/SRS/15M/Sample Size 2/Trade 1/a.png");
            _webRoot.File("Screenshots/Demo Trading/SRS/15M/Sample Size 2/Trade 2/a.png");

            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("b.png")], isSampleSizeFull: false);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 2/Trade 3/b.png");
        }

        [Fact]
        public async Task A_full_sample_size_starts_a_new_one()
        {
            _webRoot.File("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 20/a.png");

            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("b.png")], isSampleSizeFull: true);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 2/Trade 1/b.png");
        }

        /// <summary>
        /// "Sample Size 10" sorts before "Sample Size 9" as text. The old code took the last folder by
        /// name, so from sample size 10 on it kept filing new trades into sample size 9.
        /// </summary>
        [Fact]
        public async Task Sample_size_ten_comes_after_nine()
        {
            for (int i = 1; i <= 10; i++)
                _webRoot.File($"Screenshots/Demo Trading/SRS/15M/Sample Size {i}/Trade 1/a.png");

            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("b.png")], isSampleSizeFull: false);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 10/Trade 2/b.png");
        }

        [Fact]
        public async Task A_full_tenth_sample_size_starts_the_eleventh()
        {
            for (int i = 1; i <= 10; i++)
                _webRoot.File($"Screenshots/Demo Trading/SRS/15M/Sample Size {i}/Trade 1/a.png");

            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("b.png")], isSampleSizeFull: true);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 11/Trade 1/b.png");
        }

        [Fact]
        public async Task Trade_ten_comes_after_trade_nine()
        {
            for (int i = 1; i <= 10; i++)
                _webRoot.File($"Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade {i}/a.png");

            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("b.png")], isSampleSizeFull: false);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 11/b.png");
        }

        [Fact]
        public async Task Strategy_account_and_timeframe_each_get_their_own_folders()
        {
            var paper5M = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(SampleSizeType.PaperTrade, TimeFrame.M5), new SRS(), [File("a.png")], false);
            var espresso = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(timeFrame: TimeFrame.M5), new Espresso(), [File("a.png")], false);

            paper5M.Should().Equal("Screenshots/Paper Trade/SRS/5M/Sample Size 1/Trade 1/a.png");
            espresso.Should().Equal("Screenshots/Demo Trading/Espresso/5M/Sample Size 1/Trade 1/a.png");
        }

        [Fact]
        public async Task Several_files_of_one_trade_share_its_folder_and_keep_their_order()
        {
            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("1.png"), File("2.png"), File("3.png")], false);

            paths.Should().Equal(
                "Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/1.png",
                "Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/2.png",
                "Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/3.png");
        }

        [Theory]
        [InlineData("../../escape.png")]
        [InlineData("sub/dir/escape.png")]
        public async Task A_file_name_with_folders_is_saved_by_its_name_only(string uploadedName)
        {
            string folder = _webRoot["Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1"];

            var paths = await ScreenshotStorage.SaveFilesToFolderAsync(_webRoot.Path, folder, [File(uploadedName)]);

            paths.Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/escape.png");
            _webRoot.Files().Should().Equal("Screenshots/Demo Trading/SRS/15M/Sample Size 1/Trade 1/escape.png");
        }

        [Fact]
        public async Task File_content_is_written()
        {
            var paths = await ScreenshotStorage.SaveFilesAsync(_webRoot.Path, ViewModel(), new SRS(), [File("a.png", "image-bytes")], false);

            System.IO.File.ReadAllText(_webRoot[paths[0]]).Should().Be("image-bytes");
        }
    }
}
