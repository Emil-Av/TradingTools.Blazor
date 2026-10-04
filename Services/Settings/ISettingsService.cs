using Shared.Enums;
using SharedEnums.Enums;

namespace TradingTools.Blazor.Services.Settings
{
    /// <summary>One reset of an account's equity curve.</summary>
    /// <param name="ResetAtUtc">When it was made; trades created after it belong to the new curve.</param>
    /// <param name="StartingBalance">The balance the new curve starts at.</param>
    /// <param name="PreviousStartingBalance">The starting balance of the curve it ended.</param>
    public sealed record EquityResetInfo(DateTime ResetAtUtc, double StartingBalance, double PreviousStartingBalance);

    /// <summary>An account's equity settings: the account amount (where the current curve starts) and its resets, oldest first.</summary>
    public sealed record AccountEquitySettings(SampleSizeType Account, double Amount, IReadOnlyList<EquityResetInfo> Resets)
    {
        /// <summary>The account amount used while nothing was entered - what was once hard-coded.</summary>
        public const double DefaultAmount = 2000;

        public static AccountEquitySettings Default(SampleSizeType account) => new(account, DefaultAmount, []);
    }

    /// <summary>Defaults for what hasn't been set.</summary>
    public static class SettingsDefaults
    {
        /// <summary>The account the app works with until another is chosen: the demo account.</summary>
        public const SampleSizeType Account = SampleSizeType.DemoTrading;

        /// <summary>The accounts to choose from: Demo Trading and Trade (real). Paper trading is no longer used.</summary>
        public static readonly IReadOnlyList<SampleSizeType> Accounts = [SampleSizeType.Trade, SampleSizeType.DemoTrading];
    }

    /// <summary>The settings page's data: the default account, the spread per instrument, and the equity settings per account.</summary>
    public interface ISettingsService
    {
        /// <summary>
        /// The account the app works with: the Dashboard, the History, the Data check and the Trades pages show
        /// its trades. <see cref="SettingsDefaults.Account"/> until one is chosen.
        /// </summary>
        Task<SampleSizeType> GetDefaultAccountAsync(CancellationToken cancellationToken = default);

        /// <summary>Chooses the default account (Demo Trading or Trade). The Data check is run again for it.</summary>
        Task SaveDefaultAccountAsync(SampleSizeType account, CancellationToken cancellationToken = default);

        /// <summary>The spread of every instrument, in points.</summary>
        Task<SpreadTable> GetSpreadsAsync(CancellationToken cancellationToken = default);

        /// <summary>Saves the spread of each instrument (0 = none). Trades are re-checked afterwards.</summary>
        Task SaveSpreadsAsync(IReadOnlyDictionary<ESymbol, double> spreads, CancellationToken cancellationToken = default);

        /// <summary>The equity settings of one account; the default when nothing was saved yet.</summary>
        Task<AccountEquitySettings> GetAccountAsync(SampleSizeType account, CancellationToken cancellationToken = default);

        /// <summary>Saves the account amount: the starting point of the account's current equity curve.</summary>
        Task SaveAccountAmountAsync(SampleSizeType account, double amount, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a new equity curve for the account at <paramref name="amount"/>, counting from the next trade.
        /// Nothing is deleted: the old curve is kept and still shown by "All history".
        /// </summary>
        Task ResetEquityAsync(SampleSizeType account, double amount, CancellationToken cancellationToken = default);
    }
}
