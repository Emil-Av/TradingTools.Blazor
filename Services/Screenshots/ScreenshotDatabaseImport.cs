using System.Security.Cryptography;
using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models.Screenshots;
using Models.Trades;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// Moves the screenshots from the Screenshots folder into the database, once. Which screenshot belongs to which
    /// trade comes only from the trade's own list of paths (ScreenshotsUrls), never from the folder names: for every
    /// trade - of the journal and of Tom - and every path at index i, the file is read and stored as that trade's
    /// screenshot at position i, with the path kept as LegacyPath.
    ///
    /// Safety first:
    /// - Nothing on disk and nothing in ScreenshotsUrls is changed or deleted; they stay as they were.
    /// - Everything happens in one transaction: either all screenshots are in the database, or none.
    /// - Before committing, every stored image is read back and compared with the file's (SHA-256), and the owner,
    ///   position and path of every row with what was planned. Any difference and nothing is kept.
    /// - It runs only once: the ScreenshotImport row it writes says it is done. If there are screenshots already but
    ///   no such row, it stops instead of adding them a second time.
    /// - A path whose file is missing (or that points outside the Screenshots folder) is counted and logged; the
    ///   trade keeps its old path in ScreenshotsUrls.
    /// </summary>
    public sealed class ScreenshotDatabaseImport(ApplicationDbContext db, IWebHostEnvironment environment, TimeProvider time, ILogger<ScreenshotDatabaseImport> logger)
    {
        public sealed record Result(bool Ran, int Imported, IReadOnlyList<string> MissingPaths);

        /// <summary>The trade a planned screenshot belongs to and where: exactly one of the two ids is set.</summary>
        private sealed record Planned(int? BaseTradeId, int? TomTradeId, int Position, string LegacyPath, byte[] Sha256);

        public async Task<Result> RunAsync(CancellationToken cancellationToken = default)
        {
            if (await db.ScreenshotImports.AnyAsync(cancellationToken)) return new(false, 0, []);

            if (await db.Screenshots.AnyAsync(cancellationToken))
                throw new InvalidOperationException(
                    "There are screenshots in the database but no record that the Screenshots folder was imported. " +
                    "Nothing was imported, so no screenshot is added twice.");

            var trades = await db.Set<BaseTrade>().AsNoTracking()
                .Where(t => t.ScreenshotsUrls != null)
                .Select(t => new { t.Id, Paths = t.ScreenshotsUrls! })
                .ToListAsync(cancellationToken);
            var tomTrades = await db.TomTrades.AsNoTracking()
                .Where(t => t.ScreenshotsUrls != null)
                .Select(t => new { t.Id, Paths = t.ScreenshotsUrls! })
                .ToListAsync(cancellationToken);

            var planned = new List<Planned>();
            var missing = new List<string>();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            foreach (var trade in trades)
                await ImportTradeAsync(trade.Id, null, trade.Paths, planned, missing, cancellationToken);
            foreach (var trade in tomTrades)
                await ImportTradeAsync(null, trade.Id, trade.Paths, planned, missing, cancellationToken);

            await VerifyAsync(planned, cancellationToken);

            db.ScreenshotImports.Add(new ScreenshotImport { ImportedAt = time.GetUtcNow().UtcDateTime, Imported = planned.Count, Missing = missing.Count });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();

            logger.LogInformation("Moved {Count} screenshot(s) of {Trades} trade(s) from the Screenshots folder into the database.",
                planned.Count, planned.Select(p => (p.BaseTradeId, p.TomTradeId)).Distinct().Count());
            foreach (var path in missing)
                logger.LogWarning("Screenshot not moved into the database, its file was not found: {Path}", path);

            return new(true, planned.Count, missing);
        }

        /// <summary>One trade's screenshots, saved on their own so only one trade's images are in memory at a time.</summary>
        private async Task ImportTradeAsync(int? baseTradeId, int? tomTradeId, List<string> paths, List<Planned> planned, List<string> missing, CancellationToken cancellationToken)
        {
            if (paths.Count == 0) return;

            for (int position = 0; position < paths.Count; position++)
            {
                string path = paths[position];
                string? file = ScreenshotFiles.ResolveFile(environment.WebRootPath, path);
                if (file is null || !File.Exists(file))
                {
                    missing.Add($"{(baseTradeId is { } id ? $"trade {id}" : $"Tom trade {tomTradeId}")}, screenshot {position + 1}: {path}");
                    continue;
                }

                byte[] data = await File.ReadAllBytesAsync(file, cancellationToken);
                db.Screenshots.Add(new Screenshot
                {
                    BaseTradeId = baseTradeId,
                    TomTradeId = tomTradeId,
                    Position = position,
                    FileName = TradeScreenshots.FileNameOf(path),
                    CreatedAt = File.GetLastWriteTimeUtc(file),
                    LegacyPath = path,
                    Content = new ScreenshotContent { Data = data }
                });
                planned.Add(new Planned(baseTradeId, tomTradeId, position, path, SHA256.HashData(data)));
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        /// <summary>Reads every screenshot back, one at a time, and compares it with what was planned.</summary>
        private async Task VerifyAsync(List<Planned> planned, CancellationToken cancellationToken)
        {
            var expected = planned.ToDictionary(p => (p.BaseTradeId, p.TomTradeId, p.Position));
            int found = 0;

            var rows = db.Screenshots.AsNoTracking()
                .Select(s => new { s.BaseTradeId, s.TomTradeId, s.Position, s.LegacyPath, Data = s.Content == null ? null : s.Content.Data })
                .AsAsyncEnumerable();
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                if (!expected.TryGetValue((row.BaseTradeId, row.TomTradeId, row.Position), out var plan))
                    throw new InvalidOperationException($"Unexpected screenshot (trade {row.BaseTradeId}, Tom trade {row.TomTradeId}, position {row.Position}); nothing was imported.");
                if (row.LegacyPath != plan.LegacyPath)
                    throw new InvalidOperationException($"Screenshot of {plan.LegacyPath} has the path {row.LegacyPath}; nothing was imported.");
                if (row.Data is null || !SHA256.HashData(row.Data).AsSpan().SequenceEqual(plan.Sha256))
                    throw new InvalidOperationException($"The image stored for {plan.LegacyPath} differs from its file; nothing was imported.");
                found++;
            }

            if (found != planned.Count)
                throw new InvalidOperationException($"{planned.Count} screenshot(s) were stored but {found} read back; nothing was imported.");
        }
    }
}
