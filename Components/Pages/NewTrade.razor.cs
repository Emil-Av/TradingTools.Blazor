using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Models.Trades;
using Models.ViewModels;
using MudBlazor;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.Interfaces;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class NewTrade
    {
        [Inject] private INewTradeService NewTradeService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

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

        private ECandleType _candleType = ECandleType.Bullish;
        private bool _isInOvernightRange;
        private bool _isFlippedSwitch;

        private bool _saving;

        // See the matching comment in Trades.razor.cs: clicking the drop zone asks JS to click the
        // hidden <InputFile> directly rather than relying on a <label for="..."> to forward the click.
        private Task TriggerUpload() => JS.InvokeVoidAsync("triggerFileInputClick", "screenshotInput").AsTask();

        private void OnFilesSelected(InputFileChangeEventArgs e)
        {
            foreach (var file in e.GetMultipleFiles(20))
            {
                _uploadedFiles.Add(file);
            }
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
            _candleType = ECandleType.Bullish;
            _isInOvernightRange = false;
            _isFlippedSwitch = false;
        }
    }
}
