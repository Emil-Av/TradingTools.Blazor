using DataAccess.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models.Tom;
using TradingTools.Blazor.Services.Tom;

namespace TradingTools.Blazor.Tests.Tom
{
    /// <summary>The Tom section against an in-memory database; the screenshots are in it too.</summary>
    public class TomServiceTests : IDisposable
    {
        private readonly DbContextOptions<ApplicationDbContext> _options =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"tom-{Guid.NewGuid()}").Options;
        private readonly ApplicationDbContext _db;
        private readonly TomService _service;

        public TomServiceTests()
        {
            _db = new ApplicationDbContext(_options);
            _service = new TomService(_db, new Ganss.Xss.HtmlSanitizer());
        }

        public void Dispose() => _db.Dispose();

        /// <summary>What is saved, read with a context of its own (not what the page's context still holds).</summary>
        private async Task<List<(int TradeId, int Position, string FileName, string Image)>> SavedScreenshots()
        {
            await using var db = new ApplicationDbContext(_options);
            return [.. (await db.Screenshots.AsNoTracking().Include(s => s.Content).ToListAsync())
                .OrderBy(s => s.TomTradeId).ThenBy(s => s.Position)
                .Select(s => (s.TomTradeId!.Value, s.Position, s.FileName, System.Text.Encoding.UTF8.GetString(s.Content!.Data)))];
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
        public async Task A_new_trade_saves_its_screenshots_in_the_database_in_the_order_given()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);

            var trade = await _service.AddTradeAsync(stream, [Png("b.png", "B"), Png("a.png", "A")]);

            (await SavedScreenshots()).Should().Equal((trade.Id, 0, "b.png", "B"), (trade.Id, 1, "a.png", "A"));
            trade.Screenshots.Select(s => s.FileName).Should().Equal("b.png", "a.png");
            trade.ScreenshotsUrls.Should().BeEmpty("the old paths are no longer written");
        }

        [Fact]
        public async Task The_page_keeps_the_screenshots_but_not_their_images()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);

            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);

            trade.Screenshots.Should().ContainSingle().Which.Content.Should().BeNull();
            _db.ChangeTracker.Entries<Models.Screenshots.ScreenshotContent>().Should().BeEmpty();
            (await SavedScreenshots()).Should().ContainSingle();
        }

        [Fact]
        public async Task More_screenshots_go_after_the_ones_a_trade_has()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);

            await _service.AddScreenshotsAsync(stream, trade, [Png("b.png"), Png("c.png")]);

            (await SavedScreenshots()).Select(s => (s.Position, s.FileName)).Should().Equal((0, "a.png"), (1, "b.png"), (2, "c.png"));
            trade.Screenshots.Select(s => s.FileName).Should().Equal("a.png", "b.png", "c.png");
        }

        [Fact]
        public async Task Two_screenshots_with_the_same_name_are_both_kept()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("image.png", "first")]);

            await _service.AddScreenshotsAsync(stream, trade, [Png("image.png", "second")]);

            (await SavedScreenshots()).Select(s => s.Image).Should().Equal("first", "second");
        }

        [Fact]
        public async Task Deleting_a_screenshot_removes_that_one_only_and_the_others_keep_their_positions()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png"), Png("b.png"), Png("c.png")]);

            await _service.DeleteScreenshotAsync(trade, trade.Screenshots[1].Id);

            (await SavedScreenshots()).Select(s => (s.Position, s.FileName)).Should().Equal((0, "a.png"), (2, "c.png"));
            trade.Screenshots.Select(s => s.FileName).Should().Equal("a.png", "c.png");
        }

        [Fact]
        public async Task More_screenshots_after_a_deleted_one_go_after_the_last()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png"), Png("b.png")]);
            await _service.DeleteScreenshotAsync(trade, trade.Screenshots[0].Id);

            await _service.AddScreenshotsAsync(stream, trade, [Png("c.png")]);

            (await SavedScreenshots()).Select(s => (s.Position, s.FileName)).Should().Equal((1, "b.png"), (2, "c.png"));
        }

        [Fact]
        public async Task A_screenshot_the_trade_does_not_have_changes_nothing()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png")]);
            var other = await _service.AddTradeAsync(stream, [Png("other.png")]);

            var act = () => _service.DeleteScreenshotAsync(trade, other.Screenshots[0].Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await SavedScreenshots()).Should().HaveCount(2);
        }

        [Fact]
        public async Task Deleting_a_trade_deletes_its_screenshots_and_leaves_the_other_trades_alone()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var keep = await _service.AddTradeAsync(stream, [Png("keep.png")]);
            var gone = await _service.AddTradeAsync(stream, [Png("gone1.png"), Png("gone2.png")]);

            await _service.DeleteTradeAsync(stream, gone);

            (await _db.TomTrades.AsNoTracking().Select(t => t.Id).ToListAsync()).Should().Equal(keep.Id);
            stream.Trades.Should().Equal(keep);
            (await SavedScreenshots()).Select(s => s.FileName).Should().Equal("keep.png");
        }

        [Fact]
        public async Task Deleting_a_stream_deletes_its_trades_and_their_screenshots_but_not_other_streams()
        {
            var other = await _service.CreateStreamAsync(Day.AddDays(-1), null, TomSession.EU, TomTrader.Tom);
            await _service.AddTradeAsync(other, [Png("other.png")]);
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            await _service.AddTradeAsync(stream, [Png("a.png")]);
            await _service.AddTradeAsync(stream, [Png("b.png")]);

            await _service.DeleteStreamAsync(stream);

            (await _db.TomStreams.AsNoTracking().Select(s => s.Id).ToListAsync()).Should().Equal(other.Id);
            (await _db.TomTrades.AsNoTracking().CountAsync()).Should().Be(1);
            (await SavedScreenshots()).Select(s => s.FileName).Should().Equal("other.png");
        }

        [Fact]
        public async Task The_streams_come_with_their_trades_screenshots_in_carousel_order()
        {
            var stream = await _service.CreateStreamAsync(Day, null, TomSession.EU, TomTrader.Tom);
            var trade = await _service.AddTradeAsync(stream, [Png("a.png"), Png("b.png"), Png("c.png")]);

            await using var db = new ApplicationDbContext(_options);
            var streams = await new TomService(db, new Ganss.Xss.HtmlSanitizer()).GetStreamsAsync();

            streams.Single().Trades.Single().Screenshots.Select(s => s.FileName).Should().Equal("a.png", "b.png", "c.png");
            streams.Single().Trades.Single().Screenshots.Should().OnlyContain(s => s.Content == null);
        }

        #endregion
    }
}
