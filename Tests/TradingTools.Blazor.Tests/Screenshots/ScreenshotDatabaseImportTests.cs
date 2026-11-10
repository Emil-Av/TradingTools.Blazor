using DataAccess.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Models;
using Models.Screenshots;
using Models.Tom;
using Models.Trades;
using NSubstitute;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    /// <summary>
    /// Moving the screenshots from the folder into the database: every screenshot lands at its own trade and position,
    /// with the exact bytes of its file, nothing on disk or in the old paths changes, and it never runs twice.
    /// </summary>
    public class ScreenshotDatabaseImportTests : IDisposable
    {
        private readonly TempFolder _webRoot = new();
        private readonly DbContextOptions<ApplicationDbContext> _options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"import-{Guid.NewGuid()}")
                // The in-memory database has no transactions; the real one (PostgreSQL) does.
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        public void Dispose() => _webRoot.Dispose();

        private ApplicationDbContext NewDb() => new(_options);

        private async Task<ScreenshotDatabaseImport.Result> Import()
        {
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.WebRootPath.Returns(_webRoot.Path);
            await using var db = NewDb();
            return await new ScreenshotDatabaseImport(db, environment, new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero)),
                NullLogger<ScreenshotDatabaseImport>.Instance).RunAsync();
        }

        private async Task AddTrades(params BaseTrade[] trades)
        {
            await using var db = NewDb();
            if (!await db.SampleSizes.AnyAsync())
                db.SampleSizes.Add(new SampleSize { Id = 1, SampleSizeType = SampleSizeType.DemoTrading, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15 });
            foreach (var trade in trades) { trade.SampleSizeId = 1; db.Add(trade); }
            await db.SaveChangesAsync();
        }

        /// <summary>Every screenshot in the database as (journal trade, Tom trade, position, old path, image).</summary>
        private async Task<List<(int? Trade, int? TomTrade, int Position, string? Path, string FileName, string Image)>> Saved()
        {
            await using var db = NewDb();
            return [.. (await db.Screenshots.AsNoTracking().Include(s => s.Content).ToListAsync())
                .OrderBy(s => s.BaseTradeId).ThenBy(s => s.TomTradeId).ThenBy(s => s.Position)
                .Select(s => (s.BaseTradeId, s.TomTradeId, s.Position, s.LegacyPath, s.FileName, System.Text.Encoding.UTF8.GetString(s.Content!.Data)))];
        }

        private const string Folder = "Screenshots/Demo Trading/SRS/15M/Sample Size 1";

        [Fact]
        public async Task Every_screenshot_goes_to_its_own_trade_at_its_position_with_the_bytes_of_its_file()
        {
            _webRoot.File($"{Folder}/Trade 1/a.png", "A1");
            _webRoot.File($"{Folder}/Trade 1/b.png", "B1");
            _webRoot.File($"{Folder}/Trade 2/a.png", "A2");
            await AddTrades(
                new SRS { Id = 10, ScreenshotsUrls = [$"{Folder}/Trade 1/b.png", $"{Folder}/Trade 1/a.png"] },
                new SRS { Id = 11, ScreenshotsUrls = [$"{Folder}/Trade 2/a.png"] });

            var result = await Import();

            result.Should().BeEquivalentTo(new { Ran = true, Imported = 3, MissingPaths = Array.Empty<string>() });
            (await Saved()).Should().Equal(
                (10, null, 0, $"{Folder}/Trade 1/b.png", "b.png", "B1"),
                (10, null, 1, $"{Folder}/Trade 1/a.png", "a.png", "A1"),
                (11, null, 0, $"{Folder}/Trade 2/a.png", "a.png", "A2"));
        }

        [Fact]
        public async Task The_folder_a_file_is_in_does_not_decide_its_trade_the_trades_paths_do()
        {
            // Trade 20's screenshot lies in the "Trade 1" folder (as after a renumbering); it still belongs to trade 20.
            _webRoot.File($"{Folder}/Trade 1/x.png", "X");
            await AddTrades(new SRS { Id = 20, ScreenshotsUrls = [$"{Folder}/Trade 1/x.png"] });

            await Import();

            (await Saved()).Should().ContainSingle().Which.Trade.Should().Be(20);
        }

        [Fact]
        public async Task Tom_trades_get_their_own_screenshots()
        {
            _webRoot.File("Screenshots/Tom/Stream 1/Trade 3/t.png", "T");
            await using (var db = NewDb())
            {
                db.TomStreams.Add(new TomStream { Id = 1, Trades = [new TomTrade { Id = 3, ScreenshotsUrls = ["Screenshots/Tom/Stream 1/Trade 3/t.png"] }] });
                await db.SaveChangesAsync();
            }

            await Import();

            (await Saved()).Should().Equal((null, 3, 0, "Screenshots/Tom/Stream 1/Trade 3/t.png", "t.png", "T"));
        }

        [Fact]
        public async Task The_same_file_twice_in_a_trade_and_in_two_trades_is_stored_for_each()
        {
            _webRoot.File($"{Folder}/Trade 1/shared.png", "S");
            await AddTrades(
                new SRS { Id = 30, ScreenshotsUrls = [$"{Folder}/Trade 1/shared.png", $"{Folder}/Trade 1/shared.png"] },
                new SRS { Id = 31, ScreenshotsUrls = [$"{Folder}/Trade 1/shared.png"] });

            await Import();

            (await Saved()).Select(s => (s.Trade, s.Position, s.Image)).Should().Equal((30, 0, "S"), (30, 1, "S"), (31, 0, "S"));
        }

        [Fact]
        public async Task Old_research_paths_with_a_leading_slash_and_backslashes_are_found()
        {
            _webRoot.File("Screenshots/Research/ResearchCradle/Trade 1/r.png", "R");
            await AddTrades(new SRS { Id = 40, ScreenshotsUrls = [@"/Screenshots\Research\ResearchCradle\Trade 1\r.png"] });

            await Import();

            (await Saved()).Should().Equal((40, null, 0, @"/Screenshots\Research\ResearchCradle\Trade 1\r.png", "r.png", "R"));
        }

        [Fact]
        public async Task A_missing_file_is_reported_and_the_other_screenshots_keep_their_positions()
        {
            _webRoot.File($"{Folder}/Trade 1/a.png", "A");
            _webRoot.File($"{Folder}/Trade 1/c.png", "C");
            await AddTrades(new SRS { Id = 50, ScreenshotsUrls = [$"{Folder}/Trade 1/a.png", $"{Folder}/Trade 1/gone.png", $"{Folder}/Trade 1/c.png"] });

            var result = await Import();

            result.Imported.Should().Be(2);
            result.MissingPaths.Should().ContainSingle().Which.Should().Contain("trade 50").And.Contain("gone.png");
            (await Saved()).Select(s => (s.Position, s.Image)).Should().Equal((0, "A"), (2, "C"));
        }

        [Fact]
        public async Task A_path_outside_the_screenshots_folder_is_never_read()
        {
            _webRoot.File("secret.txt", "secret");
            await AddTrades(new SRS { Id = 60, ScreenshotsUrls = ["Screenshots/../secret.txt", "../secret.txt"] });

            var result = await Import();

            result.Imported.Should().Be(0);
            result.MissingPaths.Should().HaveCount(2);
            (await Saved()).Should().BeEmpty();
        }

        [Fact]
        public async Task Nothing_on_disk_and_nothing_in_the_old_paths_changes()
        {
            _webRoot.File($"{Folder}/Trade 1/a.png", "A");
            await AddTrades(new SRS { Id = 70, ScreenshotsUrls = [$"{Folder}/Trade 1/a.png"] });

            await Import();

            _webRoot.Files().Should().Equal($"{Folder}/Trade 1/a.png");
            File.ReadAllText(_webRoot[$"{Folder}/Trade 1/a.png"]).Should().Be("A");
            await using var db = NewDb();
            (await db.Set<BaseTrade>().AsNoTracking().SingleAsync()).ScreenshotsUrls.Should().Equal($"{Folder}/Trade 1/a.png");
        }

        [Fact]
        public async Task It_runs_once_and_records_what_it_did()
        {
            _webRoot.File($"{Folder}/Trade 1/a.png", "A");
            await AddTrades(new SRS { Id = 80, ScreenshotsUrls = [$"{Folder}/Trade 1/a.png", $"{Folder}/Trade 1/gone.png"] });

            await Import();
            var second = await Import();

            second.Ran.Should().BeFalse();
            (await Saved()).Should().ContainSingle();
            await using var db = NewDb();
            (await db.ScreenshotImports.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(
                new { ImportedAt = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc), Imported = 1, Missing = 1 });
        }

        [Fact]
        public async Task A_deleted_screenshot_never_comes_back()
        {
            _webRoot.File($"{Folder}/Trade 1/a.png", "A");
            await AddTrades(new SRS { Id = 90, ScreenshotsUrls = [$"{Folder}/Trade 1/a.png"] });
            await Import();
            await using (var db = NewDb())
            {
                db.Screenshots.RemoveRange(db.Screenshots);
                await db.SaveChangesAsync();
            }

            await Import();

            (await Saved()).Should().BeEmpty();
        }

        [Fact]
        public async Task Screenshots_without_a_record_of_the_import_stop_it_so_nothing_is_added_twice()
        {
            _webRoot.File($"{Folder}/Trade 1/a.png", "A");
            await AddTrades(new SRS { Id = 100, ScreenshotsUrls = [$"{Folder}/Trade 1/a.png"], Screenshots = [new Screenshot { Position = 0, FileName = "new.png", Content = new ScreenshotContent { Data = [1] } }] });

            var act = () => Import();

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await Saved()).Select(s => s.FileName).Should().Equal("new.png");
        }

        [Fact]
        public async Task Trades_without_old_paths_are_fine()
        {
            await AddTrades(new SRS { Id = 110, ScreenshotsUrls = null }, new SRS { Id = 111, ScreenshotsUrls = [] });

            var result = await Import();

            result.Should().BeEquivalentTo(new { Ran = true, Imported = 0, MissingPaths = Array.Empty<string>() });
        }
    }
}
