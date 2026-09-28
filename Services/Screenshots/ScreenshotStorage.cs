using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Models.ViewModels;
using Shared;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// Where screenshots live on disk and how new trades' files are stored.
    ///
    /// There is a single root, wwwroot/Screenshots, in every environment. The old "ScreenshotsDev"
    /// root (used when the app only ran locally) is merged into it at startup by
    /// <see cref="LegacyScreenshotMigration"/>. Paths stored in the database are relative to wwwroot,
    /// with forward slashes, e.g. "Screenshots/Demo Trading/SRS/15M/Sample Size 3/Trade 7/chart.png".
    /// </summary>
    public static partial class ScreenshotStorage
    {
        public const string RootFolderName = "Screenshots";
        public const string LegacyRootFolderName = "ScreenshotsDev";

        public static string GetRoot(string webRootPath) => Path.Combine(webRootPath, RootFolderName);

        /// <summary>
        /// Saves a new trade's screenshots under
        /// Screenshots/{type}/{strategy class}/{timeframe}/Sample Size n/Trade m/ and returns their
        /// database paths.
        /// </summary>
        /// <param name="newTrade">The trade or research entity; its class name is the strategy folder (SRS, Espresso, ResearchCradle, ...).</param>
        /// <param name="isSampleSizeFull">True when the trade starts a new sample size.</param>
        public static async Task<List<string>> SaveFilesAsync(string webRootPath, NewTradeVM viewModel, object newTrade, IFormFile[] files, bool isSampleSizeFull)
        {
            string strategyFolder = Path.Combine(
                GetRoot(webRootPath),
                MyEnumConverter.TradeTypeFromEnum(viewModel.SampleSizeViewData.SampleSizeType),
                newTrade.GetType().Name,
                MyEnumConverter.TimeFrameFromEnum(viewModel.SampleSizeViewData.TimeFrame));
            Directory.CreateDirectory(strategyFolder);

            string tradeFolder = NextTradeFolder(strategyFolder, isSampleSizeFull);
            return await SaveFilesToFolderAsync(webRootPath, tradeFolder, files);
        }

        /// <summary>
        /// The folder for the next trade: a new "Trade n" in the latest sample size, or "Sample Size n+1/Trade 1"
        /// when that sample size is full. Sample size folders are ordered by their number, not their name
        /// ("Sample Size 10" sorts before "Sample Size 9" as text).
        /// </summary>
        internal static string NextTradeFolder(string strategyFolder, bool isSampleSizeFull)
        {
            var sampleSizeFolders = NumberedFolders(strategyFolder, "Sample Size");

            string sampleSizeFolder;
            if (sampleSizeFolders.Count == 0)
                sampleSizeFolder = Path.Combine(strategyFolder, "Sample Size 1");
            else if (isSampleSizeFull)
                sampleSizeFolder = Path.Combine(strategyFolder, $"Sample Size {sampleSizeFolders[^1].Number + 1}");
            else
                sampleSizeFolder = sampleSizeFolders[^1].Path;

            var tradeFolders = Directory.Exists(sampleSizeFolder) ? NumberedFolders(sampleSizeFolder, "Trade") : [];
            int nextTrade = tradeFolders.Count == 0 ? 1 : tradeFolders[^1].Number + 1;

            string tradeFolder = Path.Combine(sampleSizeFolder, $"Trade {nextTrade}");
            Directory.CreateDirectory(tradeFolder);
            return tradeFolder;
        }

        /// <summary>Writes the files into <paramref name="folder"/> and returns their database paths (relative to wwwroot, forward slashes).</summary>
        public static async Task<List<string>> SaveFilesToFolderAsync(string webRootPath, string folder, IFormFile[] files)
        {
            Directory.CreateDirectory(folder);
            var paths = new List<string>();

            foreach (var file in files)
            {
                // The browser-supplied name can contain '/' or '\' whatever the server's OS; keep only
                // the file name so nothing is written outside the folder.
                string filePath = Path.Combine(folder, Path.GetFileName(file.FileName));

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                paths.Add(ToDatabasePath(webRootPath, filePath));
            }

            return paths;
        }

        /// <summary>A file path under wwwroot as stored in the database: relative, forward slashes.</summary>
        public static string ToDatabasePath(string webRootPath, string filePath) =>
            Path.GetRelativePath(webRootPath, filePath).Replace('\\', '/');

        private static List<(int Number, string Path)> NumberedFolders(string parent, string prefix) =>
            [.. Directory.GetDirectories(parent)
                .Select(path => (Match: NumberedFolderRegex().Match(System.IO.Path.GetFileName(path)), Path: path))
                .Where(x => x.Match.Success && string.Equals(x.Match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase))
                .Select(x => (int.Parse(x.Match.Groups["number"].Value), x.Path))
                .OrderBy(x => x.Item1)];

        [GeneratedRegex(@"^(?<prefix>.+?) (?<number>\d+)$")]
        private static partial Regex NumberedFolderRegex();
    }
}
