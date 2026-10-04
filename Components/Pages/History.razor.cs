using Microsoft.AspNetCore.Components;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Interfaces;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Components.Pages
{
    /// <summary>
    /// Every closed SRS and Espresso trade of one account in the Recent trades grid of the dashboard (same
    /// filters, same columns, same summary line), with 50, 100 or 200 rows per page or all of them.
    /// </summary>
    public partial class History
    {
        [Inject] private IDashboardService DashboardService { get; set; } = default!;
        [Inject] private ISettingsService SettingsService { get; set; } = default!;

        private static readonly int[] PageSizes = [50, 100, 200, RecentTradesPanel.AllRows];

        private bool _loading = true;

        // Every SRS/Espresso trade, oldest first; the page shows one account at a time, with both strategies or one.
        private List<DashboardTrade> _allTrades = [];
        private SampleSizeType _account = DashboardSelection.DefaultAccount;
        private Strategy? _strategy; // null = SRS and Espresso together

        /// <summary>The selected account's trades of the selected strategy.</summary>
        private List<DashboardTrade> _trades = [];

        protected override async Task OnInitializedAsync()
        {
            _allTrades = await DashboardService.GetTradesAsync();
            // The page opens on the default account (Settings); the switch in the heading still lets you look at the other one.
            _account = await SettingsService.GetDefaultAccountAsync();
            Refresh();
            _loading = false;
        }

        private void Refresh() => _trades = DashboardSelection.Select(_allTrades, _account, _strategy);
    }
}
