using Models;
using Models.RequestModels;
using Models.Trades;
using Models.ViewModels;
using SharedEnums.Enums;
using Microsoft.AspNetCore.Http;

namespace TradingTools.Blazor.Services.Interfaces
{
    public interface ITradesService
    {
        TradesVM InitializeNewTradeTradesViewModel();
        Task<TradesVM> InitializeTradesViewModelAsync();
        Task UpdateTradeDataAsync(BaseTrade tradeData);
        Task UpdateReviewAsync(Review review);
        Task UpdateJournalAsync(Journal journal);
        Task UpdateResearchData(UpdateResearchDataModel updateResearchData);
        Task DeleteTrade(DeleteTradeRequestModel deleteTradeRequest);
        Task<TradesVM> LoadSampleSizeNumberAsync(int sampleSizeId);
        Task<TradesVM> LoadTimeFrameAsync(TradesLoadTimeFrameRequestModel requestModel);
        Task<TradesVM> LoadStrategyAsync(Strategy strategy, SampleSizeType sampleSizeType);
        Task<TradesVM> LoadTypeAsync(SampleSizeType sampleSizeType, Strategy strategy);
        Task<List<string>> UploadScreenshotsAsync(int tradeId, IFormFile[] files);
    }
}
