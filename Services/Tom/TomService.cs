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

        Task<TomStream> CreateStreamAsync(DateOnly date, string? url, TomSession session, TomTrader trader, CancellationToken cancellationToken = default);

        /// <summary>Whether something was changed on the page that isn't saved yet.</summary>
        bool HasUnsavedChanges { get; }

        /// <summary>Saves every change made on the page (dates, sessions, traders, links, notes, times in the video).</summary>
        /// <exception cref="ArgumentException">A video link isn't a web link (http or https).</exception>
        Task SaveAsync(CancellationToken cancellationToken = default);

        /// <summary>A new trade in the stream with the given screenshots (in the order given).</summary>
        Task<TomTrade> AddTradeAsync(TomStream stream, IFormFile[] screenshots, CancellationToken cancellationToken = default);

        /// <summary>More screenshots for a trade, after the ones it has.</summary>
        Task AddScreenshotsAsync(TomStream stream, TomTrade trade, IFormFile[] screenshots, CancellationToken cancellationToken = default);

        /// <summary>One screenshot of a trade, with its image, found by its id.</summary>
        /// <exception cref="InvalidOperationException">The trade has no such screenshot (nothing is changed).</exception>
        Task DeleteScreenshotAsync(TomTrade trade, int screenshotId, CancellationToken cancellationToken = default);

        /// <summary>A trade with its notes and its screenshots.</summary>
        Task DeleteTradeAsync(TomStream stream, TomTrade trade, CancellationToken cancellationToken = default);

        /// <summary>A stream with its notes, its trades and their screenshots.</summary>
        Task DeleteStreamAsync(TomStream stream, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The Tom section: streams of Tom and Andi (link, date, notes) and the trades they took in them (screenshots,
    /// notes). The page edits the tracked streams and trades directly; every save goes through here, so the notes are
    /// always sanitized. Screenshots are in the database, like the Trades page's, and go with their trade.
    /// </summary>
    public class TomService(ApplicationDbContext db, Ganss.Xss.IHtmlSanitizer sanitizer) : ITomService
    {
        private readonly ApplicationDbContext _db = db;
        private readonly Ganss.Xss.IHtmlSanitizer _sanitizer = sanitizer;

        public async Task<List<TomStream>> GetStreamsAsync(CancellationToken cancellationToken = default)
        {
            var streams = await _db.TomStreams.Include(s => s.Trades).ThenInclude(t => t.Screenshots).ToListAsync(cancellationToken);
            foreach (var stream in streams)
            {
                stream.Trades.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (var trade in stream.Trades) TradeScreenshots.Sort(trade.Screenshots);
            }
            return streams;
        }

        public async Task<TomStream> CreateStreamAsync(DateOnly date, string? url, TomSession session, TomTrader trader, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(url) && !TomLinks.IsWebLink(url))
                throw new ArgumentException("The video link has to be a web link (https://...).", nameof(url));

            var stream = new TomStream { Date = date, Url = Clean(url), Session = session, Trader = trader };
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
            // The trade and its screenshots are saved together.
            var trade = new TomTrade { TomStreamId = stream.Id, Screenshots = await TradeScreenshots.FromFilesAsync(screenshots, firstPosition: 0, cancellationToken) };
            stream.Trades.Add(trade);
            await SaveTrackedAsync(cancellationToken);
            TradeScreenshots.ReleaseImages(_db, trade.Screenshots);
            return trade;
        }

        public async Task AddScreenshotsAsync(TomStream stream, TomTrade trade, IFormFile[] screenshots, CancellationToken cancellationToken = default)
        {
            var added = await TradeScreenshots.FromFilesAsync(screenshots, TradeScreenshots.NextPosition(trade.Screenshots), cancellationToken);
            trade.Screenshots.AddRange(added);
            await SaveTrackedAsync(cancellationToken);
            TradeScreenshots.ReleaseImages(_db, added);
        }

        public async Task DeleteScreenshotAsync(TomTrade trade, int screenshotId, CancellationToken cancellationToken = default)
        {
            var screenshot = trade.Screenshots.FirstOrDefault(s => s.Id == screenshotId)
                ?? throw new InvalidOperationException("This screenshot is no longer part of the trade.");

            // Its image goes with it (the database deletes it).
            trade.Screenshots.Remove(screenshot);
            _db.Screenshots.Remove(screenshot);
            await SaveTrackedAsync(cancellationToken);
        }

        public async Task DeleteTradeAsync(TomStream stream, TomTrade trade, CancellationToken cancellationToken = default)
        {
            stream.Trades.Remove(trade);
            _db.TomTrades.Remove(trade); // its screenshots go with it
            await SaveTrackedAsync(cancellationToken);
        }

        public async Task DeleteStreamAsync(TomStream stream, CancellationToken cancellationToken = default)
        {
            _db.TomStreams.Remove(stream); // its trades and their screenshots go with it
            await SaveTrackedAsync(cancellationToken);
        }

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
