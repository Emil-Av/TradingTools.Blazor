using Microsoft.AspNetCore.Components;
using Shared;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Interfaces;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class Dashboard
    {
        [Inject] private IDashboardService DashboardService { get; set; } = default!;
        [Inject] private ISettingsService SettingsService { get; set; } = default!;

        private bool _loading = true;

        // Every SRS/Espresso trade, oldest first. The dashboard shows one account at a time (each is a
        // separate account starting from its account amount on the Settings page), with both strategies or one.
        private List<DashboardTrade> _allTrades = [];
        private SampleSizeType _account = DashboardSelection.DefaultAccount;
        private Strategy? _strategy; // null = SRS and Espresso together

        /// <summary>The selected account's trades of the selected strategy - everything on the page is based on these.</summary>
        private List<DashboardTrade> _accountTrades = [];

        private DashboardStats.Summary? _summary;
        private List<DashboardStats.Breakdown> _byStrategy = [];
        private List<DashboardStats.Breakdown> _bySymbol = [];

        // The equity curve: which part of the history, and an optional date range. Only the curve (and the figures
        // printed with it) follows these; the key figures above and the grid below always cover every trade.
        private AccountEquitySettings _accountSettings = AccountEquitySettings.Default(DashboardSelection.DefaultAccount);
        private EquityScope _scope = EquityScope.SinceLastReset;
        private DateTime? _equityFrom;
        private DateTime? _equityTo;
        private EquityView _equityView = new([new DashboardStats.EquityPoint(null, AccountEquitySettings.DefaultAmount)], AccountEquitySettings.DefaultAmount, AccountEquitySettings.DefaultAmount, 0, 0, 0);

        protected override async Task OnInitializedAsync()
        {
            _allTrades = await DashboardService.GetTradesAsync();
            // The page opens on the default account (Settings); the switch in the heading still lets you look at the other one.
            _account = await SettingsService.GetDefaultAccountAsync();
            await RefreshAsync();
            _loading = false;
        }

        /// <summary>The account or strategy changed: everything on the page is worked out again.</summary>
        private async Task RefreshAsync()
        {
            _accountTrades = DashboardSelection.Select(_allTrades, _account, _strategy);
            _accountSettings = await SettingsService.GetAccountAsync(_account);

            _summary = DashboardStats.Summarize(_accountTrades, _accountSettings.Amount);
            _byStrategy = DashboardStats.BreakdownBy(_accountTrades, t => MyEnumConverter.StrategyFromEnum(t.Strategy));
            _bySymbol = DashboardStats.BreakdownBy(_accountTrades, t => t.Symbol);
            BuildEquity();
        }

        /// <summary>The curve (and its figures) for the chosen scope and date range.</summary>
        private void BuildEquity() =>
            _equityView = EquityCurves.Build(_accountTrades, _accountSettings, _scope, ToDateOnly(_equityFrom), ToDateOnly(_equityTo));

        private void SelectScope(EquityScope scope)
        {
            _scope = scope;
            BuildEquity();
        }

        private static DateOnly? ToDateOnly(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

        private static string StreakLabel(DashboardStats.Streak streak) => streak.Outcome switch
        {
            EOutcome.Win => $"{streak.Length}W",
            EOutcome.Loss => $"{streak.Length}L",
            _ => "—"
        };

        /// <summary>Net result as a share of the starting balance, e.g. "+4.2%".</summary>
        private static string ReturnLabel(double netEuro, double startBalance)
        {
            double ratio = startBalance > 0 ? netEuro / startBalance : 0;
            return $"{(ratio >= 0 ? "+" : "−")}{DashboardFormat.Percent(Math.Abs(ratio))}";
        }
    }
}
