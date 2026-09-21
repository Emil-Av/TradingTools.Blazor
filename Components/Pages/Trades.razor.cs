using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Models.RequestModels;
using Models.Trades;
using Models.ViewModels;
using MudBlazor;
using Newtonsoft.Json;
using SharedEnums.Enums;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.Interfaces;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class Trades
    {
        [Inject] private ITradesService TradesService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;
        [Inject] private Ganss.Xss.IHtmlSanitizer HtmlSanitizer { get; set; } = default!;

        private const long MaxFileSizeBytes = 10 * 1024 * 1024;

        private TradesVM? _vm;
        private bool _loading = true;
        private bool _saving;
        private int _tradeIndex;
        private int _screenshotIndex;
        private int _activeTab;

        private object? CurrentTradeObj => _vm?.AllTradesInSampleSize.ElementAtOrDefault(_tradeIndex);
        private BaseTrade? CurrentBase => CurrentTradeObj as BaseTrade;
        private SRS? AsSRS => CurrentTradeObj as SRS;
        private BrunchBreak? AsBrunchBreak => CurrentTradeObj as BrunchBreak;
        private Espresso? AsEspresso => CurrentTradeObj as Espresso;

        private int SampleSizePosition => _vm is null ? 0 : _vm.SampleSizes.FindIndex(s => s.Id == _vm.CurrentSampleSize.Id);
        private bool CanGoPrevSampleSize => SampleSizePosition > 0;
        private bool CanGoNextSampleSize => _vm is not null && SampleSizePosition < _vm.SampleSizes.Count - 1;

        protected override async Task OnInitializedAsync()
        {
            _vm = await TradesService.InitializeTradesViewModelAsync();
            ResetIndexes();
            _loading = false;
        }

        private void ResetIndexes()
        {
            _tradeIndex = (_vm?.AllTradesInSampleSize.Count ?? 0) - 1;
            if (_tradeIndex < 0) _tradeIndex = 0;
            _screenshotIndex = 0;
        }

        private void PrevTrade() { if (_tradeIndex > 0) { _tradeIndex--; _screenshotIndex = 0; } }
        private void NextTrade() { if (_vm is not null && _tradeIndex < _vm.AllTradesInSampleSize.Count - 1) { _tradeIndex++; _screenshotIndex = 0; } }

        private void PrevScreenshot() { if (_screenshotIndex > 0) _screenshotIndex--; }
        private void NextScreenshot() { if (CurrentBase?.ScreenshotsUrls is { } urls && _screenshotIndex < urls.Count - 1) _screenshotIndex++; }

        private async Task PrevSampleSize()
        {
            if (!CanGoPrevSampleSize || _vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadSampleSizeNumberAsync(_vm.SampleSizes[SampleSizePosition - 1].Id);
            ResetIndexes();
            _loading = false;
        }

        private async Task NextSampleSize()
        {
            if (!CanGoNextSampleSize || _vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadSampleSizeNumberAsync(_vm.SampleSizes[SampleSizePosition + 1].Id);
            ResetIndexes();
            _loading = false;
        }

        private async Task ChangeStrategy(Strategy strategy)
        {
            if (_vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadStrategyAsync(strategy, _vm.CurrentSampleSize.SampleSizeType);
            ResetIndexes();
            _loading = false;
        }

        private async Task ChangeType(SampleSizeType type)
        {
            if (_vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadTypeAsync(type, _vm.CurrentSampleSize.Strategy);
            ResetIndexes();
            _loading = false;
        }

        private async Task ChangeTimeFrame(TimeFrame timeFrame)
        {
            if (_vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadTimeFrameAsync(new TradesLoadTimeFrameRequestModel
            {
                Strategy = _vm.CurrentSampleSize.Strategy,
                SampleSizeType = _vm.CurrentSampleSize.SampleSizeType,
                TimeFrame = timeFrame
            });
            ResetIndexes();
            _loading = false;
        }

        private string? Sanitize(string? html) => string.IsNullOrWhiteSpace(html) ? html : HtmlSanitizer.Sanitize(html);

        private async Task UpdateAsync()
        {
            if (_vm is null || CurrentBase is null) return;
            _saving = true;
            try
            {
                switch (_activeTab)
                {
                    case 0:
                        await TradesService.UpdateTradeDataAsync(CurrentBase);
                        break;
                    case 1:
                        await TradesService.UpdateResearchData(new UpdateResearchDataModel
                        {
                            Strategy = _vm.CurrentSampleSize.Strategy,
                            Data = JsonConvert.SerializeObject(CurrentTradeObj)
                        });
                        break;
                    case 2:
                        if (CurrentBase.Journal is { } journal)
                        {
                            journal.Pre = Sanitize(journal.Pre);
                            journal.During = Sanitize(journal.During);
                            journal.Exit = Sanitize(journal.Exit);
                            journal.Post = Sanitize(journal.Post);
                            await TradesService.UpdateJournalAsync(journal);
                        }
                        break;
                    case 3:
                        if (_vm.CurrentSampleSize.Review is { } review)
                        {
                            review.First = Sanitize(review.First);
                            review.Second = Sanitize(review.Second);
                            review.Third = Sanitize(review.Third);
                            review.Forth = Sanitize(review.Forth);
                            review.Summary = Sanitize(review.Summary);
                            await TradesService.UpdateReviewAsync(review);
                        }
                        break;
                }
                Snackbar.Add("Updated.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error while updating: {ex.Message}", Severity.Error);
            }
            finally
            {
                _saving = false;
            }
        }

        private async Task OnUploadMoreScreenshots(InputFileChangeEventArgs e)
        {
            if (CurrentBase is null) return;
            _saving = true;
            try
            {
                var files = e.GetMultipleFiles(20);
                var formFiles = await Task.WhenAll(files.Select(f => BrowserFileFormFile.CreateAsync(f, MaxFileSizeBytes)));
                var updatedUrls = await TradesService.UploadScreenshotsAsync(CurrentBase.Id, formFiles.Cast<Microsoft.AspNetCore.Http.IFormFile>().ToArray());
                CurrentBase.ScreenshotsUrls = updatedUrls;
                Snackbar.Add("Screenshots uploaded.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error while uploading: {ex.Message}", Severity.Error);
            }
            finally
            {
                _saving = false;
            }
        }

        private async Task ConfirmDeleteAsync()
        {
            if (_vm is null || CurrentBase is null) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Are you sure?",
                "All data including screenshots will be gone.",
                yesText: "Yes", cancelText: "Cancel");

            if (confirmed != true) return;

            _saving = true;
            try
            {
                await TradesService.DeleteTrade(new DeleteTradeRequestModel { Id = CurrentBase.Id, Strategy = _vm.CurrentSampleSize.Strategy });
                Snackbar.Add("Trade deleted.", Severity.Success);
                _vm = await TradesService.InitializeTradesViewModelAsync();
                ResetIndexes();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error while deleting: {ex.Message}", Severity.Error);
            }
            finally
            {
                _saving = false;
            }
        }
    }
}
