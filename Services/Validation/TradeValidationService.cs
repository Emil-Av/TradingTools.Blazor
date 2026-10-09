using DataAccess.Repository.IRepository;
using Models.Trades;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Services.Validation
{
    /// <summary>A trade with at least one validation problem, with enough context to find it on the Trades page.</summary>
    /// <param name="SampleSizeNumber">Position of the sample size as shown on the Trades page ("Sample size n / N").</param>
    /// <param name="TradeNumber">Position of the trade inside its sample size as shown on the Trades page ("Trade n / N").</param>
    public sealed record InvalidTrade(
        int TradeId,
        DateOnly Date,
        string Symbol,
        Strategy Strategy,
        TimeFrame TimeFrame,
        SampleSizeType AccountType,
        int SampleSizeId,
        int SampleSizeNumber,
        int TradeNumber,
        IReadOnlyList<TradeValidationIssue> Issues);

    /// <param name="Account">The account the check covered: the default account (Settings page).</param>
    /// <param name="SkippedTradeId">The most recent trade of that account, which isn't validated because it may still be in progress.</param>
    public sealed record TradeValidationReport(
        DateTime CompletedAtUtc,
        SampleSizeType Account,
        int TradesChecked,
        int? SkippedTradeId,
        IReadOnlyList<InvalidTrade> InvalidTrades);

    public interface ITradeValidationService
    {
        Task<TradeValidationReport> ValidateAllAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validates every trade of the default account (the Settings page: Demo Trading or Trade) except the most
    /// recent one of that account, which may still be open.
    /// </summary>
    public class TradeValidationService(IUnitOfWork unitOfWork, ISettingsService settings, TimeProvider timeProvider) : ITradeValidationService
    {
        private readonly IUnitOfWork _unitOfWork = unitOfWork;
        private readonly ISettingsService _settings = settings;
        private readonly TimeProvider _timeProvider = timeProvider;

        public async Task<TradeValidationReport> ValidateAllAsync(CancellationToken cancellationToken = default)
        {
            // Only the default account's trades (Demo Trading or Trade): the other account isn't looked at.
            var account = await _settings.GetDefaultAccountAsync(cancellationToken);

            var trades = await _unitOfWork.BaseTrade.GetAllAsync(
                t => t.SampleSize!.SampleSizeType == account,
                includeProperties: "SampleSize," + TradeAddOns.Include);
            cancellationToken.ThrowIfCancellationRequested();

            var sampleSizes = await _unitOfWork.SampleSize.GetAllAsync(s => s.SampleSizeType != SampleSizeType.Research);
            var sampleSizeNumbers = SampleSizeNumbering.Number(sampleSizes);

            // The Trades page lists the trades of a sample size by id.
            var tradeNumbers = trades
                .GroupBy(t => t.SampleSizeId)
                .SelectMany(group => group.OrderBy(t => t.Id).Select((t, index) => (t.Id, Number: index + 1)))
                .ToDictionary(x => x.Id, x => x.Number);

            var skipped = trades
                .OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .LastOrDefault();

            // The spread counts towards the net result a trade with add-ons is judged by.
            var spreads = await _settings.GetSpreadsAsync(cancellationToken);

            var invalid = new List<InvalidTrade>();
            foreach (var trade in trades.Where(t => t != skipped).OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id))
            {
                trade.SortAddOns();
                var issues = TradeValidator.Validate(trade, spreads.For(trade.Symbol));
                if (issues.Count == 0) continue;

                invalid.Add(new InvalidTrade(
                    trade.Id,
                    trade.Date,
                    string.IsNullOrWhiteSpace(trade.Symbol) ? "Unknown" : trade.Symbol.Trim().ToUpperInvariant(),
                    trade.SampleSize!.Strategy,
                    trade.SampleSize.TimeFrame,
                    trade.SampleSize.SampleSizeType,
                    trade.SampleSizeId,
                    sampleSizeNumbers.GetValueOrDefault(trade.SampleSizeId),
                    tradeNumbers[trade.Id],
                    issues));
            }

            return new TradeValidationReport(
                _timeProvider.GetUtcNow().UtcDateTime,
                account,
                trades.Count - (skipped is null ? 0 : 1),
                skipped?.Id,
                // In the order of the Trades page: by sample size (oldest first), then by trade within it.
                [.. invalid.OrderBy(t => t.SampleSizeId).ThenBy(t => t.TradeNumber)]);
        }
    }
}
