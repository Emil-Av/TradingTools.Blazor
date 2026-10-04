using SharedEnums.Enums;

namespace TradingTools.Blazor.Services.Dashboard
{
    /// <summary>What the dashboard shows: one account, and either both strategies or a single one.</summary>
    public static class DashboardSelection
    {
        /// <summary>
        /// The accounts to choose from, whether or not they have trades yet. Each is a separate account
        /// starting from the account amount set on the Settings page. Paper trading is no longer used.
        /// </summary>
        public static readonly IReadOnlyList<SampleSizeType> Accounts = Settings.SettingsDefaults.Accounts;

        /// <summary>Demo trading is the account currently traded (and the New Trade page's default).</summary>
        public const SampleSizeType DefaultAccount = Settings.SettingsDefaults.Account;

        /// <summary>The strategies to choose from; null means SRS and Espresso together.</summary>
        public static readonly IReadOnlyList<Strategy?> Strategies = [null, Strategy.SRS, Strategy.Espresso];

        /// <summary>The account's trades, limited to <paramref name="strategy"/> unless it's null. Keeps the (chronological) order.</summary>
        public static List<DashboardTrade> Select(IEnumerable<DashboardTrade> trades, SampleSizeType account, Strategy? strategy) =>
            [.. trades.Where(t => t.AccountType == account && (strategy is null || t.Strategy == strategy))];

        /// <summary>"SRS &amp; Espresso", "SRS" or "Espresso".</summary>
        public static string StrategyLabel(Strategy? strategy) =>
            strategy is { } s ? Shared.MyEnumConverter.StrategyFromEnum(s) : "SRS & Espresso";
    }
}
