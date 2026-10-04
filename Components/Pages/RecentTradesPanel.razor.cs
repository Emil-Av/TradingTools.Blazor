using Microsoft.AspNetCore.Components;
using Shared;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class RecentTradesPanel
    {
        private const string All = "All";

        /// <summary>The page size that shows every trade on one page (MudBlazor's "All").</summary>
        public const int AllRows = int.MaxValue;

        /// <summary>
        /// The trades the grid works from, oldest first (one account, one or both strategies). A different
        /// list (the page's account or strategy switch changed) starts the filters over.
        /// </summary>
        [Parameter, EditorRequired] public IReadOnlyList<DashboardTrade> Trades { get; set; } = [];

        [Parameter] public string Title { get; set; } = "Recent trades";

        /// <summary>Rows per page to choose from; <see cref="AllRows"/> adds an "All" entry.</summary>
        [Parameter] public int[] PageSizes { get; set; } = [10, 20, 50];

        /// <summary>The page size to start with (one of <see cref="PageSizes"/>).</summary>
        [Parameter] public int InitialPageSize { get; set; } = 10;

        /// <summary>The strategy filter is only useful when the trades come from both strategies.</summary>
        [Parameter] public bool ShowStrategyFilter { get; set; } = true;

        private IReadOnlyList<DashboardTrade>? _trades;
        private bool _pageSizeSet;
        private int _rowsPerPage;

        private string _filterStrategy = All;
        private string _filterTimeFrame = All;
        private int _filterSampleSizeId; // 0 = all
        private PeriodFilter _filterPeriod = PeriodFilter.AllTime;
        private int _lastXTrades = 20;
        private DateTime? _filterFrom;
        private DateTime? _filterTo;
        private int _currentPage; // 0-based, bound to the grid's pager

        protected override void OnParametersSet()
        {
            if (!_pageSizeSet)
            {
                _rowsPerPage = InitialPageSize;
                _pageSizeSet = true;
            }

            if (!ReferenceEquals(_trades, Trades))
            {
                _trades = Trades;
                ResetFilters();
            }
        }

        private IEnumerable<string> StrategyOptions => Trades.Select(t => t.Strategy.ToString()).Distinct().Order();

        private IEnumerable<string> TimeFrameOptions => Trades.Select(t => t.TimeFrame).Distinct().Order().Select(tf => tf.ToString());

        /// <summary>Sample sizes matching the strategy/timeframe filters, oldest first.</summary>
        private IEnumerable<(int Id, string Label)> SampleSizeOptions => Trades
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
                var trades = Trades
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
    }
}
