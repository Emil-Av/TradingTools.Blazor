using DataAccess.Repository.IRepository;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Interfaces;
using TradingTools.Blazor.Services.Calculation;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Services.Dashboard
{
    public class DashboardService(IUnitOfWork unitOfWork, ISettingsService settings) : IDashboardService
    {
        private readonly IUnitOfWork _unitOfWork = unitOfWork;
        private readonly ISettingsService _settings = settings;

        public async Task<List<DashboardTrade>> GetTradesAsync()
        {
            var trades = await _unitOfWork.BaseTrade.GetAllAsync(
                t => t.Status == EStatus.Closed
                     && (t.SampleSize!.Strategy == Strategy.SRS || t.SampleSize.Strategy == Strategy.Espresso)
                     && t.SampleSize.SampleSizeType != SampleSizeType.Research,
                includeProperties: "SampleSize," + TradeAddOns.Include);

            var sampleSizes = await _unitOfWork.SampleSize.GetAllAsync(
                s => (s.Strategy == Strategy.SRS || s.Strategy == Strategy.Espresso)
                     && s.SampleSizeType != SampleSizeType.Research);

            // Numbered from the sample sizes themselves, not from the trades above, so one whose
            // trades are all still open keeps its place.
            var sampleSizeNumbers = SampleSizeNumbering.Number(sampleSizes);

            // The spread of each instrument is taken off every position's result, here and nowhere else, so the
            // dashboard and the History page (which both use this list) always agree with the Trades page.
            var spreads = await _settings.GetSpreadsAsync();

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
                    TradeResults.MainPointsAfterSpread(t, spreads.For(t.Symbol)),
                    t.Outcome)
                {
                    AddOns = [.. t.AddOns
                        .OrderBy(a => a.Id)
                        .Select(a => new DashboardAddOn(TradeResults.AddOnPointsAfterSpread(t, a, spreads.For(t.Symbol)), a.Volume))]
                })];
        }

        /// <summary>The main position's result in points before the spread (see <see cref="TradeResults.MainPoints"/>).</summary>
        internal static double? SignedPoints(BaseTrade trade) => TradeResults.MainPoints(trade);

        private static string NormalizeSymbol(string? symbol) =>
            string.IsNullOrWhiteSpace(symbol) ? "Unknown" : symbol.Trim().ToUpperInvariant();
    }
}
