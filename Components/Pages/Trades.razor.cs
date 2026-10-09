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
using TradingTools.Blazor.Services.Calculation;
using TradingTools.Blazor.Services.Reviews;
using TradingTools.Blazor.Services.Screenshots;
using TradingTools.Blazor.Services.Settings;
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
        [Inject] private ISettingsService SettingsService { get; set; } = default!;
        [Inject] private IReviewService ReviewService { get; set; } = default!;
        [Inject] private ITradeScreenshotService ScreenshotService { get; set; } = default!;

        /// <summary>Opens this trade directly, e.g. from the Data check page (/trades?tradeId=123).</summary>
        [SupplyParameterFromQuery] public int? TradeId { get; set; }

        /// <summary>Opens this sample size (e.g. from the Reviews page: /trades?sampleSizeId=12&review=First).</summary>
        [SupplyParameterFromQuery] public int? SampleSizeId { get; set; }

        /// <summary>With <see cref="SampleSizeId"/>: the review to open, e.g. "First" or "Summary" (the Review tab opens).</summary>
        [SupplyParameterFromQuery] public string? Review { get; set; }

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
                if (CurrentBase is null || value is null) return;
                CurrentBase.Symbol = value.ToString();
                RecalculateOutcome(); // the spread depends on the symbol, and with add-ons it decides the outcome
            }
        }

        #region Validation and P&L

        // The spread of each instrument (Settings page): taken off every position's result.
        private SpreadTable _spreads = SpreadTable.None;

        /// <summary>The spread in points of the trade on screen's instrument; 0 when it has none.</summary>
        private double CurrentSpread => _spreads.For(CurrentBase?.Symbol);

        /// <summary>What the trade on screen won or lost with the spread taken off - the same calculation as the dashboard and the history.</summary>
        private TradeResultSummary? CurrentResult => CurrentBase is null ? null : TradeResults.Calculate(CurrentBase, CurrentSpread);

        /// <summary>Live validation of the trade on screen - the same rules as the Data check page.</summary>
        private IReadOnlyList<TradeValidationIssue> CurrentIssues =>
            CurrentBase is null ? [] : TradeValidator.Validate(CurrentBase, CurrentSpread);

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
            RecalculateOutcome();
        }

        private void OnExitPriceChanged(double? value)
        {
            if (CurrentBase is null) return;
            CurrentBase.ExitPrice = value;
            RecalculatePnl();
            RecalculateOutcome();
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

        /// <summary>The whole trade's result in euro (main position + add-ons); null without add-ons or while something is missing.</summary>
        private TradeNetResult? CurrentNet => CurrentBase is { AddOns.Count: > 0 } trade ? TradeNet.Calculate(trade, CurrentSpread) : null;

        /// <summary>
        /// The outcome of the trade on screen, worked out and never typed in (like an add-on's): from the entry price,
        /// exit price and direction; with add-ons it is the net result of the whole trade. Null while something it
        /// needs is missing.
        /// </summary>
        private EOutcome? DeterminedOutcome => CurrentBase is null ? null : TradeNet.DeterminedOutcome(CurrentBase, CurrentSpread);

        /// <summary>
        /// Sets the trade's Outcome to the determined one, as the prices, direction, amount, volumes and symbol are
        /// changed. While it can't be determined yet the Outcome is left as it is.
        /// </summary>
        private void RecalculateOutcome()
        {
            if (CurrentBase is not null && DeterminedOutcome is { } outcome)
                CurrentBase.Outcome = outcome;
        }

        private void AddAddOn()
        {
            if (CurrentBase is null) return;
            CurrentBase.AddOns.Add(TradeAddOns.NewFor(CurrentBase));
            RecalculateOutcome();
        }

        private void RemoveAddOn(TradeAddOn addOn)
        {
            CurrentBase?.AddOns.Remove(addOn);
            RecalculateOutcome();
        }

        private void OnAddOnEntryChanged(TradeAddOn addOn, double? value)
        {
            addOn.EntryPrice = value;
            RecalculateAddOnPnl(addOn);
            RecalculateOutcome();
        }

        private void OnAddOnExitChanged(TradeAddOn addOn, double? value)
        {
            addOn.ExitPrice = value;
            RecalculateAddOnPnl(addOn);
            RecalculateOutcome();
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

        #region Reviews

        /// <summary>Index of the Review tab.</summary>
        private const int ReviewTab = 3;

        /// <summary>The reviews still to do for the sample size on screen; null when there are none.</summary>
        private DueReview? _dueReview;

        /// <summary>The review panels that are open.</summary>
        private readonly HashSet<ReviewKind> _openReviews = [];

        private bool IsDue(ReviewKind kind) => _dueReview?.Reviews.Contains(kind) == true;

        /// <summary>
        /// A sample size is on screen (opened, or navigated to): work out its reviews and open the one that is to be
        /// done, so it is ready when you go to the Review tab.
        /// </summary>
        private async Task OnSampleSizeShownAsync()
        {
            await LoadDueReviewAsync();

            _openReviews.Clear();
            if (_dueReview is { Reviews.Count: > 0 } due) _openReviews.Add(due.Reviews[0]);
        }

        private async Task LoadDueReviewAsync() =>
            _dueReview = _vm is null ? null : await ReviewService.GetDueForSampleSizeAsync(_vm.CurrentSampleSize.Id);

        private void OpenReview(ReviewKind kind)
        {
            _openReviews.Clear();
            _openReviews.Add(kind);
        }

        private void SetReviewOpen(ReviewKind kind, bool open)
        {
            if (open) _openReviews.Add(kind);
            else _openReviews.Remove(kind);
        }

        #endregion

        protected override async Task OnInitializedAsync()
        {
            _spreads = await SettingsService.GetSpreadsAsync();

            if (TradeId is { } tradeId && await TradesService.GetSampleSizeIdOfTradeAsync(tradeId) is { } sampleSizeId)
            {
                _vm = await TradesService.LoadSampleSizeNumberAsync(sampleSizeId);
                ResetIndexes();
                await OnSampleSizeShownAsync();
                int index = _vm.AllTradesInSampleSize.FindIndex(t => t is BaseTrade trade && trade.Id == tradeId);
                if (index >= 0) _tradeIndex = index;
            }
            else if (SampleSizeId is { } reviewSampleSizeId && await TradesService.SampleSizeExistsAsync(reviewSampleSizeId))
            {
                _vm = await TradesService.LoadSampleSizeNumberAsync(reviewSampleSizeId);
                ResetIndexes();
                await OnSampleSizeShownAsync();

                // Coming from the Reviews page: the review that is to be done is open, on the Review tab.
                if (Enum.TryParse<ReviewKind>(Review, ignoreCase: true, out var kind))
                {
                    _activeTab = ReviewTab;
                    OpenReview(kind);
                }
            }
            else
            {
                _vm = await TradesService.InitializeTradesViewModelAsync();
                ResetIndexes();
                await OnSampleSizeShownAsync();
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
            await OnSampleSizeShownAsync();
            _loading = false;
        }

        private async Task NextSampleSize()
        {
            if (!CanGoNextSampleSize || _vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadSampleSizeNumberAsync(_vm.SampleSizes[SampleSizePosition + 1].Id);
            ResetIndexes();
            await OnSampleSizeShownAsync();
            _loading = false;
        }

        private async Task ChangeStrategy(Strategy strategy)
        {
            if (_vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadStrategyAsync(strategy, _vm.CurrentSampleSize.SampleSizeType);
            ResetIndexes();
            await OnSampleSizeShownAsync();
            _loading = false;
        }

        private async Task ChangeType(SampleSizeType type)
        {
            if (_vm is null) return;
            _loading = true;
            _vm = await TradesService.LoadTypeAsync(type, _vm.CurrentSampleSize.Strategy);
            ResetIndexes();
            await OnSampleSizeShownAsync();
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
            await OnSampleSizeShownAsync();
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
                        RecalculateOutcome(); // a trade saved from here always carries its determined outcome
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

                            // What is still to do may have changed; the panels stay as they are (the one you wrote stays open).
                            await LoadDueReviewAsync();
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

        /// <summary>
        /// Deletes the screenshot on show - its path from the trade and its file from the Screenshots folder - after
        /// asking. The screenshot is identified by its path, taken before the dialog opens.
        /// </summary>
        private async Task ConfirmDeleteScreenshotAsync()
        {
            if (CurrentBase?.ScreenshotsUrls is not { } urls || _screenshotIndex < 0 || _screenshotIndex >= urls.Count) return;

            int tradeId = CurrentBase.Id;
            int position = _screenshotIndex;
            string path = urls[position];
            string name = Path.GetFileName(path.Replace('\\', '/'));

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                $"Delete screenshot {position + 1} of {urls.Count}?",
                $"{name} is removed from this trade and deleted from the Screenshots folder. This cannot be undone.",
                yesText: "Delete", cancelText: "Cancel");

            if (confirmed != true) return;

            _saving = true;
            try
            {
                var result = await ScreenshotService.DeleteAsync(tradeId, path);

                // Only the trade that was on show is updated on screen (it is the one the screenshot belonged to).
                if (CurrentBase is not null && CurrentBase.Id == tradeId)
                {
                    CurrentBase.ScreenshotsUrls = result.Urls;
                    _screenshotIndex = Math.Max(0, Math.Min(position, result.Urls.Count - 1));
                }

                switch (result.File)
                {
                    case ScreenshotFileOutcome.Deleted:
                        Snackbar.Add("Screenshot deleted.", Severity.Success);
                        break;
                    case ScreenshotFileOutcome.WasMissing:
                        Snackbar.Add("Screenshot removed (its file was already gone).", Severity.Success);
                        break;
                    case ScreenshotFileOutcome.KeptBecauseShared:
                        Snackbar.Add("Screenshot removed from this trade. Its file is kept because another trade uses it.", Severity.Info);
                        break;
                    case ScreenshotFileOutcome.NotInScreenshotsFolder:
                        Snackbar.Add("Screenshot removed from this trade. Its file is outside the Screenshots folder, so it was not touched.", Severity.Warning);
                        break;
                    default:
                        Snackbar.Add("Screenshot removed from this trade, but its file could not be deleted.", Severity.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error while deleting the screenshot: {ex.Message}", Severity.Error);
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
                await OnSampleSizeShownAsync();
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
