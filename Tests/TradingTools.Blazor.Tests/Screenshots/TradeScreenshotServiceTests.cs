using DataAccess.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Models.Trades;
using Shared.Enums;
using NSubstitute;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    /// <summary>
    /// Deleting one screenshot of a trade removes exactly that path from the database and exactly that file from the
    /// Screenshots folder - and nothing else, whatever goes wrong.
    /// </summary>
    public class TradeScreenshotServiceTests : IDisposable
    {
        private const string Folder = "Screenshots/Demo Trading/SRS/15M/Sample Size 1";

        private readonly TempFolder _webRoot = new();
        private readonly DbContextOptions<ApplicationDbContext> _options =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"shots-{Guid.NewGuid()}").Options;

        public void Dispose() => _webRoot.Dispose();

        private ApplicationDbContext NewDb() => new(_options);

        private TradeScreenshotService Service(ApplicationDbContext db)
        {
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.WebRootPath.Returns(_webRoot.Path);
            return new TradeScreenshotService(db, environment, NullLogger<TradeScreenshotService>.Instance);
        }

        private static SampleSize Sample() =>
            new() { Id = 1, SampleSizeType = SampleSizeType.DemoTrading, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15 };

        /// <summary>A trade whose screenshots are the given database paths; creates their files unless they already exist.</summary>
        private async Task<int> AddTrade(int id, params string[] paths)
        {
            await using (var db = NewDb())
            {
                if (!await db.SampleSizes.AnyAsync(s => s.Id == 1)) db.SampleSizes.Add(Sample());
                db.Add(new SRS { Id = id, SampleSizeId = 1, ScreenshotsUrls = [.. paths] });
                await db.SaveChangesAsync();
            }

            foreach (var path in paths)
            {
                string relative = path.Replace('\\', '/').TrimStart('/');
                if (!File.Exists(_webRoot[relative])) _webRoot.File(relative, content: path);
            }
            return id;
        }

        private async Task<List<string>> UrlsOf(int id)
        {
            await using var db = NewDb();
            return (await db.Set<BaseTrade>().AsNoTracking().SingleAsync(t => t.Id == id)).ScreenshotsUrls ?? [];
        }

        private async Task<ScreenshotDeleteResult> Delete(int id, string path)
        {
            await using var db = NewDb();
            return await Service(db).DeleteAsync(id, path);
        }

        private static string P(string name, int trade = 1) => $"{Folder}/Trade {trade}/{name}";

        #region The right screenshot goes

        [Fact]
        public async Task Deleting_one_screenshot_removes_its_path_and_its_file_and_nothing_else()
        {
            int id = await AddTrade(1, P("a.png"), P("b.png"), P("c.png"));

            var result = await Delete(id, P("b.png"));

            result.File.Should().Be(ScreenshotFileOutcome.Deleted);
            result.Urls.Should().Equal(P("a.png"), P("c.png"));
            (await UrlsOf(id)).Should().Equal(P("a.png"), P("c.png"));
            _webRoot.Files("Screenshots").Should().Equal(
                "Demo Trading/SRS/15M/Sample Size 1/Trade 1/a.png",
                "Demo Trading/SRS/15M/Sample Size 1/Trade 1/c.png");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Whichever_position_it_is_in_the_chosen_one_goes(int position)
        {
            string[] paths = [P("a.png"), P("b.png"), P("c.png")];
            int id = await AddTrade(1, paths);

            await Delete(id, paths[position]);

            (await UrlsOf(id)).Should().Equal(paths.Where((_, i) => i != position));
            File.Exists(_webRoot[paths[position]]).Should().BeFalse();
            paths.Where((_, i) => i != position).Should().OnlyContain(p => File.Exists(_webRoot[p]));
        }

        [Fact]
        public async Task The_screenshot_is_found_by_its_path_so_a_similar_name_in_another_folder_is_safe()
        {
            int one = await AddTrade(1, P("chart.png", 1));
            int two = await AddTrade(2, P("chart.png", 2));

            await Delete(one, P("chart.png", 1));

            File.Exists(_webRoot[P("chart.png", 1)]).Should().BeFalse();
            File.Exists(_webRoot[P("chart.png", 2)]).Should().BeTrue();
            (await UrlsOf(two)).Should().Equal(P("chart.png", 2));
        }

        [Fact]
        public async Task Other_trades_are_not_changed()
        {
            int one = await AddTrade(1, P("a.png", 1), P("b.png", 1));
            int two = await AddTrade(2, P("a.png", 2), P("b.png", 2));

            await Delete(one, P("a.png", 1));

            (await UrlsOf(two)).Should().Equal(P("a.png", 2), P("b.png", 2));
            _webRoot.Files("Screenshots").Should().HaveCount(3);
        }

        [Fact]
        public async Task The_last_screenshot_can_be_deleted_and_leaves_an_empty_list_and_its_folder()
        {
            int id = await AddTrade(1, P("only.png"));

            var result = await Delete(id, P("only.png"));

            result.Urls.Should().BeEmpty();
            (await UrlsOf(id)).Should().BeEmpty();
            Directory.Exists(_webRoot[$"{Folder}/Trade 1"]).Should().BeTrue("no folder is ever removed");
        }

        [Fact]
        public async Task Only_the_one_file_is_deleted_not_its_siblings_or_folders()
        {
            int id = await AddTrade(1, P("a.png"), P("b.png"));
            _webRoot.File($"{Folder}/Trade 1/not-in-database.png");
            _webRoot.File($"{Folder}/Trade 2/other.png");

            await Delete(id, P("a.png"));

            _webRoot.Files("Screenshots").Should().Equal(
                "Demo Trading/SRS/15M/Sample Size 1/Trade 1/b.png",
                "Demo Trading/SRS/15M/Sample Size 1/Trade 1/not-in-database.png",
                "Demo Trading/SRS/15M/Sample Size 1/Trade 2/other.png");
        }

        [Fact]
        public async Task A_file_name_with_spaces_and_special_characters_is_found()
        {
            string path = P("Screenshot from 2024-04-21 13-50-00 (1) #2.png");
            int id = await AddTrade(1, path, P("keep.png"));

            await Delete(id, path);

            File.Exists(_webRoot[path]).Should().BeFalse();
            (await UrlsOf(id)).Should().Equal(P("keep.png"));
        }

        [Fact]
        public async Task The_old_research_form_of_a_path_with_backslashes_and_a_leading_slash_is_found()
        {
            string stored = "/Screenshots/Research/ResearchFirstBarPullback/Sample Size 1/Trades/Trade 1\\Screenshot a.png";
            await AddTrade(1, stored);

            // The file lives at the forward-slash form of the same path.
            File.Exists(_webRoot["Screenshots/Research/ResearchFirstBarPullback/Sample Size 1/Trades/Trade 1/Screenshot a.png"]).Should().BeTrue();

            var result = await Delete(1, stored);

            result.File.Should().Be(ScreenshotFileOutcome.Deleted);
            _webRoot.Files("Screenshots").Should().BeEmpty();
        }

        #endregion

        #region Things that must not be touched

        [Fact]
        public async Task A_path_the_trade_does_not_have_changes_nothing()
        {
            int id = await AddTrade(1, P("a.png"));
            _webRoot.File(P("stranger.png"));

            var act = () => Delete(id, P("stranger.png"));

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await UrlsOf(id)).Should().Equal(P("a.png"));
            _webRoot.Files("Screenshots").Should().HaveCount(2);
        }

        [Fact]
        public async Task A_screenshot_of_another_trade_cannot_be_deleted_through_this_one()
        {
            int one = await AddTrade(1, P("a.png", 1));
            int two = await AddTrade(2, P("b.png", 2));

            var act = () => Delete(one, P("b.png", 2));

            await act.Should().ThrowAsync<InvalidOperationException>();
            File.Exists(_webRoot[P("b.png", 2)]).Should().BeTrue();
            (await UrlsOf(two)).Should().Equal(P("b.png", 2));
            (await UrlsOf(one)).Should().Equal(P("a.png", 1));
        }

        [Fact]
        public async Task The_path_must_match_exactly_so_a_different_case_or_prefix_deletes_nothing()
        {
            int id = await AddTrade(1, P("a.png"));

            foreach (var near in new[] { P("A.png"), P("a.PNG"), P("a.png") + " ", "/" + P("a.png"), P("a.pn") })
            {
                var act = () => Delete(id, near);
                await act.Should().ThrowAsync<InvalidOperationException>(near);
            }

            File.Exists(_webRoot[P("a.png")]).Should().BeTrue();
            (await UrlsOf(id)).Should().Equal(P("a.png"));
        }

        [Fact]
        public async Task An_unknown_trade_changes_nothing()
        {
            await AddTrade(1, P("a.png"));

            var act = () => Delete(999, P("a.png"));

            await act.Should().ThrowAsync<ArgumentException>();
            File.Exists(_webRoot[P("a.png")]).Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task An_empty_path_is_refused(string path)
        {
            await AddTrade(1, P("a.png"));

            var act = () => Delete(1, path);

            await act.Should().ThrowAsync<ArgumentException>();
            (await UrlsOf(1)).Should().Equal(P("a.png"));
        }

        [Fact]
        public async Task A_trade_without_screenshots_has_nothing_to_delete()
        {
            await AddTrade(1);

            var act = () => Delete(1, P("a.png"));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Deleting_the_same_screenshot_twice_is_refused_the_second_time_and_loses_nothing_more()
        {
            int id = await AddTrade(1, P("a.png"), P("b.png"));
            await Delete(id, P("a.png"));

            var act = () => Delete(id, P("a.png"));

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await UrlsOf(id)).Should().Equal(P("b.png"));
            File.Exists(_webRoot[P("b.png")]).Should().BeTrue();
        }

        #endregion

        #region A file that is still in use somewhere

        [Fact]
        public async Task A_file_another_trade_also_points_at_is_kept()
        {
            int one = await AddTrade(1, P("shared.png"));
            int two = await AddTrade(2, P("shared.png"));

            var result = await Delete(one, P("shared.png"));

            result.File.Should().Be(ScreenshotFileOutcome.KeptBecauseShared);
            (await UrlsOf(one)).Should().BeEmpty("its path is removed");
            (await UrlsOf(two)).Should().Equal(P("shared.png"));
            File.Exists(_webRoot[P("shared.png")]).Should().BeTrue();
        }

        [Fact]
        public async Task A_file_the_same_trade_lists_twice_is_kept_until_the_last_path_goes()
        {
            int id = await AddTrade(1, P("dup.png"), P("dup.png"));

            var first = await Delete(id, P("dup.png"));
            first.File.Should().Be(ScreenshotFileOutcome.KeptBecauseShared);
            first.Urls.Should().Equal(P("dup.png"));
            File.Exists(_webRoot[P("dup.png")]).Should().BeTrue();

            var second = await Delete(id, P("dup.png"));
            second.File.Should().Be(ScreenshotFileOutcome.Deleted);
            File.Exists(_webRoot[P("dup.png")]).Should().BeFalse();
        }

        [Fact]
        public async Task A_file_another_trade_points_at_in_a_different_spelling_is_kept()
        {
            int one = await AddTrade(1, P("x.png"));
            await AddTrade(2, "/" + P("x.png").Replace("/", "\\"));

            var result = await Delete(one, P("x.png"));

            result.File.Should().Be(ScreenshotFileOutcome.KeptBecauseShared);
            File.Exists(_webRoot[P("x.png")]).Should().BeTrue();
        }

        #endregion

        #region Odd data

        [Fact]
        public async Task A_path_whose_file_is_already_gone_is_just_removed_from_the_trade()
        {
            int id = await AddTrade(1, P("gone.png"), P("here.png"));
            File.Delete(_webRoot[P("gone.png")]);

            var result = await Delete(id, P("gone.png"));

            result.File.Should().Be(ScreenshotFileOutcome.WasMissing);
            (await UrlsOf(id)).Should().Equal(P("here.png"));
            File.Exists(_webRoot[P("here.png")]).Should().BeTrue();
        }

        [Theory]
        [InlineData("Screenshots/../outside.txt")]
        [InlineData("Screenshots/Demo Trading/../../outside.txt")]
        [InlineData("../outside.txt")]
        [InlineData("outside.txt")]
        [InlineData("Other/outside.txt")]
        [InlineData("Screenshots/./outside.txt")]
        [InlineData("Screenshots")]
        [InlineData("Screenshots/")]
        public async Task A_path_that_does_not_lead_inside_the_screenshots_folder_never_deletes_a_file(string stored)
        {
            string outside = _webRoot.File("outside.txt", "precious");
            await using (var db = NewDb())
            {
                db.SampleSizes.Add(Sample());
                db.Add(new SRS { Id = 1, SampleSizeId = 1, ScreenshotsUrls = [stored, P("keep.png")] });
                await db.SaveChangesAsync();
            }
            _webRoot.File(P("keep.png"));

            var result = await Delete(1, stored);

            result.File.Should().Be(ScreenshotFileOutcome.NotInScreenshotsFolder);
            File.ReadAllText(outside).Should().Be("precious");
            Directory.Exists(_webRoot["Screenshots"]).Should().BeTrue();
            File.Exists(_webRoot[P("keep.png")]).Should().BeTrue();
            (await UrlsOf(1)).Should().Equal([P("keep.png")]); // the bad path itself is removed from the trade
        }

        [Fact]
        public async Task An_absolute_path_never_deletes_a_file()
        {
            string outside = _webRoot.File("outside.txt", "precious");
            string absolute = outside.Replace('\\', '/');
            await using (var db = NewDb())
            {
                db.SampleSizes.Add(Sample());
                db.Add(new SRS { Id = 1, SampleSizeId = 1, ScreenshotsUrls = [absolute] });
                await db.SaveChangesAsync();
            }

            var result = await Delete(1, absolute);

            result.File.Should().Be(ScreenshotFileOutcome.NotInScreenshotsFolder);
            File.ReadAllText(outside).Should().Be("precious");
        }

        #endregion

        #region When something goes wrong

        /// <summary>A database that can't save.</summary>
        private sealed class FailingDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
        {
            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("The database is down.");
        }

        [Fact]
        public async Task When_the_database_cannot_save_the_file_is_not_touched()
        {
            int id = await AddTrade(1, P("a.png"), P("b.png"));
            await using var failing = new FailingDb(_options);

            var act = () => Service(failing).DeleteAsync(id, P("a.png"));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The database is down.");
            File.Exists(_webRoot[P("a.png")]).Should().BeTrue("a path must never be left pointing at a deleted file");
            (await UrlsOf(id)).Should().Equal(P("a.png"), P("b.png"));
        }

        [Fact]
        public async Task A_file_that_cannot_be_deleted_is_reported_and_the_path_is_still_removed()
        {
            if (!OperatingSystem.IsWindows()) return; // only Windows refuses to delete an open file

            int id = await AddTrade(1, P("locked.png"), P("b.png"));
            await using var holdOpen = new FileStream(_webRoot[P("locked.png")], FileMode.Open, FileAccess.Read, FileShare.None);

            var result = await Delete(id, P("locked.png"));

            result.File.Should().Be(ScreenshotFileOutcome.Failed);
            (await UrlsOf(id)).Should().Equal(P("b.png"));
            File.Exists(_webRoot[P("locked.png")]).Should().BeTrue();
        }

        #endregion

        #region Resolving a path to a file

        [Theory]
        [InlineData("Screenshots/a.png", "Screenshots/a.png")]
        [InlineData("/Screenshots/a.png", "Screenshots/a.png")]
        [InlineData("Screenshots\\Research\\a.png", "Screenshots/Research/a.png")]
        [InlineData("/Screenshots/Research/Trade 1\\a b.png", "Screenshots/Research/Trade 1/a b.png")]
        [InlineData("  Screenshots/a.png ", "Screenshots/a.png")]
        public void A_database_path_resolves_to_the_file_under_the_webroot(string stored, string expectedRelative)
        {
            TradeScreenshotService.ResolveFile(_webRoot.Path, stored).Should().Be(Path.GetFullPath(_webRoot[expectedRelative]));
        }

        [Theory]
        [InlineData("")]
        [InlineData("/")]
        [InlineData("Screenshots")]
        [InlineData("Screenshots/")]
        [InlineData("Screenshots/../x.png")]
        [InlineData("Screenshots/a/../../x.png")]
        [InlineData("Screenshots/./x.png")]
        [InlineData("..\\x.png")]
        [InlineData("x.png")]
        [InlineData("ScreenshotsDev/x.png")]
        [InlineData("Screenshots2/x.png")]
        public void Paths_that_do_not_lead_to_a_file_inside_the_screenshots_folder_resolve_to_nothing(string stored)
        {
            TradeScreenshotService.ResolveFile(_webRoot.Path, stored).Should().BeNull();
        }

        #endregion
    }
}
