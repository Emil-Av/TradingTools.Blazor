using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models.Trades;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// The rules for deleting a screenshot file, shared by the Trades page and the Tom section. A deleted file can't be
    /// brought back, so a file is only deleted when it lies inside the Screenshots folder and no path in the database
    /// still points at it - and only that one file, never a folder.
    /// </summary>
    public static class ScreenshotFiles
    {
        /// <summary>A path as a comparable key: forward slashes, no leading slash (older research paths have both forms).</summary>
        public static string Key(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

        /// <summary>Whether two database paths point at the same file. Case-insensitive on purpose: when in doubt the file is kept.</summary>
        public static bool SameFile(string path, string key) =>
            string.Equals(Key(path), key, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The full path of the file a database path points at, or null when it doesn't lead to a file strictly
        /// inside the Screenshots folder (an empty path, a rooted one, one with "." or ".." in it).
        /// </summary>
        public static string? ResolveFile(string webRootPath, string path)
        {
            string relative = Key(path);
            if (relative.Length == 0 || relative.Split('/').Any(part => part is "." or "..")) return null;

            char separator = Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(ScreenshotStorage.GetRoot(webRootPath)).TrimEnd(separator, Path.AltDirectorySeparatorChar) + separator;
            string full = Path.GetFullPath(Path.Combine(webRootPath, relative.Replace('/', separator)));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(root, comparison) && full.Length > root.Length ? full : null;
        }

        /// <summary>
        /// Whether any trade - a trade of the journal or a Tom trade - other than the given one still has a path to
        /// the file. The database is read as it is saved, so call this before removing the path.
        /// </summary>
        public static async Task<bool> IsUsedElsewhereAsync(
            ApplicationDbContext db, string path, int? exceptTradeId = null, int? exceptTomTradeId = null, CancellationToken cancellationToken = default) =>
            await IsUsedElsewhereAsync(db, path, exceptTradeId, exceptTomTradeId is { } id ? [id] : [], cancellationToken);

        /// <summary>The same, leaving out several Tom trades (all the trades of a stream that is being deleted).</summary>
        public static async Task<bool> IsUsedElsewhereAsync(
            ApplicationDbContext db, string path, int? exceptTradeId, IReadOnlyCollection<int> exceptTomTradeIds, CancellationToken cancellationToken = default)
        {
            string key = Key(path);

            var trades = await db.Set<BaseTrade>().AsNoTracking()
                .Where(t => t.Id != exceptTradeId && t.ScreenshotsUrls != null)
                .Select(t => t.ScreenshotsUrls!)
                .ToListAsync(cancellationToken);
            if (trades.Any(list => list.Any(url => SameFile(url, key)))) return true;

            var tomTrades = await db.TomTrades.AsNoTracking()
                .Where(t => !exceptTomTradeIds.Contains(t.Id) && t.ScreenshotsUrls != null)
                .Select(t => t.ScreenshotsUrls!)
                .ToListAsync(cancellationToken);
            return tomTrades.Any(list => list.Any(url => SameFile(url, key)));
        }

        /// <summary>Deletes the one file at <paramref name="file"/> (from <see cref="ResolveFile"/>) unless it is shared.</summary>
        public static ScreenshotFileOutcome Delete(string? file, bool shared, ILogger logger)
        {
            if (file is null) return ScreenshotFileOutcome.NotInScreenshotsFolder;
            if (shared) return ScreenshotFileOutcome.KeptBecauseShared;
            if (!File.Exists(file)) return ScreenshotFileOutcome.WasMissing;

            try
            {
                File.Delete(file);
                return ScreenshotFileOutcome.Deleted;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "The screenshot's path was removed but its file could not be deleted: {File}", file);
                return ScreenshotFileOutcome.Failed;
            }
        }
    }
}
