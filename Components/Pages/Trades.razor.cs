using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Models.RequestModels;
using Models.Trades;
using Models.ViewModels;
using MudBlazor;
using Newtonsoft.Json;
using Shared;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Interfaces;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class Trades
    {
        [Inject] private ITradesService TradesService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;
        [Inject] private Ganss.Xss.IHtmlSanitizer HtmlSanitizer { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;
        [Inject] private ITradeValidationMonitor ValidationMonitor { get; set; } = default!;

        /// <summary>Opens this trade directly, e.g. from the Data check page (/trades?tradeId=123).</summary>
        [SupplyParameterFromQuery] public int? TradeId { get; set; }

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

        // Symbol is stored as plain text (existing free-typed values are left alone), so this maps it
        // to/from the ESymbol select. A missing or older value that isn't one of the 3 options shows as
        // empty (rather than pretending to be DAX), so it matches what the validation reports.
        private ESymbol? CurrentSymbol
        {
            get => CurrentBase?.Symbol is { } symbol && MyEnumConverter.SymbolFromString(symbol) is { Success: true } result
                ? result.Value
                : null;
            set
            {
                if (CurrentBase is not null && value is not null) CurrentBase.Symbol = value.ToString();
            }
        }

        #region Validation and P&L

        /// <summary>Live validation of the trade on screen - the same rules as the Data check page.</summary>
        private IReadOnlyList<TradeValidationIssue> CurrentIssues =>
            CurrentBase is null ? [] : TradeValidator.Validate(CurrentBase);

        /// <summary>The most recent trade isn't flagged on the Data check page since it may still be in progress.</summary>
        private bool CurrentIsMostRecentTrade =>
            CurrentBase is not null && ValidationMonitor.LatestReport?.SkippedTradeId == CurrentBase.Id;

        /// <summary>The problems with one field, for its error text; null when there are none.</summary>
        private string? IssueFor(string field)
        {
            var messages = CurrentIssues.Where(i => i.Field == field).Select(i => i.Message).ToList();
            return messages.Count == 0 ? null : string.Join(" ", messages);
        }

        private void OnEntryPriceChanged(double? value)
        {
            if (CurrentBase is null) return;
            CurrentBase.EntryPrice = value;
            RecalculatePnl();
        }

        private void OnExitPriceChanged(double? value)
        {
            if (CurrentBase is null) return;
            CurrentBase.ExitPrice = value;
            RecalculatePnl();
        }

        /// <summary>P&amp;L = |exit - entry|, as long as both prices are there; otherwise it's left as it is.</summary>
        private void RecalculatePnl()
        {
            if (CurrentBase is not null && TradePnl.Points(CurrentBase.EntryPrice, CurrentBase.ExitPrice) is { } points)
                CurrentBase.PnL = points;
        }

        #endregion

        #region Add-ons

        private string? AddOnIssue(int index, string property) => IssueFor(TradeValidator.AddOnField(index, property));

        private void AddAddOn() => CurrentBase?.AddOns.Add(TradeAddOns.NewFor(CurrentBase));

        private void RemoveAddOn(TradeAddOn addOn) => CurrentBase?.AddOns.Remove(addOn);

        private static void OnAddOnEntryChanged(TradeAddOn addOn, double? value)
        {
            addOn.EntryPrice = value;
            RecalculateAddOnPnl(addOn);
        }

        private static void OnAddOnExitChanged(TradeAddOn addOn, double? value)
        {
            addOn.ExitPrice = value;
            RecalculateAddOnPnl(addOn);
        }

        /// <summary>Same rule as the trade itself: |exit - entry| once both prices are there.</summary>
        private static void RecalculateAddOnPnl(TradeAddOn addOn)
        {
            if (TradePnl.Points(addOn.EntryPrice, addOn.ExitPrice) is { } points)
                addOn.PnL = points;
        }

        #endregion

        private int SampleSizePosition => _vm is null ? 0 : _vm.SampleSizes.FindIndex(s => s.Id == _vm.CurrentSampleSize.Id);
        private bool CanGoPrevSampleSize => SampleSizePosition > 0;
        private bool CanGoNextSampleSize => _vm is not null && SampleSizePosition < _vm.SampleSizes.Count - 1;

        protected override async Task OnInitializedAsync()
        {
            if (TradeId is { } tradeId && await TradesService.GetSampleSizeIdOfTradeAsync(tradeId) is { } sampleSizeId)
            {
                _vm = await TradesService.LoadSampleSizeNumberAsync(sampleSizeId);
                ResetIndexes();
                int index = _vm.AllTradesInSampleSize.FindIndex(t => t is BaseTrade trade && trade.Id == tradeId);
                if (index >= 0) _tradeIndex = index;
            }
            else
            {
                _vm = await TradesService.InitializeTradesViewModelAsync();
                ResetIndexes();
            }
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

        // A <label for="..."> wrapping a MudButton doesn't reliably forward its click to the hidden
        // <InputFile> (a nested interactive element breaks the browser's native label-forwarding), so
        // the button click asks JS to click the file input directly instead.
        private Task TriggerUpload() => JS.InvokeVoidAsync("triggerFileInputClick", "uploadMoreInput").AsTask();

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
