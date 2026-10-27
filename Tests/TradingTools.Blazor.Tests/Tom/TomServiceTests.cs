using DataAccess.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.Tom;
using Models.Trades;
using NSubstitute;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Screenshots;
using TradingTools.Blazor.Services.Tom;

namespace TradingTools.Blazor.Tests.Tom
{
    /// <summary>The Tom section against an in-memory database and a real temp folder for the screenshots.</summary>
    public class TomServiceTests : IDisposable
    {
        private readonly TempFolder _webRoot = new();
        private readonly ApplicationDbContext _db;
        private readonly TomService _service;

        public TomServiceTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"tom-{Guid.NewGuid()}").Options);
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.WebRootPath.Returns(_webRoot.Path);
            _service = new TomService(_db, environment, new Ganss.Xss.HtmlSanitizer(), NullLogger<TomService>.Instance);
        }

        public void Dispose()
        {
            _db.Dispose();
            _webRoot.Dispose();
        }

        private static IFormFile Png(string name, string content = "png")
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name);
        }

        private static readonly DateOnly Day = new(2026, 10, 20);

        #region Streams

        [Fact]
        public async Task A_stream_is_created_with_its_date_link_session_and_trader()
        {
            var stream = await _service.CreateStreamAsync(Day, "  https://youtu.be/abc ", TomSession.US, TomTrader.Andi);

            var saved = await _db.TomStreams.AsNoTracking().SingleAsync();
            saved.Id.Should().Be(stream.Id);
            saved.Date.Should().Be(Day);
            saved.Url.Should().Be("https://youtu.be/abc");
            saved.Session.Should().Be(TomSession.US);
            saved.Trader.Should().Be(TomTrader.Andi);
        }

        [Fact]
        public async Task A_stream_can_be_created_without_a_link()
        {
            await _service.CreateStreamAsync(Day, " ", TomSession.EU, TomTrader.Tom);

            var saved = await _db.TomStreams.AsNoTracking().SingleAsync();
            saved.Url.Should().BeNull();
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("youtube.com/watch?v=abc")]
        public async Task A_stream_with_a_link_that_is_not_a_web_link_is_refused(string url)
        {
            var act = () => _service.CreateStreamAsync(Day, url, TomSession.EU, TomTrader.Tom);

            await act.Should().ThrowAsync<ArgumentException>();
            (await _db.TomStreams.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Changes_made_on_the_page_are_saved_and_the_notes_are_sanitized()
        {
            var stream = await _service.CreateStreamAsync(Day, "https://youtu.be/abc", TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, []);

            stream.Notes = "<p>Bias long</p><script>alert(1)</script>";
            stream.Session = TomSession.US;
            stream.Trader = TomTrader.Andi;
            trade.Notes = "<p onclick=\"x()\">Good entry</p>";
            trade.VideoSeconds = 5025;
            _service.HasUnsavedChanges.Should().BeTrue();

            await _service.SaveAsync();

            _service.HasUnsavedChanges.Should().BeFalse();
            var saved = await _db.TomStreams.AsNoTracking().Include(s => s.Trades).SingleAsync();
            saved.Notes.Should().Be("<p>Bias long</p>");
            saved.Session.Should().Be(TomSession.US);
            saved.Trader.Should().Be(TomTrader.Andi);
            saved.Trades.Single().Notes.Should().Be("<p>Good entry</p>");
            saved.Trades.Single().VideoSeconds.Should().Be(5025);
        }

        [Fact]
        public async Task A_changed_link_that_is_not_a_web_link_is_not_saved()
        {
            var stream = await _service.CreateStreamAsync(Day, "https://youtu.be/abc", TomSession.EU, TomTrader.Tom);
            stream.Url = "javascript:alert(1)";

            var act = () => _service.SaveAsync();

            await act.Should().ThrowAsync<ArgumentException>();
            (await _db.TomStreams.AsNoTracking().SingleAsync()).Url.Should().Be("https://youtu.be/abc");
        }

        [Fact]
        public async Task The_streams_come_with_their_trades_in_the_order_they_were_added()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var first = await _service.AddTradeAsync(stream, []);
            var second = await _service.AddTradeAsync(stream, []);

            var streams = await _service.GetStreamsAsync();

            streams.Single().Trades.Select(t => t.Id).Should().Equal(first.Id, second.Id);
        }

        #endregion

        #region Trades and screenshots

        [Fact]
        public async Task A_new_trade_saves_its_screenshots_in_its_own_folder_in_the_order_given()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);

            var trade = await _service.AddTradeAsync(stream, [Png("b.png"), Png("a.png")]);

            string folder = $"Screenshots/Tom/Stream {stream.Id}/Trade {trade.Id}";
            trade.ScreenshotsUrls.Should().Equal($"{folder}/b.png", $"{folder}/a.png");
            (await _db.TomTrades.AsNoTracking().SingleAsync()).ScreenshotsUrls.Should().Equal($"{folder}/b.png", $"{folder}/a.png");
            _webRoot.Files("Screenshots/Tom").Should().Equal($"Stream {stream.Id}/Trade {trade.Id}/a.png", $"Stream {stream.Id}/Trade {trade.Id}/b.png");
        }

        [Fact]
        public async Task More_screenshots_go_after_the_ones_a_trade_has()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);

            await _service.AddScreenshotsAsync(stream, trade, [Png("b.png")]);

            (await _db.TomTrades.AsNoTracking().SingleAsync()).ScreenshotsUrls!.Select(Path.GetFileName).Should().Equal("a.png", "b.png");
        }

        [Fact]
        public async Task A_screenshot_with_a_name_already_in_the_folder_does_not_overwrite_it()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("image.png", "first")]);

            await _service.AddScreenshotsAsync(stream, trade, [Png("image.png", "second")]);

            trade.ScreenshotsUrls!.Select(Path.GetFileName).Should().Equal("image.png", "image (2).png");
            File.ReadAllText(_webRoot[trade.ScreenshotsUrls![0]]).Should().Be("first");
            File.ReadAllText(_webRoot[trade.ScreenshotsUrls![1]]).Should().Be("second");
        }

        [Fact]
        public async Task Deleting_a_screenshot_removes_its_path_and_its_file_only()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png"), Png("b.png"), Png("c.png")]);
            string b = trade.ScreenshotsUrls![1];

            var outcome = await _service.DeleteScreenshotAsync(trade, b);

            outcome.Should().Be(ScreenshotFileOutcome.Deleted);
            (await _db.TomTrades.AsNoTracking().SingleAsync()).ScreenshotsUrls!.Select(Path.GetFileName).Should().Equal("a.png", "c.png");
            _webRoot.Files("Screenshots/Tom").Select(Path.GetFileName).Should().Equal("a.png", "c.png");
        }

        [Fact]
        public async Task A_screenshot_the_trade_does_not_have_changes_nothing()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);

            var act = () => _service.DeleteScreenshotAsync(trade, "Screenshots/Tom/elsewhere.png");

            await act.Should().ThrowAsync<InvalidOperationException>();
            _webRoot.Files("Screenshots/Tom").Should().HaveCount(1);
        }

        [Fact]
        public async Task A_file_a_trade_of_the_journal_also_uses_is_kept()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);
            string path = trade.ScreenshotsUrls![0];
            _db.SampleSizes.Add(new SampleSize { Id = 1, SampleSizeType = SampleSizeType.DemoTrading, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15 });
            _db.Add(new SRS { Id = 50, SampleSizeId = 1, ScreenshotsUrls = [path] });
            await _db.SaveChangesAsync();

            var outcome = await _service.DeleteScreenshotAsync(trade, path);

            outcome.Should().Be(ScreenshotFileOutcome.KeptBecauseShared);
            File.Exists(_webRoot[path]).Should().BeTrue();
        }

        [Fact]
        public async Task Deleting_a_trade_deletes_its_files_and_leaves_the_other_trades_alone()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var keep = await _service.AddTradeAsync(stream, [Png("keep.png")]);
            var gone = await _service.AddTradeAsync(stream, [Png("gone1.png"), Png("gone2.png")]);

            await _service.DeleteTradeAsync(stream, gone);

            (await _db.TomTrades.AsNoTracking().Select(t => t.Id).ToListAsync()).Should().Equal(keep.Id);
            stream.Trades.Should().Equal(keep);
            _webRoot.Files("Screenshots/Tom").Select(Path.GetFileName).Should().Equal("keep.png");
        }

        [Fact]
        public async Task Deleting_a_stream_deletes_its_trades_and_their_files_but_not_other_streams()
        {
            var other = await _service.CreateStreamAsync(Day.AddDays(-1), null, TomSession.EU, TomTrader.Tom);
            await _service.AddTradeAsync(other, [Png("other.png")]);
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            await _service.AddTradeAsync(stream, [Png("a.png")]);
            await _service.AddTradeAsync(stream, [Png("b.png")]);

            await _service.DeleteStreamAsync(stream);

            (await _db.TomStreams.AsNoTracking().Select(s => s.Id).ToListAsync()).Should().Equal(other.Id);
            (await _db.TomTrades.AsNoTracking().CountAsync()).Should().Be(1);
            _webRoot.Files("Screenshots/Tom").Select(Path.GetFileName).Should().Equal("other.png");
        }

        [Fact]
        public async Task Deleting_a_stream_never_deletes_a_file_a_trade_of_the_journal_uses()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);
            string path = trade.ScreenshotsUrls![0];
            _db.SampleSizes.Add(new SampleSize { Id = 1, SampleSizeType = SampleSizeType.DemoTrading, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15 });
            _db.Add(new SRS { Id = 50, SampleSizeId = 1, ScreenshotsUrls = [path] });
            await _db.SaveChangesAsync();

            await _service.DeleteStreamAsync(stream);

            File.Exists(_webRoot[path]).Should().BeTrue();
        }

        [Fact]
        public async Task Deleting_a_trade_of_the_journal_keeps_a_file_a_tom_trade_uses()
        {
            // The rule works both ways: the Trades page now also looks at the Tom trades.
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var tomTrade = await _service.AddTradeAsync(stream, [Png("a.png")]);
            string path = tomTrade.ScreenshotsUrls![0];
            _db.SampleSizes.Add(new SampleSize { Id = 1, SampleSizeType = SampleSizeType.DemoTrading, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15 });
            _db.Add(new SRS { Id = 50, SampleSizeId = 1, ScreenshotsUrls = [path] });
            await _db.SaveChangesAsync();
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.WebRootPath.Returns(_webRoot.Path);

            var result = await new TradeScreenshotService(_db, environment, NullLogger<TradeScreenshotService>.Instance).DeleteAsync(50, path);

            result.File.Should().Be(ScreenshotFileOutcome.KeptBecauseShared);
            File.Exists(_webRoot[path]).Should().BeTrue();
        }

        #endregion
    }
}
