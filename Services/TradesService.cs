using DataAccess.Repository.IRepository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Models.RequestModels;
using Models.Trades;
using Models.ViewModels;
using Newtonsoft.Json;
using SharedEnums.Enums;
using System.Diagnostics;
using TradingTools.Blazor.Services.Interfaces;
using Utilities.Trade;

namespace TradingTools.Blazor.Services
{
    public class TradesService(IUnitOfWork unitOfWork, IWebHostEnvironment webHostEnvironment, DeleteTradeService deleteTradeService) : ITradesService
    {
        private readonly IUnitOfWork _unitOfWork = unitOfWork;
        private readonly IWebHostEnvironment webHostEnvironment = webHostEnvironment;
        private readonly DeleteTradeService _deleteTradeService = deleteTradeService;
        private List<SampleSize> _allSampleSizes = [];
        private TradesVM _tradesVM = new();

        public async Task<TradesVM> LoadTypeAsync(SampleSizeType sampleSizeType, Strategy strategy)
        {
            _allSampleSizes = await _unitOfWork.SampleSize.GetAllAsync(sampleSize =>
                                                                        sampleSize.SampleSizeType == sampleSizeType,
                                                                        includeProperties: "Review");
            if (!_allSampleSizes.Any())
            {
                await InitializeTradesViewModelAsync();
            }

            Strategy availableStrategy = _allSampleSizes.Any(s => s.Strategy == strategy) ? strategy : _allSampleSizes.First().Strategy;

            await SetViewModel(availableStrategy);
            return _tradesVM;
        }

        public async Task<TradesVM> LoadStrategyAsync(Strategy strategy, SampleSizeType sampleSizeType)
        {
            _allSampleSizes = [.. (await _unitOfWork.SampleSize
                .GetAllAsync(sampleSize => sampleSize.SampleSizeType == sampleSizeType, includeProperties: "Review"))
                .OrderBy(sampleSize => sampleSize.Id)];

            if (!_allSampleSizes.Any())
            {
                await InitializeTradesViewModelAsync();
            }
            await SetViewModel(strategy);
            return _tradesVM;
        }

        public async Task<TradesVM> LoadTimeFrameAsync(TradesLoadTimeFrameRequestModel requestModel)
        {
            _allSampleSizes = [.. (await _unitOfWork.SampleSize.GetAllAsync(sampleSize => sampleSize.Strategy == requestModel.Strategy &&
                                                                                     sampleSize.SampleSizeType == requestModel.SampleSizeType &&
                                                                                     sampleSize.TimeFrame == requestModel.TimeFrame,
                                                                                     includeProperties: "Review")).OrderBy(sampleSize => sampleSize.Id)];

            if (!_allSampleSizes.Any())
            {
                await InitializeTradesViewModelAsync();
            }

            await SetViewModel();

            return _tradesVM;
        }

        public async Task<TradesVM> LoadSampleSizeNumberAsync(int sampleSizeId)
        {
            _allSampleSizes = await GetAllSampleSizes();

            if (!_allSampleSizes.Any())
            {
                return _tradesVM;
            }

            await SetViewModel(sampleSizeId);

            return _tradesVM;
        }

        public async Task<TradesVM> InitializeTradesViewModelAsync()
        {
            _allSampleSizes = await GetAllSampleSizes();

            if (!_allSampleSizes.Any())
            {
                return _tradesVM;
            }

            await SetViewModel();
            _tradesVM.ErrorMsg = _tradesVM.CurrentTrade is null ? "No trade for the selected paramaters." : string.Empty;

            return _tradesVM;
        }

        public TradesVM InitializeNewTradeTradesViewModel()
        {
            return new();
        }

        public async Task UpdateTradeDataAsync(BaseTrade tradeData)
        {
            await _unitOfWork.BaseTrade.UpdateAsync(tradeData);
            await _unitOfWork.SaveAsync();
        }

        public async Task UpdateResearchData([FromBody] UpdateResearchDataModel updateResearchData)
        {
            switch (updateResearchData.Strategy)
            {
                case Strategy.SRS:
                    await UpdateSRSResearchData(updateResearchData);
                    break;

                case Strategy.BrunchBreak:
                    await UpdateBrunchBreakResearchData(updateResearchData);
                    break;

                case Strategy.Espresso:
                    await UpdateEspressoResearchData(updateResearchData);
                    break;
            }
        }

        private async Task UpdateEspressoResearchData(UpdateResearchDataModel updateResearchData)
        {
            Espresso espresso = JsonConvert.DeserializeObject<Espresso>(updateResearchData.Data!)!;
            await _unitOfWork.Espresso.UpdateAsync(espresso);
            await _unitOfWork.SaveAsync();
        }

        private async Task UpdateBrunchBreakResearchData(UpdateResearchDataModel updateResearchData)
        {
            BrunchBreak brunchBreak = JsonConvert.DeserializeObject<BrunchBreak>(updateResearchData.Data!)!;
            await _unitOfWork.BrunchBreak.UpdateAsync(brunchBreak);
            await _unitOfWork.SaveAsync();
        }

        private async Task UpdateSRSResearchData(UpdateResearchDataModel updateResearchData)
        {
            SRS srs = JsonConvert.DeserializeObject<SRS>(updateResearchData.Data!)!;
            await _unitOfWork.SRS.UpdateAsync(srs);
            await _unitOfWork.SaveAsync();
        }

        public async Task UpdateReviewAsync(Review review)
        {
            await _unitOfWork.Review.UpdateAsync(review);
            await _unitOfWork.SaveAsync();
        }

        public async Task UpdateJournalAsync(Journal journal)
        {
            await _unitOfWork.Journal.UpdateAsync(journal);
            await _unitOfWork.SaveAsync();
        }

        #region Helper Methods - General

        private async Task<List<SampleSize>> GetAllSampleSizes()
        {
            return [.. (await _unitOfWork.SampleSize
                .GetAllAsync(sampleSize => sampleSize.SampleSizeType != SampleSizeType.Research, includeProperties: "Review")
                ).OrderBy(sampleSize => sampleSize.Id)];
        }

        private async Task SetViewModel(Strategy strategy)
        {
            _tradesVM.CurrentSampleSize = _allSampleSizes.Where(sampleSize => sampleSize.Strategy == strategy).Last();
            _tradesVM.SampleSizes = _allSampleSizes;

            await SetCurrentTrade();
            await SetAvailableMenus(strategy);
        }

        // Overload that accepts a specific sample size ID
        private async Task SetViewModel(int sampleSizeId)
        {
            _tradesVM.CurrentSampleSize = _allSampleSizes.FirstOrDefault(sampleSize => sampleSize.Id == sampleSizeId)!;
            _tradesVM.SampleSizes = _allSampleSizes;

            await SetCurrentTrade();
            await SetAvailableMenus();
        }

        private async Task SetViewModel()
        {
            _tradesVM.CurrentSampleSize = _allSampleSizes.Last();
            _tradesVM.SampleSizes = _allSampleSizes;

            await SetCurrentTrade();
            await SetAvailableMenus();
        }


        private async Task SetCurrentTrade()
        {
            switch (_tradesVM.CurrentSampleSize.Strategy)
            {
                case Strategy.SRS:
                    await SetSRSCurrentTrade();
                    break;

                case Strategy.BrunchBreak:
                    await SetBrunchBreakCurrentTrade();
                    break;

                case Strategy.Espresso:
                    await SetEspressoCurrentTrade();
                    break;
                default:
                    throw new ArgumentException($"Strategy {_tradesVM.CurrentSampleSize.Strategy.ToString()} not implemented in {nameof(TradesService)}.{nameof(SetCurrentTrade)}");
            }
        }

        private async Task SetEspressoCurrentTrade()
        {
            _tradesVM.AllTradesInSampleSize = [.. (await _unitOfWork.Espresso.GetAllAsync(trade => trade.SampleSizeId == _tradesVM.CurrentSampleSize.Id,
                                                                                            includeProperties: "Journal")).OrderBy(t => t.Id).Cast<object>()];

            _tradesVM.CurrentTrade = _tradesVM.AllTradesInSampleSize.Last() as BaseTrade;
            _tradesVM.EspressoTrade = _tradesVM.AllTradesInSampleSize.Last() as Espresso;
        }

        private async Task SetBrunchBreakCurrentTrade()
        {
            _tradesVM.AllTradesInSampleSize = [.. (await _unitOfWork.BrunchBreak.GetAllAsync(trade => trade.SampleSizeId == _tradesVM.CurrentSampleSize.Id,
                                                                                            includeProperties: "Journal")).OrderBy(t => t.Id).Cast<object>()];

            _tradesVM.CurrentTrade = _tradesVM.AllTradesInSampleSize.Last() as BaseTrade;
            _tradesVM.BrunchBreakTrade = _tradesVM.AllTradesInSampleSize.Last() as BrunchBreak;
        }

        private async Task SetSRSCurrentTrade()
        {
            _tradesVM.AllTradesInSampleSize = [.. (await _unitOfWork.SRS.GetAllAsync(trade => trade.SampleSizeId == _tradesVM.CurrentSampleSize.Id,
                                                                                            includeProperties: "Journal")).OrderBy(t => t.Id).Cast<object>()];

            _tradesVM.CurrentTrade = _tradesVM.AllTradesInSampleSize.Last() as BaseTrade;
            _tradesVM.SRSTrade = _tradesVM.AllTradesInSampleSize.Last() as SRS;
        }

        private async Task SetAvailableMenus(Strategy strategy)
        {
            SetSampleSizeMenu(strategy);
            await SetTimeFrames();
            await SetStrategies();
            SortMenus();
        }

        private async Task SetAvailableMenus()
        {
            SetSampleSizeMenu();
            await SetTimeFrames();
            await SetStrategies();
            SortMenus();
        }

        private async Task SetStrategies()
        {
            var sampleSizes = (await GetAllSampleSizes()).Where(sampleSize => sampleSize.SampleSizeType == _tradesVM.CurrentSampleSize.SampleSizeType);
            foreach (SampleSize sampleSize in sampleSizes)
            {
                if (!_tradesVM.AvailableStrategies.Contains(sampleSize.Strategy))
                {
                    _tradesVM.AvailableStrategies.Add(sampleSize.Strategy);
                }
            }
        }

        private async Task SetTimeFrames()
        {
            var sampleSizesForCurrentStrategy = (await GetAllSampleSizes())
                .Where(sampleSize => sampleSize.Strategy == _tradesVM.CurrentSampleSize.Strategy &&
                       sampleSize.SampleSizeType == _tradesVM.CurrentSampleSize.SampleSizeType);

            foreach (SampleSize sampleSize in sampleSizesForCurrentStrategy)
            {
                if (!_tradesVM.AvailableTimeframes.Contains(sampleSize.TimeFrame))
                {
                    _tradesVM.AvailableTimeframes.Add(sampleSize.TimeFrame);
                }
            }
        }

        private void SetSampleSizeMenu(Strategy strategy)
        {
            _tradesVM.SampleSizes = [.. _allSampleSizes.Where(sampleSize => sampleSize.Strategy == strategy && sampleSize.TimeFrame == _tradesVM.CurrentSampleSize.TimeFrame)];
        }

        private void SetSampleSizeMenu()
        {
            _tradesVM.SampleSizes = [.. _allSampleSizes.Where(sampleSize => sampleSize.TimeFrame == _tradesVM.CurrentSampleSize.TimeFrame &&
                                                                            sampleSize.SampleSizeType == _tradesVM.CurrentSampleSize.SampleSizeType &&
                                                                            sampleSize.Strategy == _tradesVM.CurrentSampleSize.Strategy)];
        }

        private void SortMenus()
        {
            _tradesVM.AvailableTimeframes.Sort();
            _tradesVM.AvailableStrategies.Sort();
        }

        #endregion

        public async Task DeleteTrade(DeleteTradeRequestModel deleteTradeRequest)
        {
            await _deleteTradeService.DeleteTrade(deleteTradeRequest.Strategy, deleteTradeRequest.Id, webHostEnvironment.WebRootPath);
        }

        public async Task<List<string>> UploadScreenshotsAsync(int tradeId, IFormFile[] files)
        {
            // Get the trade to determine its folder path
            var trade = await _unitOfWork.BaseTrade.GetAsync(t => t.Id == tradeId) ?? throw new ArgumentException($"Trade with ID {tradeId} not found.");

            // Get the existing screenshots folder path from the first screenshot URL
            string tradeFolderPath;
            if (trade.ScreenshotsUrls!.Any())
            {
                // Extract the folder path from the first screenshot URL
                string firstScreenshotUrl = trade.ScreenshotsUrls![0];
                // Use Path.GetFullPath with normalized slashes for cross-platform compatibility
                string normalizedUrl = firstScreenshotUrl.Replace("\\", "/");
                var pathParts = new[] { webHostEnvironment.WebRootPath }.Concat(normalizedUrl.Split('/').Where(s => !string.IsNullOrEmpty(s))).ToArray();
                string fullPath = Path.Combine(pathParts);
                tradeFolderPath = Path.GetDirectoryName(fullPath)!;
            }
            else
            {
                throw new InvalidOperationException("Cannot upload screenshots to a trade that has no existing screenshots folder.");
            }

            // Save the files to disk
            var newScreenshotPaths = await SaveFilesToDiskAsync(webHostEnvironment.WebRootPath, tradeFolderPath, files);

            trade.ScreenshotsUrls.AddRange(newScreenshotPaths);

            await _unitOfWork.BaseTrade.UpdateAsync(trade);
            await _unitOfWork.SaveAsync();

            return trade.ScreenshotsUrls;
        }

        private async Task<List<string>> SaveFilesToDiskAsync(string webRootPath, string destinationPath, IFormFile[] files)
        {
            List<string> screenshotsPaths = [];

            try
            {
                foreach (IFormFile file in files)
                {
                    // The browser-supplied file name can contain path separators (either '/' or '\',
                    // regardless of the server's OS - a Windows client uploading to a Linux server can
                    // still send one). Path.GetFileName strips any directory portion so the file always
                    // lands inside destinationPath instead of wherever a crafted name points to.
                    string safeFileName = Path.GetFileName(file.FileName);
                    string filePath = Path.Combine(destinationPath, safeFileName);

                    using (Stream stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    string dbFilePath = Path.GetRelativePath(webRootPath, filePath).Replace("\\", "/");
                    screenshotsPaths.Add(dbFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in saving uploaded files: {ex.Message}");
                throw;
            }

            return screenshotsPaths;
        }
    }
}
