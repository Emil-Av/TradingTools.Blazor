using DataAccess.Data;
using Microsoft.EntityFrameworkCore;

namespace TradingTools.Blazor.Services.Screenshots
{
    public interface ITradeScreenshotService
    {
        /// <summary>
        /// Deletes one screenshot of a trade of the journal, with its image. The screenshot is found by its id and must
        /// belong to that trade.
        /// </summary>
        /// <exception cref="InvalidOperationException">The trade has no such screenshot (nothing is changed).</exception>
        Task DeleteAsync(int tradeId, int screenshotId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Deleting a screenshot: by its id, never by its position, and only when it belongs to the given trade. Its image
    /// goes with it (the database deletes it). The other screenshots keep their positions.
    /// </summary>
    public class TradeScreenshotService(ApplicationDbContext db) : ITradeScreenshotService
    {
        private readonly ApplicationDbContext _db = db;

        public async Task DeleteAsync(int tradeId, int screenshotId, CancellationToken cancellationToken = default)
        {
            var screenshot = await _db.Screenshots.FirstOrDefaultAsync(s => s.Id == screenshotId && s.BaseTradeId == tradeId, cancellationToken)
                ?? throw new InvalidOperationException("This screenshot is no longer part of the trade.");

            _db.Screenshots.Remove(screenshot);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
