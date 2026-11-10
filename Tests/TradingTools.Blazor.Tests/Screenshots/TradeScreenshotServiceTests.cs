using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Screenshots;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    /// <summary>Deleting one screenshot of a trade of the journal deletes exactly that one - and nothing else.</summary>
    public class TradeScreenshotServiceTests
    {
        private readonly DbContextOptions<ApplicationDbContext> _options =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"shots-{Guid.NewGuid()}").Options;

        private ApplicationDbContext NewDb() => new(_options);

        /// <summary>A trade with screenshots of these names at positions 0, 1, ...; returns their ids.</summary>
        private async Task<List<int>> AddTrade(int id, params string[] names)
        {
            await using var db = NewDb();
            if (!await db.SampleSizes.AnyAsync(s => s.Id == 1))
                db.SampleSizes.Add(new SampleSize { Id = 1, SampleSizeType = SampleSizeType.DemoTrading, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15 });
            var trade = new SRS
            {
                Id = id,
                SampleSizeId = 1,
                Screenshots = [.. names.Select((name, i) => new Screenshot { Position = i, FileName = name, Content = new ScreenshotContent { Data = [1, 2, 3] } })]
            };
            db.Add(trade);
            await db.SaveChangesAsync();
            return [.. trade.Screenshots.Select(s => s.Id)];
        }

        private async Task<List<(int? TradeId, int Position, string FileName)>> Saved()
        {
            await using var db = NewDb();
            return [.. (await db.Screenshots.AsNoTracking().ToListAsync())
                .OrderBy(s => s.BaseTradeId).ThenBy(s => s.Position).Select(s => (s.BaseTradeId, s.Position, s.FileName))];
        }

        [Fact]
        public async Task Deletes_that_screenshot_only_and_the_others_keep_their_positions()
        {
            var ids = await AddTrade(5, "a.png", "b.png", "c.png");
            await AddTrade(6, "other.png");

            await using (var db = NewDb())
                await new TradeScreenshotService(db).DeleteAsync(5, ids[1]);

            (await Saved()).Should().Equal((5, 0, "a.png"), (5, 2, "c.png"), (6, 0, "other.png"));
        }

        [Fact]
        public async Task A_screenshot_of_another_trade_is_refused_and_nothing_changes()
        {
            await AddTrade(5, "a.png");
            var otherIds = await AddTrade(6, "other.png");

            await using var db = NewDb();
            var act = () => new TradeScreenshotService(db).DeleteAsync(5, otherIds[0]);

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await Saved()).Should().HaveCount(2);
        }

        [Fact]
        public async Task A_screenshot_that_is_already_gone_is_refused()
        {
            var ids = await AddTrade(5, "a.png");
            await using (var db = NewDb())
                await new TradeScreenshotService(db).DeleteAsync(5, ids[0]);

            await using var again = NewDb();
            var act = () => new TradeScreenshotService(again).DeleteAsync(5, ids[0]);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
