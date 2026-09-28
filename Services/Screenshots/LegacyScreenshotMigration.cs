using System.Text.RegularExpressions;
using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models.Trades;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// Merges the old wwwroot/ScreenshotsDev folder into wwwroot/Screenshots and rewrites the
    /// "ScreenshotsDev/..." paths in the database to "Screenshots/...". Safe to run on every startup
    /// (and after a database import, which may bring old paths back): once nothing is left to move
    /// or rewrite it does nothing. A file is never overwritten - if both folders hold a different
    /// file under the same name, the old one is left where it is and logged.
    /// </summary>
    public sealed partial class LegacyScreenshotMigration(ApplicationDbContext db, IWebHostEnvironment environment, ILogger<LegacyScreenshotMigration> logger)
    {
        public sealed record FolderMergeResult(int Moved, int DuplicatesRemoved, IReadOnlyList<string> Conflicts);

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            string legacyRoot = Path.Combine(environment.WebRootPath, ScreenshotStorage.LegacyRootFolderName);
            var merge = MergeFolder(legacyRoot, ScreenshotStorage.GetRoot(environment.WebRootPath));
            if (merge.Moved > 0 || merge.DuplicatesRemoved > 0)
                logger.LogInformation("Moved {Moved} screenshot(s) from ScreenshotsDev to Screenshots ({Duplicates} duplicate(s) removed).", merge.Moved, merge.DuplicatesRemoved);
            foreach (var conflict in merge.Conflicts)
                logger.LogWarning("Screenshot not moved, a different file already exists in Screenshots: {File}", conflict);

            int rewritten = await RewriteDatabasePathsAsync(cancellationToken);
            if (rewritten > 0)
                logger.LogInformation("Rewrote ScreenshotsDev paths to Screenshots for {Count} trade(s).", rewritten);
        }

        /// <summary>Moves every file of <paramref name="source"/> into <paramref name="target"/>, keeping the folder structure.</summary>
        public static FolderMergeResult MergeFolder(string source, string target)
        {
            if (!Directory.Exists(source)) return new(0, 0, []);

            if (!Directory.Exists(target))
            {
                // Plain rename: instant, whatever the folder size.
                int count = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Count();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
                Directory.Move(source, target);
                return new(count, 0, []);
            }

            int moved = 0, duplicates = 0;
            var conflicts = new List<string>();

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList())
            {
                string destination = Path.Combine(target, Path.GetRelativePath(source, file));

                if (!File.Exists(destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(file, destination);
                    moved++;
                }
                else if (SameContent(file, destination))
                {
                    File.Delete(file);
                    duplicates++;
                }
                else
                {
                    conflicts.Add(Path.GetRelativePath(source, file));
                }
            }

            DeleteEmptyFolders(source);
            return new(moved, duplicates, conflicts);
        }

        /// <summary>
        /// "ScreenshotsDev/x" -> "Screenshots/x" (also the old "/ScreenshotsDev\x" research form); any other
        /// path is returned unchanged.
        /// </summary>
        public static string RewriteLegacyPath(string path) =>
            LegacyRootRegex().Replace(path, "${slash}" + ScreenshotStorage.RootFolderName);

        private async Task<int> RewriteDatabasePathsAsync(CancellationToken cancellationToken)
        {
            // Cheap check first so a normal startup doesn't load every trade.
            bool anyLegacy = await db.Database
                .SqlQueryRaw<bool>("""SELECT EXISTS (SELECT 1 FROM "BaseTrades" WHERE "ScreenshotsUrls"::text LIKE '%ScreenshotsDev%') AS "Value" """)
                .SingleAsync(cancellationToken);
            if (!anyLegacy) return 0;

            var trades = await db.Set<BaseTrade>().Where(t => t.ScreenshotsUrls != null).ToListAsync(cancellationToken);
            int changed = 0;
            foreach (var trade in trades)
            {
                var rewritten = trade.ScreenshotsUrls!.Select(RewriteLegacyPath).ToList();
                if (rewritten.SequenceEqual(trade.ScreenshotsUrls!)) continue;

                trade.ScreenshotsUrls = rewritten;
                changed++;
            }

            await db.SaveChangesAsync(cancellationToken);
            return changed;
        }

        private static bool SameContent(string a, string b)
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length) return false;

            using var streamA = infoA.OpenRead();
            using var streamB = infoB.OpenRead();
            Span<byte> bufferA = stackalloc byte[8192];
            Span<byte> bufferB = stackalloc byte[8192];
            int read;
            while ((read = streamA.Read(bufferA)) > 0)
            {
                streamB.ReadExactly(bufferB[..read]);
                if (!bufferA[..read].SequenceEqual(bufferB[..read])) return false;
            }
            return true;
        }

        private static void DeleteEmptyFolders(string folder)
        {
            foreach (string child in Directory.GetDirectories(folder))
                DeleteEmptyFolders(child);

            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }

        // Optional leading "/" (old research paths), then ScreenshotsDev followed by a separator.
        [GeneratedRegex(@"^(?<slash>/?)ScreenshotsDev(?=[/\\])")]
        private static partial Regex LegacyRootRegex();
    }
}
