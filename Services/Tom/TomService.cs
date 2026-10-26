using DataAccess.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models.Tom;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Services.Tom
{
    public interface ITomService
    {
        /// <summary>Every stream with its trades (the trades in the order they were added).</summary>
        Task<List<TomStream>> GetStreamsAsync(CancellationToken cancellationToken = default);

        Task<TomStream> CreateStreamAsync(DateOnly date, string? url, string? title, CancellationToken cancellationToken = default);

        /// <summary>Whether something was changed on the page that isn't saved yet.</summary>
        bool HasUnsavedChanges { get; }

        /// <summary>Saves every change made on the page (dates, links, titles, notes, times in the video).</summary>
        /// <exception cref="ArgumentException">A video link isn't a web link (http or https).</exception>
        Task SaveAsync(CancellationToken cancellationToken = default);

        /// <summary>A new trade in the stream with the given screenshots (in the order given).</summary>
        Task<TomTrade> AddTradeAsync(TomStream stream, IFormFile[] screenshots, CancellationToken cancellationToken = default);

        /// <summary>More screenshots for a trade, after the ones it has.</summary>
        Task AddScreenshotsAsync(TomStream stream, TomTrade trade, IFormFile[] screenshots, CancellationToken cancellationToken = default);

        /// <summary>One screenshot of a trade: its path, and its file unless something else still uses it.</summary>
        /// <exception cref="InvalidOperationException">The trade has no screenshot with that path (nothing is changed).</exception>
        Task<ScreenshotFileOutcome> DeleteScreenshotAsync(TomTrade trade, string path, CancellationToken cancellationToken = default);

        /// <summary>A trade with its notes and its screenshot files.</summary>
        Task DeleteTradeAsync(TomStream stream, TomTrade trade, CancellationToken cancellationToken = default);

        /// <summary>A stream with its notes, its trades and their screenshot files.</summary>
        Task DeleteStreamAsync(TomStream stream, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The Tom section: streams of Tom and Andi (link, date, notes) and the trades they took in them (screenshots,
    /// notes). The page edits the tracked streams and trades directly; every save goes through here, so the notes are
    /// always sanitized. Screenshots live in Screenshots/Tom/Stream n/Trade m/ and are deleted with the same rules as
    /// on the Trades page (database first, only inside the Screenshots folder, never a file something else uses).
    /// </summary>
    public class TomService(ApplicationDbContext db, IWebHostEnvironment environment, Ganss.Xss.IHtmlSanitizer sanitizer, ILogger<TomService> logger) : ITomService
    {
        private readonly ApplicationDbContext _db = db;
        private readonly IWebHostEnvironment _environment = environment;
        private readonly Ganss.Xss.IHtmlSanitizer _sanitizer = sanitizer;
        private readonly ILogger<TomService> _logger = logger;

        public async Task<List<TomStream>> GetStreamsAsync(CancellationToken cancellationToken = default)
        {
            var streams = await _db.TomStreams.Include(s => s.Trades).ToListAsync(cancellationToken);
            foreach (var stream in streams) stream.Trades.Sort((a, b) => a.Id.CompareTo(b.Id));
            return streams;
        }

        public async Task<TomStream> CreateStreamAsync(DateOnly date, string? url, string? title, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(url) && !TomLinks.IsWebLink(url))
                throw new ArgumentException("The video link has to be a web link (https://...).", nameof(url));

            var stream = new TomStream { Date = date, Url = Clean(url), Title = Clean(title) };
            _db.TomStreams.Add(stream);
            await SaveTrackedAsync(cancellationToken);
            return stream;
        }

        public bool HasUnsavedChanges =>
            _db.ChangeTracker.Entries().Any(e => (e.Entity is TomStream or TomTrade) && e.State != EntityState.Unchanged);

        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            var badLink = _db.ChangeTracker.Entries<TomStream>()
                .Select(e => e.Entity)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Url) && !TomLinks.IsWebLink(s.Url));
            if (badLink is not null)
                throw new ArgumentException($"The video link of the stream of {badLink.Date.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)} has to be a web link (https://...).");

            await SaveTrackedAsync(cancellationToken);
        }

        public async Task<TomTrade> AddTradeAsync(TomStream stream, IFormFile[] screenshots, CancellationToken cancellationToken = default)
        {
            // The trade is saved first: its folder is named after its id.
            var trade = new TomTrade { TomStreamId = stream.Id };
            stream.Trades.Add(trade);
            await SaveTrackedAsync(cancellationToken);

            trade.ScreenshotsUrls = await ScreenshotStorage.SaveFilesToFolderAsync(_environment.WebRootPath, FolderOf(stream, trade), screenshots);
            await SaveTrackedAsync(cancellationToken);
            return trade;
        }

        public async Task AddScreenshotsAsync(TomStream stream, TomTrade trade, IFormFile[] screenshots, CancellationToken cancellationToken = default)
        {
            var paths = await ScreenshotStorage.SaveFilesToFolderAsync(_environment.WebRootPath, FolderOf(stream, trade), screenshots);
            trade.ScreenshotsUrls = [.. trade.ScreenshotsUrls ?? [], .. paths];
            await SaveTrackedAsync(cancellationToken);
        }

        public async Task<ScreenshotFileOutcome> DeleteScreenshotAsync(TomTrade trade, string path, CancellationToken cancellationToken = default)
        {
            var urls = trade.ScreenshotsUrls ?? [];
            int index = urls.FindIndex(url => string.Equals(url, path, StringComparison.Ordinal));
            if (index < 0) throw new InvalidOperationException("This screenshot is no longer part of the trade.");

            string key = ScreenshotFiles.Key(path);
            bool shared = urls.Where((_, i) => i != index).Any(url => ScreenshotFiles.SameFile(url, key))
                || await ScreenshotFiles.IsUsedElsewhereAsync(_db, path, exceptTomTradeId: trade.Id, cancellationToken: cancellationToken);

            // The database first: if this fails nothing has been touched.
            trade.ScreenshotsUrls = [.. urls.Where((_, i) => i != index)];
            await SaveTrackedAsync(cancellationToken);

            return ScreenshotFiles.Delete(ScreenshotFiles.ResolveFile(_environment.WebRootPath, path), shared, _logger);
        }

        public async Task DeleteTradeAsync(TomStream stream, TomTrade trade, CancellationToken cancellationToken = default)
        {
            var files = await FilesToDeleteAsync(trade.ScreenshotsUrls ?? [], [trade.Id], cancellationToken);

            stream.Trades.Remove(trade);
            _db.TomTrades.Remove(trade);
            await SaveTrackedAsync(cancellationToken);

            DeleteFiles(files);
        }

        public async Task DeleteStreamAsync(TomStream stream, CancellationToken cancellationToken = default)
        {
            var files = await FilesToDeleteAsync(
                [.. stream.Trades.SelectMany(t => t.ScreenshotsUrls ?? [])], [.. stream.Trades.Select(t => t.Id)], cancellationToken);

            _db.TomStreams.Remove(stream); // its trades go with it
            await SaveTrackedAsync(cancellationToken);

            DeleteFiles(files);
        }

        /// <summary>The files of these paths, each with whether something outside the given Tom trades still uses it.</summary>
        private async Task<List<(string? File, bool Shared)>> FilesToDeleteAsync(IEnumerable<string> paths, IReadOnlyCollection<int> tomTradeIds, CancellationToken cancellationToken)
        {
            var files = new List<(string? File, bool Shared)>();
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool shared = await ScreenshotFiles.IsUsedElsewhereAsync(_db, path, null, tomTradeIds, cancellationToken);
                files.Add((ScreenshotFiles.ResolveFile(_environment.WebRootPath, path), shared));
            }
            return files;
        }

        private void DeleteFiles(IEnumerable<(string? File, bool Shared)> files)
        {
            foreach (var (file, shared) in files) ScreenshotFiles.Delete(file, shared, _logger);
        }

        private string FolderOf(TomStream stream, TomTrade trade) =>
            Path.Combine(ScreenshotStorage.GetRoot(_environment.WebRootPath), "Tom", $"Stream {stream.Id}", $"Trade {trade.Id}");

        /// <summary>Saves, with the notes sanitized (they are HTML from the editor) and the short fields trimmed.</summary>
        private async Task SaveTrackedAsync(CancellationToken cancellationToken)
        {
            foreach (var entry in _db.ChangeTracker.Entries<TomStream>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                entry.Entity.Url = Clean(entry.Entity.Url);
                entry.Entity.Title = Clean(entry.Entity.Title);
                entry.Entity.Notes = Sanitize(entry.Entity.Notes);
            }
            foreach (var entry in _db.ChangeTracker.Entries<TomTrade>().Where(e => e.State is EntityState.Added or EntityState.Modified))
                entry.Entity.Notes = Sanitize(entry.Entity.Notes);

            await _db.SaveChangesAsync(cancellationToken);
        }

        private string? Sanitize(string? html) => string.IsNullOrWhiteSpace(html) ? html : _sanitizer.Sanitize(html);

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
