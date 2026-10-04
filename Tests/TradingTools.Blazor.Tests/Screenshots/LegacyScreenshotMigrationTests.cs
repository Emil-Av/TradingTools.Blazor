using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Tests.Screenshots
{
    public class LegacyScreenshotMigrationTests : IDisposable
    {
        private readonly TempFolder _root = new();

        public void Dispose() => _root.Dispose();

        #region Database paths

        [Theory]
        [InlineData("ScreenshotsDev/Demo Trading/SRS/15M/Sample Size 9/Trade 3/US500.png", "Screenshots/Demo Trading/SRS/15M/Sample Size 9/Trade 3/US500.png")]
        // Real old research paths: leading slash and a backslash before the file name.
        [InlineData(@"/ScreenshotsDev/Research/ResearchFirstBarPullback/Sample Size 1/Trades/Trade 1\Screenshot.png", @"/Screenshots/Research/ResearchFirstBarPullback/Sample Size 1/Trades/Trade 1\Screenshot.png")]
        [InlineData(@"ScreenshotsDev\Demo Trading\a.png", @"Screenshots\Demo Trading\a.png")]
        public void Old_root_is_rewritten(string path, string expected)
        {
            LegacyScreenshotMigration.RewriteLegacyPath(path).Should().Be(expected);
        }

        [Theory]
        [InlineData("Screenshots/Demo Trading/SRS/a.png")]                  // already migrated
        [InlineData("/Screenshots/Research/a.png")]
        [InlineData("Screenshots/Demo Trading/ScreenshotsDev/a.png")]       // only the root is rewritten
        [InlineData("ScreenshotsDevelopment/a.png")]                        // a different folder
        [InlineData("screenshotsdev/a.png")]                                // folder names are case-sensitive on Linux
        [InlineData("")]
        public void Other_paths_are_left_alone(string path)
        {
            LegacyScreenshotMigration.RewriteLegacyPath(path).Should().Be(path);
        }

        [Fact]
        public void Rewriting_twice_changes_nothing_more()
        {
            string once = LegacyScreenshotMigration.RewriteLegacyPath("ScreenshotsDev/a/b.png");

            LegacyScreenshotMigration.RewriteLegacyPath(once).Should().Be(once);
        }

        #endregion

        #region Folder merge

        [Fact]
        public void When_there_is_no_new_folder_yet_the_old_one_is_renamed()
        {
            _root.File("ScreenshotsDev/Demo Trading/SRS/a.png");
            _root.File("ScreenshotsDev/Research/b.png");

            var result = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            result.Moved.Should().Be(2);
            Directory.Exists(_root["ScreenshotsDev"]).Should().BeFalse();
            _root.Files("Screenshots").Should().Equal("Demo Trading/SRS/a.png", "Research/b.png");
        }

        [Fact]
        public void Files_are_merged_into_an_existing_new_folder()
        {
            _root.File("Screenshots/Demo Trading/SRS/existing.png");
            _root.File("ScreenshotsDev/Demo Trading/SRS/old.png");
            _root.File("ScreenshotsDev/Paper Trade/old.png");

            var result = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            result.Should().BeEquivalentTo(new { Moved = 2, DuplicatesRemoved = 0, Conflicts = Array.Empty<string>() });
            _root.Files("Screenshots").Should().Equal("Demo Trading/SRS/existing.png", "Demo Trading/SRS/old.png", "Paper Trade/old.png");
            Directory.Exists(_root["ScreenshotsDev"]).Should().BeFalse("the emptied old folder is removed");
        }

        [Fact]
        public void An_identical_file_already_in_the_new_folder_is_a_duplicate_and_removed()
        {
            _root.File("Screenshots/a.png", "same");
            _root.File("ScreenshotsDev/a.png", "same");

            var result = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            result.DuplicatesRemoved.Should().Be(1);
            Directory.Exists(_root["ScreenshotsDev"]).Should().BeFalse();
        }

        [Fact]
        public void A_different_file_with_the_same_name_is_never_overwritten()
        {
            _root.File("Screenshots/a.png", "new content");
            _root.File("ScreenshotsDev/a.png", "old content");
            _root.File("ScreenshotsDev/b.png", "other");

            var result = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            result.Conflicts.Should().Equal("a.png");
            File.ReadAllText(_root["Screenshots/a.png"]).Should().Be("new content");
            File.ReadAllText(_root["ScreenshotsDev/a.png"]).Should().Be("old content", "the old file stays where it is");
            File.Exists(_root["Screenshots/b.png"]).Should().BeTrue();
        }

        [Fact]
        public void Same_size_but_different_bytes_is_a_conflict_not_a_duplicate()
        {
            _root.File("Screenshots/a.png", "AAAA");
            _root.File("ScreenshotsDev/a.png", "AAAB");

            var result = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            result.DuplicatesRemoved.Should().Be(0);
            result.Conflicts.Should().Equal("a.png");
        }

        [Fact]
        public void Nothing_to_merge_is_a_no_op()
        {
            _root.File("Screenshots/a.png");

            var result = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            result.Should().BeEquivalentTo(new { Moved = 0, DuplicatesRemoved = 0, Conflicts = Array.Empty<string>() });
            _root.Files("Screenshots").Should().Equal("a.png");
        }

        [Fact]
        public void Running_twice_is_safe()
        {
            _root.File("ScreenshotsDev/a.png");

            LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);
            var second = LegacyScreenshotMigration.MergeFolder(_root["ScreenshotsDev"], _root["Screenshots"]);

            second.Moved.Should().Be(0);
            _root.Files("Screenshots").Should().Equal("a.png");
        }

        #endregion
    }
}
