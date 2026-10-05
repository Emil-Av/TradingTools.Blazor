using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Services.Reviews
{
    /// <summary>A sample size with at least one review to do.</summary>
    /// <param name="SampleSizeNumber">Position of the sample size as shown on the Trades page ("Sample size n").</param>
    /// <param name="TradeCount">How many trades the sample size has so far (a full one has 20).</param>
    /// <param name="Reviews">The reviews to do, oldest first; the first is the one to open.</param>
    public sealed record DueReview(
        int SampleSizeId,
        SampleSizeType Account,
        Strategy Strategy,
        TimeFrame TimeFrame,
        int SampleSizeNumber,
        int TradeCount,
        IReadOnlyList<ReviewKind> Reviews)
    {
        public bool IsComplete => TradeCount >= ReviewSchedule.TradesPerSampleSize;
    }

    public interface IReviewService
    {
        /// <summary>The sample sizes of the default account with reviews to do, newest first.</summary>
        Task<IReadOnlyList<DueReview>> GetDueAsync(CancellationToken cancellationToken = default);

        /// <summary>The reviews to do for one sample size; null when there are none.</summary>
        Task<DueReview?> GetDueForSampleSizeAsync(int sampleSizeId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Works out which reviews are due (see <see cref="ReviewSchedule"/>) from the trades of each sample size and
    /// the review texts. Nothing is stored: a review is due until it has text, so it can't get out of step.
    /// A sample size without a review record can't be reviewed and is left out.
    /// </summary>
    public class ReviewService(ApplicationDbContext db, ISettingsService settings) : IReviewService
    {
        private readonly ApplicationDbContext _db = db;
        private readonly ISettingsService _settings = settings;

        public async Task<IReadOnlyList<DueReview>> GetDueAsync(CancellationToken cancellationToken = default)
        {
            var account = await _settings.GetDefaultAccountAsync(cancellationToken);
            var due = await LoadAsync(cancellationToken);

            return [.. due.Where(d => d.Account == account).OrderByDescending(d => d.SampleSizeId)];
        }

        public async Task<DueReview?> GetDueForSampleSizeAsync(int sampleSizeId, CancellationToken cancellationToken = default) =>
            (await LoadAsync(cancellationToken)).FirstOrDefault(d => d.SampleSizeId == sampleSizeId);

        private async Task<List<DueReview>> LoadAsync(CancellationToken cancellationToken)
        {
            var sampleSizes = await _db.SampleSizes.AsNoTracking()
                .Include(s => s.Review)
                .Where(s => s.SampleSizeType != SampleSizeType.Research)
                .ToListAsync(cancellationToken);

            // Numbered among all of them, as the Trades page and the Data check do.
            var numbers = SampleSizeNumbering.Number(sampleSizes);

            var tradeCounts = (await _db.Set<BaseTrade>().AsNoTracking()
                    .GroupBy(t => t.SampleSizeId)
                    .Select(g => new { SampleSizeId = g.Key, Count = g.Count() })
                    .ToListAsync(cancellationToken))
                .ToDictionary(x => x.SampleSizeId, x => x.Count);

            var result = new List<DueReview>();
            foreach (var sampleSize in sampleSizes)
            {
                if (sampleSize.Review is null) continue;

                int tradeCount = tradeCounts.GetValueOrDefault(sampleSize.Id);
                var reviews = ReviewSchedule.Due(tradeCount, sampleSize.Review);
                if (reviews.Count == 0) continue;

                result.Add(new DueReview(
                    sampleSize.Id, sampleSize.SampleSizeType, sampleSize.Strategy, sampleSize.TimeFrame,
                    numbers.GetValueOrDefault(sampleSize.Id), tradeCount, reviews));
            }

            return result;
        }
    }
}
