using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Components.Pages
{
    public partial class Settings
    {
        [Inject] private ISettingsService SettingsService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;

        private bool _loading = true;
        private bool _busy;

        private SampleSizeType _defaultAccount = SettingsDefaults.Account;

        // What's typed in the fields (null while a field is empty).
        private readonly Dictionary<ESymbol, double?> _spreads = Enum.GetValues<ESymbol>().ToDictionary(s => s, _ => (double?)0);
        private readonly Dictionary<SampleSizeType, double?> _amounts = [];

        // What's saved: the resets of each account, for the list under its amount.
        private readonly Dictionary<SampleSizeType, AccountEquitySettings> _accounts = [];

        protected override async Task OnInitializedAsync()
        {
            _defaultAccount = await SettingsService.GetDefaultAccountAsync();
            var spreads = await SettingsService.GetSpreadsAsync();
            foreach (var symbol in Enum.GetValues<ESymbol>())
            {
                _spreads[symbol] = spreads.For(symbol.ToString());
            }

            foreach (var account in DashboardSelection.Accounts)
            {
                await LoadAccountAsync(account);
            }

            _loading = false;
        }

        private async Task SetDefaultAccountAsync(SampleSizeType account)
        {
            if (account == _defaultAccount) return;

            _busy = true;
            try
            {
                await SettingsService.SaveDefaultAccountAsync(account);
                _defaultAccount = account;
                Snackbar.Add($"Default account: {(account == SampleSizeType.DemoTrading ? "Demo" : "Real")}.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Saving failed: {ex.Message}", Severity.Error);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task LoadAccountAsync(SampleSizeType account)
        {
            var settings = await SettingsService.GetAccountAsync(account);
            _accounts[account] = settings;
            _amounts[account] = settings.Amount;
        }

        private async Task SaveAsync()
        {
            _busy = true;
            try
            {
                foreach (var account in DashboardSelection.Accounts)
                {
                    if (_amounts[account] is not > 0)
                    {
                        Snackbar.Add($"The {Shared.MyEnumConverter.TradeTypeFromEnum(account)} account amount must be more than 0.", Severity.Error);
                        return;
                    }
                }

                // An empty spread field counts as no spread.
                await SettingsService.SaveSpreadsAsync(_spreads.ToDictionary(pair => pair.Key, pair => pair.Value ?? 0));

                foreach (var account in DashboardSelection.Accounts)
                {
                    await SettingsService.SaveAccountAmountAsync(account, _amounts[account]!.Value);
                    await LoadAccountAsync(account);
                }

                foreach (var symbol in _spreads.Keys.ToList()) _spreads[symbol] ??= 0;
                Snackbar.Add("Settings saved.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Saving failed: {ex.Message}", Severity.Error);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task ResetAsync(SampleSizeType account)
        {
            if (_amounts[account] is not > 0)
            {
                Snackbar.Add("Enter the account amount the new curve should start at (more than 0).", Severity.Error);
                return;
            }

            double amount = _amounts[account]!.Value;
            string name = Shared.MyEnumConverter.TradeTypeFromEnum(account);

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                $"Reset the {name} equity curve?",
                $"A new curve starts at {DashboardFormat.Euro(amount)} and counts from your next trade. " +
                "Nothing is deleted: your trades stay, and \"All history\" on the dashboard still shows the old curve.",
                yesText: "Reset", cancelText: "Cancel");

            if (confirmed != true) return;

            _busy = true;
            try
            {
                await SettingsService.ResetEquityAsync(account, amount);
                await LoadAccountAsync(account);
                Snackbar.Add($"The {name} equity curve starts again at {DashboardFormat.Euro(amount)}.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Reset failed: {ex.Message}", Severity.Error);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
