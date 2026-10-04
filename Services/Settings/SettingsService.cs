using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Services.Settings
{
    /// <summary>
    /// Reads and writes the settings tables (spreads, account amounts, equity resets) on the same scoped
    /// <see cref="ApplicationDbContext"/> as the rest of the app. Reads don't track what they load, and what a
    /// write touches is detached again afterwards, so a long-lived circuit never holds stale settings.
    /// </summary>
    public class SettingsService(ApplicationDbContext db, ITradeValidationMonitor validationMonitor, TimeProvider timeProvider) : ISettingsService
    {
        private readonly ApplicationDbContext _db = db;
        private readonly ITradeValidationMonitor _validationMonitor = validationMonitor;
        private readonly TimeProvider _timeProvider = timeProvider;

        public async Task<SampleSizeType> GetDefaultAccountAsync(CancellationToken cancellationToken = default)
        {
            var preference = await _db.AppPreferences.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
            return preference?.DefaultAccount ?? SettingsDefaults.Account;
        }

        public async Task SaveDefaultAccountAsync(SampleSizeType account, CancellationToken cancellationToken = default)
        {
            if (!SettingsDefaults.Accounts.Contains(account))
                throw new ArgumentOutOfRangeException(nameof(account), "The default account is either Demo Trading or Trade.");

            var preference = await _db.AppPreferences.OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
            if (preference is null) _db.AppPreferences.Add(new AppPreference { DefaultAccount = account });
            else preference.DefaultAccount = account;

            await SaveAndDetachAsync(cancellationToken);

            // The Data check covers the default account's trades, so it has to look again.
            _validationMonitor.RequestValidation();
        }

        public async Task<SpreadTable> GetSpreadsAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _db.InstrumentSpreads.AsNoTracking().ToListAsync(cancellationToken);
            return new SpreadTable(rows.Select(r => KeyValuePair.Create(r.Symbol, r.Spread)));
        }

        public async Task SaveSpreadsAsync(IReadOnlyDictionary<ESymbol, double> spreads, CancellationToken cancellationToken = default)
        {
            foreach (var (symbol, spread) in spreads)
            {
                if (!double.IsFinite(spread) || spread < 0)
                    throw new ArgumentOutOfRangeException(nameof(spreads), $"The spread of {symbol} must be 0 or more.");
            }

            var saved = await _db.InstrumentSpreads.ToListAsync(cancellationToken);
            foreach (var (symbol, spread) in spreads)
            {
                string name = MyEnumConverter.SymbolFromEnum(symbol);
                var row = saved.FirstOrDefault(r => r.Symbol == name);
                if (row is null) _db.InstrumentSpreads.Add(new InstrumentSpread { Symbol = name, Spread = spread });
                else row.Spread = spread;
            }

            await SaveAndDetachAsync(cancellationToken);

            // Every trade's result (and so what the Data check judges) depends on the spreads.
            _validationMonitor.RequestValidation();
        }

        public async Task<AccountEquitySettings> GetAccountAsync(SampleSizeType account, CancellationToken cancellationToken = default)
        {
            var setting = await _db.AccountSettings.AsNoTracking().FirstOrDefaultAsync(a => a.AccountType == account, cancellationToken);
            var resets = await _db.EquityResets.AsNoTracking()
                .Where(r => r.AccountType == account)
                .OrderBy(r => r.ResetAt).ThenBy(r => r.Id)
                .ToListAsync(cancellationToken);

            return new AccountEquitySettings(
                account,
                setting?.AccountAmount ?? AccountEquitySettings.DefaultAmount,
                [.. resets.Select(r => new EquityResetInfo(DateTime.SpecifyKind(r.ResetAt, DateTimeKind.Utc), r.StartingBalance, r.PreviousStartingBalance))]);
        }

        public async Task SaveAccountAmountAsync(SampleSizeType account, double amount, CancellationToken cancellationToken = default)
        {
            CheckAmount(amount);

            await UpsertAmountAsync(account, amount, cancellationToken);
            await SaveAndDetachAsync(cancellationToken);
        }

        public async Task ResetEquityAsync(SampleSizeType account, double amount, CancellationToken cancellationToken = default)
        {
            CheckAmount(amount);

            // The amount the ending curve started from is kept on the reset, so it can still be drawn.
            var previous = (await GetAccountAsync(account, cancellationToken)).Amount;

            _db.EquityResets.Add(new EquityReset
            {
                AccountType = account,
                ResetAt = _timeProvider.GetUtcNow().UtcDateTime,
                StartingBalance = amount,
                PreviousStartingBalance = previous,
            });
            await UpsertAmountAsync(account, amount, cancellationToken);

            // Both in one save: the reset and the new amount, or neither.
            await SaveAndDetachAsync(cancellationToken);
        }

        private async Task UpsertAmountAsync(SampleSizeType account, double amount, CancellationToken cancellationToken)
        {
            var row = await _db.AccountSettings.FirstOrDefaultAsync(a => a.AccountType == account, cancellationToken);
            if (row is null) _db.AccountSettings.Add(new AccountSetting { AccountType = account, AccountAmount = amount });
            else row.AccountAmount = amount;
        }

        private static void CheckAmount(double amount)
        {
            if (!double.IsFinite(amount) || amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "The account amount must be more than 0.");
        }

        private async Task SaveAndDetachAsync(CancellationToken cancellationToken)
        {
            var touched = _db.ChangeTracker.Entries()
                .Where(e => e.Entity is InstrumentSpread or AccountSetting or EquityReset or AppPreference)
                .ToList();

            await _db.SaveChangesAsync(cancellationToken);

            foreach (var entry in touched) entry.State = EntityState.Detached;
        }
    }
}
