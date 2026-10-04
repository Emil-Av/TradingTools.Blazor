using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Settings;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Tests.Spreads
{
    /// <summary>The settings service against an in-memory database: spreads, account amounts and equity resets.</summary>
    public class SettingsServiceTests : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        private readonly ApplicationDbContext _db;
        private readonly ITradeValidationMonitor _monitor = Substitute.For<ITradeValidationMonitor>();
        private readonly FakeTimeProvider _time = new(Now);
        private readonly SettingsService _service;

        public SettingsServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"settings-{Guid.NewGuid()}")
                .Options;
            _db = new ApplicationDbContext(options);
            _service = new SettingsService(_db, _monitor, _time);
        }

        public void Dispose() => _db.Dispose();

        #region Default account

        [Fact]
        public async Task Until_one_is_chosen_the_default_account_is_demo()
        {
            (await _service.GetDefaultAccountAsync()).Should().Be(SampleSizeType.DemoTrading);
        }

        [Theory]
        [InlineData(SampleSizeType.Trade)]
        [InlineData(SampleSizeType.DemoTrading)]
        public async Task The_chosen_default_account_is_read_back(SampleSizeType account)
        {
            await _service.SaveDefaultAccountAsync(account);

            (await _service.GetDefaultAccountAsync()).Should().Be(account);
        }

        [Fact]
        public async Task Switching_back_and_forth_keeps_a_single_preference()
        {
            await _service.SaveDefaultAccountAsync(SampleSizeType.Trade);
            await _service.SaveDefaultAccountAsync(SampleSizeType.DemoTrading);
            await _service.SaveDefaultAccountAsync(SampleSizeType.Trade);

            (await _db.AppPreferences.CountAsync()).Should().Be(1);
            (await _service.GetDefaultAccountAsync()).Should().Be(SampleSizeType.Trade);
        }

        [Theory]
        [InlineData(SampleSizeType.Research)]
        [InlineData(SampleSizeType.PaperTrade)]
        [InlineData((SampleSizeType)99)]
        public async Task Only_demo_trading_or_trade_can_be_the_default(SampleSizeType account)
        {
            await _service.SaveDefaultAccountAsync(SampleSizeType.Trade);

            await FluentActions.Awaiting(() => _service.SaveDefaultAccountAsync(account)).Should().ThrowAsync<ArgumentOutOfRangeException>();

            (await _service.GetDefaultAccountAsync()).Should().Be(SampleSizeType.Trade, "a wrong value changes nothing");
        }

        [Fact]
        public async Task Changing_the_default_account_has_the_data_check_run_again()
        {
            await _service.SaveDefaultAccountAsync(SampleSizeType.Trade);

            _monitor.Received(1).RequestValidation();
        }

        #endregion

        #region Spreads

        [Fact]
        public async Task Nothing_saved_means_no_spread()
        {
            (await _service.GetSpreadsAsync()).For("DAX").Should().Be(0);
        }

        [Fact]
        public async Task Saved_spreads_are_read_back_per_instrument()
        {
            await _service.SaveSpreadsAsync(new Dictionary<ESymbol, double> { [ESymbol.DAX] = 1.2, [ESymbol.US500] = 0.5, [ESymbol.NASDAQ] = 0 });

            var spreads = await _service.GetSpreadsAsync();
            spreads.For("DAX").Should().Be(1.2);
            spreads.For("US500").Should().Be(0.5);
            spreads.For("NASDAQ").Should().Be(0);
        }

        [Fact]
        public async Task Saving_again_updates_the_same_rows()
        {
            await _service.SaveSpreadsAsync(new Dictionary<ESymbol, double> { [ESymbol.DAX] = 1.2 });
            await _service.SaveSpreadsAsync(new Dictionary<ESymbol, double> { [ESymbol.DAX] = 2.5 });

            (await _service.GetSpreadsAsync()).For("DAX").Should().Be(2.5);
            (await _db.InstrumentSpreads.CountAsync()).Should().Be(1, "one row per instrument");
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public async Task A_spread_must_be_zero_or_more(double spread)
        {
            var save = () => _service.SaveSpreadsAsync(new Dictionary<ESymbol, double> { [ESymbol.DAX] = spread });

            await save.Should().ThrowAsync<ArgumentOutOfRangeException>();
            (await _db.InstrumentSpreads.CountAsync()).Should().Be(0, "nothing is saved when one value is wrong");
        }

        [Fact]
        public async Task Saving_spreads_has_the_trades_checked_again()
        {
            await _service.SaveSpreadsAsync(new Dictionary<ESymbol, double> { [ESymbol.DAX] = 1 });

            _monitor.Received(1).RequestValidation();
        }

        #endregion

        #region Account amounts

        [Fact]
        public async Task An_account_without_a_saved_amount_has_the_old_2000()
        {
            var account = await _service.GetAccountAsync(SampleSizeType.DemoTrading);

            account.Amount.Should().Be(2000);
            account.Resets.Should().BeEmpty();
        }

        [Fact]
        public async Task The_account_amount_is_saved_per_account()
        {
            await _service.SaveAccountAmountAsync(SampleSizeType.DemoTrading, 5000);
            await _service.SaveAccountAmountAsync(SampleSizeType.Trade, 800);
            await _service.SaveAccountAmountAsync(SampleSizeType.DemoTrading, 5500);

            (await _service.GetAccountAsync(SampleSizeType.DemoTrading)).Amount.Should().Be(5500);
            (await _service.GetAccountAsync(SampleSizeType.Trade)).Amount.Should().Be(800);
            (await _db.AccountSettings.CountAsync()).Should().Be(2);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        [InlineData(double.NaN)]
        public async Task An_account_amount_must_be_more_than_zero(double amount)
        {
            (await FluentActions.Awaiting(() => _service.SaveAccountAmountAsync(SampleSizeType.DemoTrading, amount))
                .Should().ThrowAsync<ArgumentOutOfRangeException>()).Which.ParamName.Should().Be("amount");
        }

        #endregion

        #region Resets

        [Fact]
        public async Task A_reset_records_the_new_amount_and_the_amount_the_ended_curve_started_from()
        {
            await _service.SaveAccountAmountAsync(SampleSizeType.DemoTrading, 2500);

            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3100);

            var account = await _service.GetAccountAsync(SampleSizeType.DemoTrading);
            account.Amount.Should().Be(3100, "the new curve starts at the amount entered");
            var reset = account.Resets.Should().ContainSingle().Subject;
            reset.StartingBalance.Should().Be(3100);
            reset.PreviousStartingBalance.Should().Be(2500, "so the old curve can still be drawn");
            reset.ResetAtUtc.Should().Be(Now.UtcDateTime);
            reset.ResetAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Fact]
        public async Task A_first_reset_without_a_saved_amount_remembers_the_default_start()
        {
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3000);

            (await _service.GetAccountAsync(SampleSizeType.DemoTrading)).Resets.Single().PreviousStartingBalance.Should().Be(2000);
        }

        [Fact]
        public async Task Resets_pile_up_oldest_first_and_none_is_ever_removed()
        {
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3000);
            _time.Advance(TimeSpan.FromDays(10));
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 4000);
            _time.Advance(TimeSpan.FromDays(10));
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3500);

            var account = await _service.GetAccountAsync(SampleSizeType.DemoTrading);

            account.Resets.Select(r => r.StartingBalance).Should().Equal(3000, 4000, 3500);
            account.Resets.Select(r => r.PreviousStartingBalance).Should().Equal(2000, 3000, 4000);
            account.Resets.Should().BeInAscendingOrder(r => r.ResetAtUtc);
        }

        [Fact]
        public async Task A_reset_only_concerns_its_own_account()
        {
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3000);

            var other = await _service.GetAccountAsync(SampleSizeType.Trade);

            other.Resets.Should().BeEmpty();
            other.Amount.Should().Be(2000);
        }

        [Fact]
        public async Task Changing_the_amount_afterwards_does_not_touch_the_recorded_reset()
        {
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3000);
            await _service.SaveAccountAmountAsync(SampleSizeType.DemoTrading, 3600);

            var account = await _service.GetAccountAsync(SampleSizeType.DemoTrading);

            account.Amount.Should().Be(3600);
            account.Resets.Single().StartingBalance.Should().Be(3000);
        }

        [Fact]
        public async Task A_reset_with_a_wrong_amount_changes_nothing()
        {
            await _service.SaveAccountAmountAsync(SampleSizeType.DemoTrading, 2500);

            await FluentActions.Awaiting(() => _service.ResetEquityAsync(SampleSizeType.DemoTrading, 0)).Should().ThrowAsync<ArgumentOutOfRangeException>();

            var account = await _service.GetAccountAsync(SampleSizeType.DemoTrading);
            account.Resets.Should().BeEmpty();
            account.Amount.Should().Be(2500);
        }

        [Fact]
        public async Task The_service_does_not_keep_what_it_saved_tracked()
        {
            await _service.SaveDefaultAccountAsync(SampleSizeType.Trade);
            await _service.ResetEquityAsync(SampleSizeType.DemoTrading, 3000);
            await _service.SaveSpreadsAsync(new Dictionary<ESymbol, double> { [ESymbol.DAX] = 1 });

            // A long-lived circuit shares the context: stale tracked settings would hide later changes.
            _db.ChangeTracker.Entries().Should().BeEmpty();
        }

        #endregion
    }
}
