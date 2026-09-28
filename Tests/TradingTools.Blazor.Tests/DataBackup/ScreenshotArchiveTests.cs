using System.IO.Compression;
using TradingTools.Blazor.Services.DataBackup;

namespace TradingTools.Blazor.Tests.DataBackup
{
    public class ScreenshotArchiveTests : IDisposable
    {
        private readonly TempFolder _temp = new();

        public void Dispose() => _temp.Dispose();

        private MemoryStream Zip(params (string Name, string Content)[] entries)
        {
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write(content);
                }
            }
            stream.Position = 0;
            return stream;
        }

        #region Entry paths

        [Theory]
        [InlineData("Demo Trading/SRS/15M/Sample Size 1/Trade 1/a.png", "Demo Trading/SRS/15M/Sample Size 1/Trade 1/a.png")]
        [InlineData("Screenshots/Demo Trading/a.png", "Demo Trading/a.png")]
        [InlineData("ScreenshotsDev/Demo Trading/a.png", "Demo Trading/a.png")]
        [InlineData("wwwroot/Screenshots/Demo Trading/a.png", "Demo Trading/a.png")]
        [InlineData(@"Screenshots\Demo Trading\a.png", "Demo Trading/a.png")]      // zips made on Windows
        [InlineData("/Screenshots/./Demo Trading/a.png", "Demo Trading/a.png")]
        [InlineData("a.png", "a.png")]
        [InlineData("Screenshots", "Screenshots")]                                 // a file literally named like the root is kept
        public void Entry_paths_are_made_relative_to_the_screenshots_folder(string entry, string expected)
        {
            ScreenshotArchive.NormalizeEntryPath(entry).Should().Be(expected);
        }

        [Theory]
        [InlineData("../outside.png")]
        [InlineData("Screenshots/../../outside.png")]
        [InlineData(@"..\outside.png")]
        [InlineData("__MACOSX/Demo Trading/._a.png")]
        [InlineData("")]
        [InlineData("/")]
        public void Unsafe_or_empty_entries_are_rejected(string entry)
        {
            ScreenshotArchive.NormalizeEntryPath(entry).Should().BeNull();
        }

        #endregion

        #region Import

        [Fact]
        public void Import_writes_files_under_the_screenshots_folder()
        {
            var result = ScreenshotArchive.ImportZip(
                Zip(("Screenshots/Demo Trading/SRS/a.png", "A"), ("Paper Trade/b.png", "B")),
                _temp["wwwroot/Screenshots"]);

            result.Should().Be(new ScreenshotArchive.ImportResult(FilesWritten: 2, EntriesSkipped: 0));
            _temp.Files("wwwroot/Screenshots").Should().Equal("Demo Trading/SRS/a.png", "Paper Trade/b.png");
            File.ReadAllText(_temp["wwwroot/Screenshots/Demo Trading/SRS/a.png"]).Should().Be("A");
        }

        [Fact]
        public void Import_overwrites_a_file_with_the_same_path_and_keeps_the_others()
        {
            _temp.File("wwwroot/Screenshots/a.png", "old");
            _temp.File("wwwroot/Screenshots/untouched.png", "keep");

            ScreenshotArchive.ImportZip(Zip(("a.png", "new")), _temp["wwwroot/Screenshots"]);

            File.ReadAllText(_temp["wwwroot/Screenshots/a.png"]).Should().Be("new");
            File.ReadAllText(_temp["wwwroot/Screenshots/untouched.png"]).Should().Be("keep");
        }

        [Fact]
        public void Zip_slip_entries_are_skipped_and_nothing_is_written_outside()
        {
            var result = ScreenshotArchive.ImportZip(
                Zip(("../escaped.png", "evil"), ("Screenshots/../../escaped2.png", "evil"), ("ok.png", "fine")),
                _temp["wwwroot/Screenshots"]);

            result.Should().Be(new ScreenshotArchive.ImportResult(1, 2));
            _temp.Files().Should().Equal("wwwroot/Screenshots/ok.png");
        }

        /// <summary>
        /// An entry holding an absolute path has no "..", so it passes the entry-name check. On Windows
        /// ("C:/...") only the check that the final path stays inside the screenshots folder stops it.
        /// </summary>
        [Fact]
        public void An_entry_with_an_absolute_path_is_never_written_outside_the_screenshots_folder()
        {
            string outside = _temp["outside/evil.png"];

            ScreenshotArchive.ImportZip(Zip((outside.Replace('\\', '/'), "evil")), _temp["wwwroot/Screenshots"]);

            File.Exists(outside).Should().BeFalse();
            Directory.Exists(_temp["outside"]).Should().BeFalse();
        }

        [Fact]
        public void Folder_entries_are_not_counted_as_files()
        {
            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                archive.CreateEntry("Demo Trading/");
                using var writer = new StreamWriter(archive.CreateEntry("Demo Trading/a.png").Open());
                writer.Write("A");
            }
            zip.Position = 0;

            ScreenshotArchive.ImportZip(zip, _temp["wwwroot/Screenshots"]).Should().Be(new ScreenshotArchive.ImportResult(1, 0));
        }

        [Fact]
        public void A_file_that_is_not_a_zip_is_refused()
        {
            var notAZip = new MemoryStream("PGDMP this is a database dump"u8.ToArray());

            var import = () => ScreenshotArchive.ImportZip(notAZip, _temp["wwwroot/Screenshots"]);

            import.Should().Throw<InvalidDataException>();
        }

        #endregion

        #region Export + import round trip

        [Fact]
        public void Export_then_import_restores_every_file_exactly()
        {
            _temp.File("source/Screenshots/Demo Trading/SRS/15M/Sample Size 10/Trade 1/chart one.png", "1");
            _temp.File("source/Screenshots/Paper Trade/SRS/5M/Sample Size 1/Trade 2/b.png", "2");
            _temp.File("source/Screenshots/Research/ResearchCradle/15M/Sample Size 1/Trade 1/c.png", "3");
            string zipPath = _temp["export.zip"];

            ScreenshotArchive.CreateZip(_temp["source/Screenshots"], zipPath);
            using (var zip = File.OpenRead(zipPath))
                ScreenshotArchive.ImportZip(zip, _temp["target/Screenshots"]);

            _temp.Files("target/Screenshots").Should().Equal(_temp.Files("source/Screenshots"));
            foreach (string file in _temp.Files("source/Screenshots"))
                File.ReadAllText(_temp["target/Screenshots/" + file]).Should().Be(File.ReadAllText(_temp["source/Screenshots/" + file]));
        }

        [Fact]
        public void Exported_entries_use_forward_slashes_relative_to_the_screenshots_folder()
        {
            _temp.File("source/Screenshots/Demo Trading/SRS/a.png");
            string zipPath = _temp["export.zip"];

            ScreenshotArchive.CreateZip(_temp["source/Screenshots"], zipPath);

            using var archive = ZipFile.OpenRead(zipPath);
            archive.Entries.Select(e => e.FullName).Should().Contain("Demo Trading/SRS/a.png");
        }

        [Fact]
        public void Exporting_a_missing_folder_gives_an_empty_zip()
        {
            string zipPath = _temp["export.zip"];

            ScreenshotArchive.CreateZip(_temp["does-not-exist"], zipPath);

            using var archive = ZipFile.OpenRead(zipPath);
            archive.Entries.Should().BeEmpty();
        }

        #endregion
    }
}
