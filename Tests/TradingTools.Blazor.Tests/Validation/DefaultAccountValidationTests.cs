using Microsoft.Extensions.Time.Testing;
using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Tests.Validation
{
    /// <summary>The Data check covers the trades of the default account (Demo Trading or Trade) and nothing else.</summary>
    public class DefaultAccountValidationTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

        private static readonly SampleSize Demo = new() { Id = 1, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.DemoTrading };
        private static readonly SampleSize Real = new() { Id = 2, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.Trade };
        private static readonly SampleSize Research = new() { Id = 3, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.Research };

        private static BaseTrade Trade(int id, SampleSize sampleSize, int day, bool valid = true) => new SRS
        {
            Id = id, SampleSize = sampleSize, SampleSizeId = sampleSize.Id,
            Date = new DateOnly(2026, 9, day), CreatedAt = new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc),
            Symbol = "DAX", Direction = EDirection.Long, Outcome = EOutcome.Win, Amount = 1,
            EntryPrice = 100, StopPrice = 90, ExitPrice = 120, MaxPrice = 125,
            PnL = valid ? 20 : 19, // should be 20
        };

        private static Task<TradeValidationReport> Run(SampleSizeType defaultAccount, params BaseTrade[] trades) =>
            new TradeValidationService(
                TestUnitOfWork.Create([Demo, Real, Research], trades),
                TestSettings.WithDefaultAccount(defaultAccount),
                new FakeTimeProvider(Now)).ValidateAllAsync();

        // Demo: trades 1 (bad), 2, 3.  Real: trades 11 (bad), 12, 13 (bad).  Research: 21 (bad).
        private static readonly BaseTrade[] Trades =
        [
            Trade(1, Demo, 1, valid: false), Trade(2, Demo, 2), Trade(3, Demo, 3),
            Trade(11, Real, 4, valid: false), Trade(12, Real, 5), Trade(13, Real, 6, valid: false),
            Trade(21, Research, 7, valid: false),
        ];

        [Fact]
        public async Task With_demo_as_the_default_only_demo_trades_are_checked()
        {
            var report = await Run(SampleSizeType.DemoTrading, Trades);

            report.Account.Should().Be(SampleSizeType.DemoTrading);
            report.InvalidTrades.Select(t => t.TradeId).Should().Equal(1);
            report.InvalidTrades.Should().OnlyContain(t => t.AccountType == SampleSizeType.DemoTrading);
        }

        [Fact]
        public async Task With_real_as_the_default_only_real_trades_are_checked()
        {
            var report = await Run(SampleSizeType.Trade, Trades);

            report.Account.Should().Be(SampleSizeType.Trade);
            // 11 is wrong; 13 is wrong too but is the newest real trade, so it's skipped as possibly still in progress.
            report.InvalidTrades.Select(t => t.TradeId).Should().Equal(11);
            report.SkippedTradeId.Should().Be(13);
        }

        [Fact]
        public async Task The_checked_count_is_the_default_accounts_trades_minus_the_one_still_in_progress()
        {
            (await Run(SampleSizeType.DemoTrading, Trades)).TradesChecked.Should().Be(2, "3 demo trades, the newest is skipped");
            (await Run(SampleSizeType.Trade, Trades)).TradesChecked.Should().Be(2, "3 real trades, the newest is skipped");
        }

        [Fact]
        public async Task The_trade_in_progress_is_the_newest_of_the_default_account_not_of_all_accounts()
        {
            // The newest trade overall is a real one (day 6); with demo as the default, the newest demo trade (3) is skipped.
            var report = await Run(SampleSizeType.DemoTrading, Trades);

            report.SkippedTradeId.Should().Be(3);
        }

        [Fact]
        public async Task An_invalid_trade_of_the_other_account_is_not_reported()
        {
            // Trade 13 (real) is the newest real trade and would be skipped anyway; 11 is the real one that is wrong.
            var demoReport = await Run(SampleSizeType.DemoTrading, Trades);

            demoReport.InvalidTrades.Select(t => t.TradeId).Should().NotContain([11, 13]);
        }

        [Fact]
        public async Task Research_trades_are_never_checked_whatever_the_default()
        {
            foreach (var account in new[] { SampleSizeType.DemoTrading, SampleSizeType.Trade })
            {
                (await Run(account, Trades)).InvalidTrades.Select(t => t.TradeId).Should().NotContain(21);
            }
        }

        [Fact]
        public async Task An_account_without_trades_has_an_empty_clean_report()
        {
            var report = await Run(SampleSizeType.Trade, Trade(1, Demo, 1, valid: false));

            report.TradesChecked.Should().Be(0);
            report.InvalidTrades.Should().BeEmpty();
            report.SkippedTradeId.Should().BeNull();
        }
    }
}
