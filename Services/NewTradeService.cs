using DataAccess.Repository.IRepository;
using Microsoft.AspNetCore.Http;
using Models;
using Models.Trades;
using Models.ViewModels;
using Models.ViewModels.DisplayClasses;
using SharedEnums.Enums;
using System.Diagnostics;
using TradingTools.Blazor.Services.Interfaces;
using Utilities;

namespace TradingTools.Blazor.Services
{
    public class NewTradeService : INewTradeService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private NewTradeVM _viewModel = null!;
        private IFormFile[] _files = null!;

        public NewTradeService(IUnitOfWork unitOfWork, IWebHostEnvironment webHostEnvironment)
        {
            _unitOfWork = unitOfWork;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task SaveTradeAsync(NewTradeVM viewModel, IFormFile[] files)
        {
            _viewModel = viewModel;
            _files = files;

            if (_viewModel.SampleSizeViewData.SampleSizeType == SampleSizeType.Research)
            {
                await SaveResearchTradeAsync();
            }
            else
            {
                await SaveTradeAsync();
            }
        }

        private async Task SaveResearchTradeAsync()
        {
            switch (_viewModel.Strategy)
            {
                case Strategy.FirstBarPullback:
                    await SaveResearchDataFirstbarPullback(maxTradesProSampleSize: 100);
                    break;
                case Strategy.Cradle:
                    await SaveResearchCradleData(maxTradesProSampleSize: 100);
                    break;
                case Strategy.CandleBracketing:
                    await SaveCandleBracketingData(maxTradesProSampleSize: 200);
                    break;
            }
        }

        private async Task SaveTradeAsync()
        {
            switch (_viewModel.SampleSizeViewData.Strategy)
            {
                case Strategy.SRS:
                    await SaveSRSTrade();
                    break;
                case Strategy.BrunchBreak:
                    await SaveBrunchBreak();
                    break;
                case Strategy.Espresso:
                    await SaveEspresso();
                    break;
                default:
                    throw new ArgumentException("Unknown strategy");
            }
        }

        private async Task SaveEspresso()
        {
            try
            {
                var (sampleSizeId, isFull) = await ProcessSampleSize(maxTradesProSampleSize: 20, _viewModel.EspressoTrade.IsFlippedTheSwitch);
                _viewModel.EspressoTrade.SampleSizeId = sampleSizeId;
                _viewModel.EspressoTrade.JournalId = await CreateJournal();
                _viewModel.EspressoTrade.ScreenshotsUrls = await ScreenshotsService.SaveFilesAsync(_webHostEnvironment.WebRootPath, _viewModel, _viewModel.EspressoTrade, _files, isFull);

                _unitOfWork.Espresso.Add(_viewModel.EspressoTrade);
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in NewTradeService.SaveEspresso(): {ex.Message}");
            }
        }

        private async Task SaveBrunchBreak()
        {
            try
            {
                var (sampleSizeId, isFull) = await ProcessSampleSize(maxTradesProSampleSize: 20, _viewModel.BrunchBreakTrade.IsFlippedTheSwitch);
                _viewModel.BrunchBreakTrade.SampleSizeId = sampleSizeId;
                _viewModel.BrunchBreakTrade.JournalId = await CreateJournal();
                _viewModel.BrunchBreakTrade.ScreenshotsUrls = await ScreenshotsService.SaveFilesAsync(_webHostEnvironment.WebRootPath, _viewModel, _viewModel.BrunchBreakTrade, _files, isFull);

                _unitOfWork.BrunchBreak.Add(_viewModel.BrunchBreakTrade);
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in NewTradeService.SaveBrunchBreak(): {ex.Message}");
            }
        }

        private async Task SaveSRSTrade()
        {
            try
            {
                var (sampleSizeId, isFull) = await ProcessSampleSize(maxTradesProSampleSize: 20, _viewModel.SRSTrade.IsFlippedTheSwitch);
                _viewModel.SRSTrade.SampleSizeId = sampleSizeId;
                _viewModel.SRSTrade.JournalId = await CreateJournal();
                _viewModel.SRSTrade.ScreenshotsUrls = await ScreenshotsService.SaveFilesAsync(_webHostEnvironment.WebRootPath, _viewModel, _viewModel.SRSTrade, _files, isFull);

                _unitOfWork.SRS.Add(_viewModel.SRSTrade);
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in NewTradeService.SaveSRSTrade(): {ex.Message}");
            }

        }

        private async Task<int> CreateJournal()
        {
            var journal = new Journal();
            _unitOfWork.Journal.Add(journal);
            await _unitOfWork.SaveAsync();

            return journal.Id;
        }

        private async Task SaveCandleBracketingData(int maxTradesProSampleSize)
        {
            var viewData = _viewModel.ResearchData as ResearchCandleBracketing;
            var sampleSizeData = await ProcessSampleSize(maxTradesProSampleSize, viewData!.IsFlippedTheSwitch);
            viewData!.SampleSizeId = sampleSizeData.id;

            var researchData = new ResearchCandleBracketing();
            EntityMapper.ViewModelToEntity(researchData, viewData);
            researchData.SampleSizeId = sampleSizeData.id;
            researchData.ScreenshotsUrls = await ScreenshotsService.SaveFilesAsync(_webHostEnvironment.WebRootPath, _viewModel, viewData, _files, sampleSizeData.isFull);

            _unitOfWork.ResearchCandleBracketing.Add(researchData);
            try
            {
                await _unitOfWork.SaveAsync();
            }
            catch (Exception ex)
            {
                Debug.Write($"{ex.Message}");
            }
        }

        private async Task<ResearchCradle> SaveResearchCradleData(int maxTradesProSampleSize)
        {
            var viewData = _viewModel.ResearchData as ResearchCradle;
            var sampleSizeData = await ProcessSampleSize(maxTradesProSampleSize);
            viewData!.SampleSizeId = sampleSizeData.id;

            var researchData = new ResearchCradle();
            EntityMapper.ViewModelToEntity(researchData, viewData);
            researchData.SampleSizeId = sampleSizeData.id;
            researchData.ScreenshotsUrls = await ScreenshotsService.SaveFilesAsync(_webHostEnvironment.WebRootPath, _viewModel, viewData, _files, sampleSizeData.isFull);

            _unitOfWork.ResearchCradle.Add(researchData);
            await _unitOfWork.SaveAsync();

            return viewData;
        }

        private async Task<ResearchFirstBarPullback> SaveResearchDataFirstbarPullback(int maxTradesProSampleSize)
        {
            var viewData = _viewModel.ResearchData as ResearchFirstBarPullbackDisplay;
            var researchData = EntityMapper.ViewModelDisplayToEntity<ResearchFirstBarPullback, ResearchFirstBarPullbackDisplay>(viewData, existingEntity: null);

            var sampleSizeData = await ProcessSampleSize(maxTradesProSampleSize);
            researchData.SampleSizeId = sampleSizeData.id;

            researchData.ScreenshotsUrls = await ScreenshotsService.SaveFilesAsync(_webHostEnvironment.WebRootPath, _viewModel, researchData, _files, sampleSizeData.isFull);

            _unitOfWork.ResearchFirstBarPullback.Add(researchData);
            await _unitOfWork.SaveAsync();

            return researchData;
        }

        private async Task<(int id, bool isFull)> ProcessSampleSize(int maxTradesProSampleSize, bool isFlippedTheSwitch = false)
        {
            var sampleSizeData = await CheckLastSampleSize(maxTradesProSampleSize, isFlippedTheSwitch);

            if (sampleSizeData.isFull || sampleSizeData.id == 0)
            {
                Review review = new Review();
                _unitOfWork.Review.Add(review);
                await _unitOfWork.SaveAsync();

                var newSampleSize = new SampleSize
                {
                    Strategy = _viewModel.SampleSizeViewData.Strategy,
                    TimeFrame = _viewModel.SampleSizeViewData.TimeFrame,
                    SampleSizeType = _viewModel.SampleSizeViewData.SampleSizeType,
                    ReviewId = review?.Id
                };

                _unitOfWork.SampleSize.Add(newSampleSize);
                await _unitOfWork.SaveAsync();
                sampleSizeData.id = newSampleSize.Id;
            }

            return sampleSizeData;
        }

        private async Task<(int id, bool isFull)> CheckLastSampleSize(int maxTradesProSampleSize, bool isFlippedTheSwitch)
        {
            bool isFull = false;

            var listSampleSizes = await _unitOfWork.SampleSize.GetAllAsync(x =>
                x.TimeFrame == _viewModel.SampleSizeViewData.TimeFrame &&
                x.Strategy == _viewModel.SampleSizeViewData.Strategy &&
                x.SampleSizeType == _viewModel.SampleSizeViewData.SampleSizeType);

            if (!listSampleSizes.Any())
                return (0, false);

            var sampleSize = listSampleSizes.Last();

            int numberTradesInSampleSize = _viewModel.SampleSizeViewData.SampleSizeType switch
            {
                SampleSizeType.Research when _viewModel.SampleSizeViewData.Strategy == Strategy.FirstBarPullback =>
                    (await _unitOfWork.ResearchFirstBarPullback.GetAllAsync(x => x.SampleSizeId == sampleSize.Id)).Count,

                SampleSizeType.Research when _viewModel.SampleSizeViewData.Strategy == Strategy.Cradle =>
                    (await _unitOfWork.ResearchCradle.GetAllAsync(x => x.SampleSizeId == sampleSize.Id)).Count,

                SampleSizeType.Research when _viewModel.SampleSizeViewData.Strategy == Strategy.CandleBracketing =>
                    (await _unitOfWork.ResearchCandleBracketing.GetAllAsync(x => x.SampleSizeId == sampleSize.Id))
                        .Select(trade => trade.Date)
                        .Distinct()
                        .Count(),

                SampleSizeType.Trade or SampleSizeType.DemoTrading =>
                    (await _unitOfWork.BaseTrade.GetAllAsync(x => x.SampleSizeId == sampleSize.Id)).Count,

                _ => 0
            };

            if (sampleSize.Strategy == Strategy.CandleBracketing && numberTradesInSampleSize == 100 && !isFlippedTheSwitch)
            {
                isFull = true;
            }
            else if (numberTradesInSampleSize == maxTradesProSampleSize)
            {
                isFull = true;
            }

            return (sampleSize.Id, isFull);
        }
    }
}
