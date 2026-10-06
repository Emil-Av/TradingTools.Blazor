using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models.Trades;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>What happened to the screenshot's file when its path was removed from the trade.</summary>
    public enum ScreenshotFileOutcome
    {
        /// <summary>The file was deleted from the Screenshots folder.</summary>
        Deleted,

        /// <summary>There was no file at that path (a broken path); only the path was removed.</summary>
        WasMissing,

        /// <summary>Another trade (or the same trade a second time) points at the same file, so it was kept.</summary>
        KeptBecauseShared,

        /// <summary>The path doesn't lead to a file inside the Screenshots folder, so no file was touched.</summary>
        NotInScreenshotsFolder,

        /// <summary>The path was removed but the file couldn't be deleted (in use, no permission); it is left behind.</summary>
        Failed
    }

    /// <param name="Urls">The trade's screenshot paths after the removal.</param>
    public sealed record ScreenshotDeleteResult(List<string> Urls, ScreenshotFileOutcome File);

    public interface ITradeScreenshotService
    {
        /// <summary>
        /// Deletes one screenshot of a trade: its path in the database and its file in the Screenshots folder.
        /// <paramref name="path"/> is the path exactly as stored for the trade.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such trade.</exception>
        /// <exception cref="InvalidOperationException">The trade has no screenshot with that path (nothing is changed).</exception>
        Task<ScreenshotDeleteResult> DeleteAsync(int tradeId, string path, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Safety first, because a deleted file can't be brought back:
    /// the screenshot is found by its exact path in the trade, never by its position;
    /// the database is changed first, so a failure never leaves a path pointing at a deleted file (the worst case
    /// is a file left behind); the file is only deleted when it lies inside the Screenshots folder and no other
    /// path in the database still points at it; and only that one file is deleted - no folders.
    /// </summary>
    public class TradeScreenshotService(ApplicationDbContext db, IWebHostEnvironment environment, ILogger<TradeScreenshotService> logger) : ITradeScreenshotService
    {
        private readonly ApplicationDbContext _db = db;
        private readonly IWebHostEnvironment _environment = environment;
        private readonly ILogger<TradeScreenshotService> _logger = logger;

        public async Task<ScreenshotDeleteResult> DeleteAsync(int tradeId, string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No screenshot given.", nameof(path));

            var trade = await _db.Set<BaseTrade>().FirstOrDefaultAsync(t => t.Id == tradeId, cancellationToken)
                ?? throw new ArgumentException($"Trade with ID {tradeId} not found.", nameof(tradeId));

            var urls = trade.ScreenshotsUrls ?? [];
            int index = urls.FindIndex(url => string.Equals(url, path, StringComparison.Ordinal));
            if (index < 0) throw new InvalidOperationException("This screenshot is no longer part of the trade.");

            string key = Key(path);
            string? file = ResolveFile(_environment.WebRootPath, path);

            // Does anything else still point at this file? Another position in this trade, or any other trade.
            bool shared = urls.Where((_, i) => i != index).Any(url => SameFile(url, key));
            if (!shared)
            {
                var others = await _db.Set<BaseTrade>().AsNoTracking()
                    .Where(t => t.Id != tradeId && t.ScreenshotsUrls != null)
                    .Select(t => t.ScreenshotsUrls!)
                    .ToListAsync(cancellationToken);
                shared = others.Any(list => list.Any(url => SameFile(url, key)));
            }

            // 1. The database first: if this fails nothing has been touched.
            var remaining = urls.Where((_, i) => i != index).ToList();
            trade.ScreenshotsUrls = remaining;
            await _db.SaveChangesAsync(cancellationToken);

            // 2. Then the file.
            return new ScreenshotDeleteResult(remaining, DeleteFile(file, shared));
        }

        private ScreenshotFileOutcome DeleteFile(string? file, bool shared)
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
                _logger.LogWarning(ex, "The screenshot's path was removed but its file could not be deleted: {File}", file);
                return ScreenshotFileOutcome.Failed;
            }
        }

        /// <summary>A path as a comparable key: forward slashes, no leading slash (older research paths have both forms).</summary>
        internal static string Key(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

        // Case-insensitive on purpose: when in doubt the file is kept.
        private static bool SameFile(string path, string key) =>
            string.Equals(Key(path), key, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The full path of the file a database path points at, or null when it doesn't lead to a file strictly
        /// inside the Screenshots folder (an empty path, a rooted one, one with "." or ".." in it).
        /// </summary>
        internal static string? ResolveFile(string webRootPath, string path)
        {
            string relative = Key(path);
            if (relative.Length == 0 || relative.Split('/').Any(part => part is "." or "..")) return null;

            char separator = Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(ScreenshotStorage.GetRoot(webRootPath)).TrimEnd(separator, Path.AltDirectorySeparatorChar) + separator;
            string full = Path.GetFullPath(Path.Combine(webRootPath, relative.Replace('/', separator)));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(root, comparison) && full.Length > root.Length ? full : null;
        }
    }
}
