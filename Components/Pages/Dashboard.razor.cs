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

        // Every SRS/Espresso trade, oldest first; the dashboard shows one account type at a time,
        // since each (demo, paper, live) is a separate account starting from DashboardStats.StartingBalance.
        private List<DashboardTrade> _allTrades = [];
        private List<SampleSizeType> _accounts = [];
        private SampleSizeType _account;
        private List<DashboardTrade> _accountTrades = [];

        private DashboardStats.Summary? _summary;
        private List<Kpi> _kpis = [];
        private List<DashboardStats.EquityPoint> _equity = [];
        private double _maxDrawdown;
        private List<ChartSeries<double>> _equitySeries = [];
        private string[] _equityLabels = [];
        private List<DashboardStats.Breakdown> _byStrategy = [];
        private List<DashboardStats.Breakdown> _bySymbol = [];

        private readonly ChartOptions _chartOptions = new() { ChartPalette = ["#19d472"] };

        // Recent trades filters (they only affect the grid, not the stats above it).
        private string _filterStrategy = All;
        private string _filterTimeFrame = All;
        private int _filterSampleSizeId; // 0 = all
        private PeriodFilter _filterPeriod = PeriodFilter.AllTime;
        private int _lastXTrades = 20;

        private enum PeriodFilter { AllTime, LastWeek, LastMonth, LastXTrades }

        protected override async Task OnInitializedAsync()
        {
            _allTrades = await DashboardService.GetTradesAsync();
            _accounts = [.. _allTrades.Select(t => t.AccountType).Distinct().OrderBy(a => a)];
            if (_accounts.Count > 0)
            {
                // Demo trading is the account currently traded (and the New Trade page's default).
                SelectAccount(_accounts.Contains(SampleSizeType.DemoTrading) ? SampleSizeType.DemoTrading : _accounts[0]);
            }
            _loading = false;
        }

        private void SelectAccount(SampleSizeType account)
        {
            _account = account;
            _accountTrades = [.. _allTrades.Where(t => t.AccountType == account)];

            _summary = DashboardStats.Summarize(_accountTrades);
            _equity = DashboardStats.EquityCurve(_accountTrades);
            _maxDrawdown = DashboardStats.MaxDrawdown(_equity);
            _byStrategy = DashboardStats.BreakdownBy(_accountTrades, t => MyEnumConverter.StrategyFromEnum(t.Strategy));
            _bySymbol = DashboardStats.BreakdownBy(_accountTrades, t => t.Symbol);
            _kpis = BuildKpis(_summary);
            BuildEquityChart();
            ResetFilters();
        }

        private List<Kpi> BuildKpis(DashboardStats.Summary s)
        {
            var streak = s.CurrentStreak;
            string streakValue = streak.Outcome switch
            {
                EOutcome.Win => $"{streak.Length} {(streak.Length == 1 ? "Win" : "Wins")}",
                EOutcome.Loss => $"{streak.Length} {(streak.Length == 1 ? "Loss" : "Losses")}",
                _ => "—"
            };
            double returnPct = s.NetEuro / DashboardStats.StartingBalance;

            return
            [
                new("WIN RATE", DashboardFormat.Percent(s.WinRate), $"{s.Wins}W · {s.Losses}L · {s.Breakevens} BE not counted",
                    Icons.Material.Filled.TrackChanges, s.WinRate < 0.5),
                new("NET P&L", DashboardFormat.SignedEuro(s.NetEuro), $"{(returnPct >= 0 ? "+" : "−")}{DashboardFormat.Percent(Math.Abs(returnPct))} on {DashboardFormat.Euro(DashboardStats.StartingBalance)} start",
                    Icons.Material.Filled.Euro, s.NetEuro < 0),
                new("AVG WIN", s.AvgWinPoints is { } w ? DashboardFormat.SignedPoints(w) : "—", "points per winning trade",
                    Icons.Material.Filled.TrendingUp, false),
                new("AVG LOSS", s.AvgLossPoints is { } l ? DashboardFormat.SignedPoints(l) : "—", "points per losing trade",
                    Icons.Material.Filled.TrendingDown, true),
                new("CURRENT STREAK", streakValue, $"Best {s.BestWinStreak}W · Worst {s.WorstLossStreak}L · BE ignored",
                    Icons.Material.Filled.LocalFireDepartment, streak.Outcome == EOutcome.Loss),
            ];
        }

        private void BuildEquityChart()
        {
            _equitySeries =
            [
                new ChartSeries<double> { Name = "Balance", Data = new ChartData<double>([.. _equity.Select(p => Math.Round(p.Balance, 2))]) }
            ];

            // Label about six evenly spaced points plus the last one, so the axis stays readable with
            // hundreds of trades. A regular label too close to the last one is dropped so they don't overlap.
            int count = _equity.Count;
            int last = count - 1;
            int step = Math.Max(1, count / 6);
            _equityLabels = [.. _equity.Select((point, index) =>
                index == last || (index % step == 0 && last - index >= step / 2)
                    ? point.Date?.ToString("dd MMM", CultureInfo.InvariantCulture) ?? "Start"
                    : string.Empty)];
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
                var today = DateOnly.FromDateTime(DateTime.Today);
                IEnumerable<DashboardTrade> trades = _accountTrades
                    .Where(MatchesStrategyAndTimeFrame)
                    .Where(t => _filterSampleSizeId == 0 || t.SampleSizeId == _filterSampleSizeId);

                trades = _filterPeriod switch
                {
                    PeriodFilter.LastWeek => trades.Where(t => t.Date >= today.AddDays(-7)),
                    PeriodFilter.LastMonth => trades.Where(t => t.Date >= today.AddMonths(-1)),
                    PeriodFilter.LastXTrades => trades.TakeLast(Math.Max(1, _lastXTrades)),
                    _ => trades
                };

                return [.. trades];
            }
        }

        private void OnStrategyFilterChanged(string value)
        {
            _filterStrategy = value;
            DropSampleSizeFilterIfHidden();
        }

        private void OnTimeFrameFilterChanged(string value)
        {
            _filterTimeFrame = value;
            DropSampleSizeFilterIfHidden();
        }

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
        }

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

        private record Kpi(string Label, string Value, string Sub, string Icon, bool IsNegative);
    }
}
