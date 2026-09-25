using DataAccess.Repository.IRepository;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Interfaces;

namespace TradingTools.Blazor.Services.Dashboard
{
    public class DashboardService(IUnitOfWork unitOfWork) : IDashboardService
    {
        private readonly IUnitOfWork _unitOfWork = unitOfWork;

        public async Task<List<DashboardTrade>> GetTradesAsync()
        {
            var trades = await _unitOfWork.BaseTrade.GetAllAsync(
                t => t.Status == EStatus.Closed
                     && (t.SampleSize!.Strategy == Strategy.SRS || t.SampleSize.Strategy == Strategy.Espresso)
                     && t.SampleSize.SampleSizeType != SampleSizeType.Research,
                includeProperties: "SampleSize");

            var sampleSizes = await _unitOfWork.SampleSize.GetAllAsync(
                s => (s.Strategy == Strategy.SRS || s.Strategy == Strategy.Espresso)
                     && s.SampleSizeType != SampleSizeType.Research);

            // "Sample size #n" as shown on the Trades page: position among ALL sample sizes of the
            // same account type, strategy and timeframe - numbered from the sample sizes themselves,
            // not from the trades above, so one whose trades are all still open keeps its place.
            var sampleSizeNumbers = sampleSizes
                .GroupBy(s => (s.SampleSizeType, s.Strategy, s.TimeFrame))
                .SelectMany(group => group.OrderBy(s => s.Id).Select((s, index) => (s.Id, Number: index + 1)))
                .ToDictionary(x => x.Id, x => x.Number);

            return [.. trades
                .OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => new DashboardTrade(
                    t.Id,
                    t.Date,
                    t.CreatedAt,
                    NormalizeSymbol(t.Symbol),
                    t.Direction,
                    t.SampleSize!.Strategy,
                    t.SampleSize.TimeFrame,
                    t.SampleSize.SampleSizeType,
                    t.SampleSizeId,
                    sampleSizeNumbers[t.SampleSizeId],
                    t.Amount,
                    SignedPoints(t),
                    t.Outcome))];
        }

        /// <summary>
        /// The trade's result in points, signed by its outcome. The recorded P&amp;L field holds the
        /// points as a positive number (the sign lives in Outcome), so it's the primary source. It
        /// falls back to |exit - entry| only when P&amp;L wasn't filled in - the Direction field isn't
        /// reliable enough to derive the sign from prices, so Outcome decides it either way.
        /// A win/loss with 0 points is treated as not filled in rather than as a 0-point result.
        /// </summary>
        internal static double? SignedPoints(BaseTrade trade)
        {
            if (trade.Outcome == EOutcome.Breakeven) return 0;

            double? magnitude =
                trade.PnL is { } pnl && pnl != 0 ? Math.Abs(pnl)
                : trade.EntryPrice is > 0 && trade.ExitPrice is > 0 && trade.ExitPrice != trade.EntryPrice
                    ? Math.Abs(trade.ExitPrice.Value - trade.EntryPrice.Value)
                    : null;

            if (magnitude is null) return null;
            return trade.Outcome == EOutcome.Win ? magnitude : -magnitude;
        }

        private static string NormalizeSymbol(string? symbol) =>
            string.IsNullOrWhiteSpace(symbol) ? "Unknown" : symbol.Trim().ToUpperInvariant();
    }
}
