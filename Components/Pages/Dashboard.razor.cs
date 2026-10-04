using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shared;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Interfaces;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class Dashboard
    {
        [Inject] private IDashboardService DashboardService { get; set; } = default!;

        private const string All = "All";
        private static readonly int[] PageSizes = [10, 20, 50];

        private bool _loading = true;

        // Every SRS/Espresso trade, oldest first. The dashboard shows one account at a time (each is a
        // separate account starting from DashboardStats.StartingBalance), with both strategies or one.
        private List<DashboardTrade> _allTrades = [];
        private SampleSizeType _account = DashboardSelection.DefaultAccount;
        private Strategy? _strategy; // null = SRS and Espresso together

        /// <summary>The selected account's trades of the selected strategy - everything on the page is based on these.</summary>
        private List<DashboardTrade> _accountTrades = [];

        private DashboardStats.Summary? _summary;
        private List<DashboardStats.EquityPoint> _equity = [];
        private double _maxDrawdown;
        private List<DashboardStats.Breakdown> _byStrategy = [];
        private List<DashboardStats.Breakdown> _bySymbol = [];

        // Recent trades filters (they only affect the grid, not the stats above it).
        private string _filterStrategy = All;
        private string _filterTimeFrame = All;
        private int _filterSampleSizeId; // 0 = all
        private PeriodFilter _filterPeriod = PeriodFilter.AllTime;
        private int _lastXTrades = 20;
        private DateTime? _filterFrom;
        private DateTime? _filterTo;
        private int _currentPage; // 0-based, bound to the grid's pager

        protected override async Task OnInitializedAsync()
        {
            _allTrades = await DashboardService.GetTradesAsync();
            Refresh();
            _loading = false;
        }

        private void SelectAccount(SampleSizeType account)
        {
            _account = account;
            Refresh();
        }

        private void SelectStrategy(Strategy? strategy)
        {
            _strategy = strategy;
            Refresh();
        }

        private void Refresh()
        {
            _accountTrades = DashboardSelection.Select(_allTrades, _account, _strategy);

            _summary = DashboardStats.Summarize(_accountTrades);
            _equity = DashboardStats.EquityCurve(_accountTrades);
            _maxDrawdown = DashboardStats.MaxDrawdown(_equity);
            _byStrategy = DashboardStats.BreakdownBy(_accountTrades, t => MyEnumConverter.StrategyFromEnum(t.Strategy));
            _bySymbol = DashboardStats.BreakdownBy(_accountTrades, t => t.Symbol);
            ResetFilters();
        }

        private static string StreakLabel(DashboardStats.Streak streak) => streak.Outcome switch
        {
            EOutcome.Win => $"{streak.Length}W",
            EOutcome.Loss => $"{streak.Length}L",
            _ => "—"
        };

        /// <summary>Net result as a share of the starting balance, e.g. "+4.2%".</summary>
        private static string ReturnLabel(double netEuro)
        {
            double ratio = netEuro / DashboardStats.StartingBalance;
            return $"{(ratio >= 0 ? "+" : "−")}{DashboardFormat.Percent(Math.Abs(ratio))}";
        }

        #region Recent trades

        private IEnumerable<string> StrategyOptions => _accountTrades.Select(t => t.Strategy.ToString()).Distinct().Order();

        private IEnumerable<string> TimeFrameOptions => _accountTrades.Select(t => t.TimeFrame).Distinct().Order().Select(tf => tf.ToString());

        /// <summary>Sample sizes matching the strategy/timeframe filters, oldest first.</summary>
        private IEnumerable<(int Id, string Label)> SampleSizeOptions => _accountTrades
            .Where(MatchesStrategyAndTimeFrame)
            .DistinctBy(t => t.SampleSizeId)
            .OrderBy(t => t.Strategy).ThenBy(t => t.TimeFrame).ThenBy(t => t.SampleSizeNumber)
            .Select(t => (t.SampleSizeId, $"{MyEnumConverter.StrategyFromEnum(t.Strategy)} {MyEnumConverter.TimeFrameFromEnum(t.TimeFrame)} · #{t.SampleSizeNumber}"));

        private bool MatchesStrategyAndTimeFrame(DashboardTrade t) =>
            (_filterStrategy == All || t.Strategy.ToString() == _filterStrategy)
            && (_filterTimeFrame == All || t.TimeFrame.ToString() == _filterTimeFrame);

        /// <summary>The grid's trades after all filters, oldest first.</summary>
        private List<DashboardTrade> FilteredTradesChronological
        {
            get
            {
                var trades = _accountTrades
                    .Where(MatchesStrategyAndTimeFrame)
                    .Where(t => _filterSampleSizeId == 0 || t.SampleSizeId == _filterSampleSizeId);

                return [.. RecentTradesFilter.Apply(trades, ToDateOnly(_filterFrom), ToDateOnly(_filterTo),
                    _filterPeriod, _lastXTrades, DateOnly.FromDateTime(DateTime.Today))];
            }
        }

        private void OnStrategyFilterChanged(string value)
        {
            _filterStrategy = value;
            DropSampleSizeFilterIfHidden();
            ResetPage();
        }

        private void OnTimeFrameFilterChanged(string value)
        {
            _filterTimeFrame = value;
            DropSampleSizeFilterIfHidden();
            ResetPage();
        }

        // A filter change starts the grid over at its first page; staying on e.g. page 4 would show
        // an arbitrary slice of the new result (or nothing, if it has fewer pages).
        private void ResetPage() => _currentPage = 0;

        // A selected sample size that no longer matches the strategy/timeframe filters would silently
        // empty the grid, so fall back to "all sample sizes" instead.
        private void DropSampleSizeFilterIfHidden()
        {
            if (_filterSampleSizeId != 0 && SampleSizeOptions.All(o => o.Id != _filterSampleSizeId))
            {
                _filterSampleSizeId = 0;
            }
        }

        private void ResetFilters()
        {
            _filterStrategy = All;
            _filterTimeFrame = All;
            _filterSampleSizeId = 0;
            _filterPeriod = PeriodFilter.AllTime;
            _lastXTrades = 20;
            _filterFrom = null;
            _filterTo = null;
            ResetPage();
        }

        private static DateOnly? ToDateOnly(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

        private static string PeriodLabel(PeriodFilter period) => period switch
        {
            PeriodFilter.LastWeek => "Last week",
            PeriodFilter.LastMonth => "Last month",
            PeriodFilter.LastXTrades => "Last X trades",
            _ => "All time"
        };

        private static string OutcomeChip(EOutcome outcome) => outcome switch
        {
            EOutcome.Win => "app-chip-green",
            EOutcome.Loss => "app-chip-red",
            _ => "app-chip-neutral"
        };

        #endregion
    }
}
