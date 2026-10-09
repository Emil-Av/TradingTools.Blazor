using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Models.Trades;
using Models.ViewModels;
using MudBlazor;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Interfaces;
using TradingTools.Blazor.Services.Reviews;
using TradingTools.Blazor.Services.Screenshots;
using TradingTools.Blazor.Services.Settings;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class NewTrade
    {
        [Inject] private INewTradeService NewTradeService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;
        [Inject] private ISettingsService SettingsService { get; set; } = default!;
        [Inject] private IReviewService ReviewService { get; set; } = default!;
        [Inject] private NavigationManager Navigation { get; set; } = default!;

        private const long MaxFileSizeBytes = 10 * 1024 * 1024;

        private SampleSizeViewData _sizeData = new() { Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.DemoTrading };
        private readonly List<IBrowserFile> _uploadedFiles = [];

        private DateTime? _dateAsDateTime = DateTime.Now.Date;
        private ESymbol _symbol = ESymbol.DAX;
        private EDirection _direction = EDirection.Long;
        private double? _amount;
        private EOutcome _outcome = EOutcome.Win;
        private ETradeRating _rating = ETradeRating.A;

        private double? _entryPrice;
        private double? _stopPrice;
        private double? _exitPrice;
        private double? _maxPrice;

        private double? _pnl;

        private readonly List<TradeAddOn> _addOns = [];

        private ECandleType _candleType = ECandleType.Bullish;
        private bool _isInOvernightRange;
        private bool _isFlippedSwitch;

        private bool _saving;

        // The spread of each instrument (Settings page): taken off every position's result, so the outcome set
        // from the net result of a trade with add-ons agrees with the Trades page and the dashboard.
        private SpreadTable _spreads = SpreadTable.None;

        protected override async Task OnInitializedAsync()
        {
            _spreads = await SettingsService.GetSpreadsAsync();

            // A new trade starts on the default account (Settings): Demo Trading or Trade. It can still be changed above.
            _sizeData.SampleSizeType = await SettingsService.GetDefaultAccountAsync();
        }

        // See the matching comment in Trades.razor.cs: clicking the drop zone asks JS to click the
        // hidden <InputFile> directly rather than relying on a <label for="..."> to forward the click.
        private Task TriggerUpload() => JS.InvokeVoidAsync("triggerFileInputClick", "screenshotInput").AsTask();

        // Same rule as the Trades page: P&L = |exit - entry| in points, filled in once both prices are there.
        private void OnEntryPriceChanged(double? value)
        {
            _entryPrice = value;
            _pnl = TradePnl.Points(_entryPrice, _exitPrice) ?? _pnl;
            RecalculateOutcome();
        }

        private void OnExitPriceChanged(double? value)
        {
            _exitPrice = value;
            _pnl = TradePnl.Points(_entryPrice, _exitPrice) ?? _pnl;
            RecalculateOutcome();
        }

        // A new add-on starts with the trade's exit price: usually the whole position is closed at once.
        private void AddAddOn()
        {
            _addOns.Add(new TradeAddOn { ExitPrice = _exitPrice });
            RecalculateOutcome();
        }

        private void RemoveAddOn(TradeAddOn addOn)
        {
            _addOns.Remove(addOn);
            RecalculateOutcome();
        }

        private void OnAddOnEntryChanged(TradeAddOn addOn, double? value)
        {
            addOn.EntryPrice = value;
            addOn.PnL = TradePnl.Points(addOn.EntryPrice, addOn.ExitPrice) ?? addOn.PnL;
            RecalculateOutcome();
        }

        private void OnAddOnExitChanged(TradeAddOn addOn, double? value)
        {
            addOn.ExitPrice = value;
            addOn.PnL = TradePnl.Points(addOn.EntryPrice, addOn.ExitPrice) ?? addOn.PnL;
            RecalculateOutcome();
        }

        /// <summary>The whole trade's result in euro (main position + add-ons); null without add-ons or while something is missing.</summary>
        private TradeNetResult? Net => _addOns.Count > 0 ? TradeNet.Calculate(_direction, _entryPrice, _exitPrice, _amount, _addOns, _spreads.For(_symbol.ToString())) : null;

        /// <summary>
        /// The outcome, worked out and never typed in (like an add-on's): from the prices and direction, or the net
        /// result of the whole trade with add-ons. Null while something it needs is missing.
        /// </summary>
        private EOutcome? DeterminedOutcome =>
            TradeNet.DeterminedOutcome(_direction, _entryPrice, _exitPrice, _amount, _addOns, _spreads.For(_symbol.ToString()));

        /// <summary>Sets the outcome to the determined one; while it can't be determined yet it is left as it is.</summary>
        private void RecalculateOutcome()
        {
            if (DeterminedOutcome is { } outcome) _outcome = outcome;
        }

        private void OnSymbolChanged() => RecalculateOutcome(); // the spread decides the outcome of a trade with add-ons

        private void OnFilesSelected(InputFileChangeEventArgs e)
        {
            foreach (var file in e.GetMultipleFiles(20))
            {
                _uploadedFiles.Add(file);
            }

            // Shown, uploaded and saved by date, the oldest first - however they were picked or in what order.
            var sorted = ScreenshotOrder.OldestFirst(_uploadedFiles);
            _uploadedFiles.Clear();
            _uploadedFiles.AddRange(sorted);
        }

        private void RemoveFile(IBrowserFile file) => _uploadedFiles.Remove(file);

        // SRS is always traded on the 15M chart and Espresso on the 5M chart, so picking either one
        // sets its matching timeframe, and vice versa. Other timeframes don't imply a strategy.
        private void OnStrategyChanged(Strategy strategy)
        {
            _sizeData.Strategy = strategy;
            _sizeData.TimeFrame = strategy switch
            {
                Strategy.SRS => TimeFrame.M15,
                Strategy.Espresso => TimeFrame.M5,
                _ => _sizeData.TimeFrame
            };
        }

        private void OnTimeFrameChanged(TimeFrame timeFrame)
        {
            _sizeData.TimeFrame = timeFrame;
            _sizeData.Strategy = timeFrame switch
            {
                TimeFrame.M15 => Strategy.SRS,
                TimeFrame.M5 => Strategy.Espresso,
                _ => _sizeData.Strategy
            };
        }

        /// <summary>
        /// The trade just saved may have completed a block of 5 trades (or the whole sample size): then the review of it
        /// is ready, and a notification with a link to it stays on screen until it's dismissed or clicked. Reviews that are
        /// already waiting from earlier are not repeated here - the Reviews page and the menu count show those.
        /// </summary>
        private async Task NotifyIfReviewReadyAsync(BaseTrade? saved)
        {
            if (saved is null || saved.SampleSizeId == 0) return;

            var due = await ReviewService.GetDueForSampleSizeAsync(saved.SampleSizeId);
            if (due is null) return;

            var ready = ReviewSchedule.ReachedAt(due.TradeCount).Where(due.Reviews.Contains).ToList();
            if (ready.Count == 0) return;

            string what = string.Join(" and ", ready.Select(kind => ReviewSchedule.Label(kind).ToLowerInvariant()));
            string link = $"trades?sampleSizeId={due.SampleSizeId}&review={due.Reviews[0]}";

            Snackbar.Add($"Sample size {due.SampleSizeNumber} has {due.TradeCount} trades: the {what} {(ready.Count == 1 ? "is" : "are")} ready.", Severity.Info, config =>
            {
                config.RequireInteraction = true;
                config.Action = "Open review";
                config.OnClick = _ =>
                {
                    Navigation.NavigateTo(link);
                    return Task.CompletedTask;
                };
            });
        }

        private async Task SaveAsync()
        {
            if (_uploadedFiles.Count == 0)
            {
                Snackbar.Add("No screenshots uploaded.", Severity.Error);
                return;
            }

            _saving = true;
            try
            {
                RecalculateOutcome();
                var date = DateOnly.FromDateTime(_dateAsDateTime ?? DateTime.Now);
                var vm = new NewTradeVM { SampleSizeViewData = _sizeData };

                switch (_sizeData.Strategy)
                {
                    case Strategy.SRS:
                        vm.SRSTrade = BuildTrade<SRS>(date);
                        vm.SRSTrade.CandleType = _candleType;
                        vm.SRSTrade.IsInOverNightRange = _isInOvernightRange;
                        vm.SRSTrade.IsFlippedTheSwitch = _isFlippedSwitch;
                        break;
                    case Strategy.Espresso:
                        vm.EspressoTrade = BuildTrade<Espresso>(date);
                        vm.EspressoTrade.CandleType = _candleType;
                        vm.EspressoTrade.IsInOverNightRange = _isInOvernightRange;
                        vm.EspressoTrade.IsFlippedTheSwitch = _isFlippedSwitch;
                        break;
                }

                var formFiles = await Task.WhenAll(_uploadedFiles.Select(f => BrowserFileFormFile.CreateAsync(f, MaxFileSizeBytes)));

                await NewTradeService.SaveTradeAsync(vm, formFiles.Cast<Microsoft.AspNetCore.Http.IFormFile>().ToArray());

                Snackbar.Add("Trade saved.", Severity.Success);
                await NotifyIfReviewReadyAsync((BaseTrade?)vm.SRSTrade ?? (BaseTrade?)vm.EspressoTrade ?? vm.BrunchBreakTrade);
                ClearForm();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error while saving the trade: {ex.Message}", Severity.Error);
            }
            finally
            {
                _saving = false;
            }
        }

        private T BuildTrade<T>(DateOnly date) where T : BaseTrade, new() => new()
        {
            Date = date,
            // Symbol stays a plain string column (existing free-text values aren't touched), so the
            // selected enum is saved as its name rather than the underlying int.
            Symbol = _symbol.ToString(),
            Direction = _direction,
            Amount = _amount,
            Outcome = _outcome,
            TradeRating = _rating,
            EntryPrice = _entryPrice,
            StopPrice = _stopPrice,
            ExitPrice = _exitPrice,
            MaxPrice = _maxPrice,
            PnL = _pnl,
            Status = EStatus.Closed,
            // Saved with the trade (EF inserts them along with it and sets their trade id).
            AddOns = [.. _addOns],
        };

        private void ClearForm()
        {
            _uploadedFiles.Clear();
            _dateAsDateTime = DateTime.Now.Date;
            _symbol = ESymbol.DAX;
            _direction = EDirection.Long;
            _amount = null;
            _outcome = EOutcome.Win;
            _rating = ETradeRating.A;
            _entryPrice = null;
            _stopPrice = null;
            _exitPrice = null;
            _maxPrice = null;
            _pnl = null;
            _addOns.Clear();
            _candleType = ECandleType.Bullish;
            _isInOvernightRange = false;
            _isFlippedSwitch = false;
        }
    }
}
