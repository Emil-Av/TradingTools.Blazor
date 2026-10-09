using Microsoft.Extensions.Time.Testing;
using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Tests.Validation
{
    /// <summary>Validating every trade: which trades, the skipped most recent one, and how they're located.</summary>
    public class TradeValidationServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

        private static SampleSize SampleSize(int id, Strategy strategy = Strategy.SRS, TimeFrame timeFrame = TimeFrame.M15, SampleSizeType type = SampleSizeType.DemoTrading) =>
            new() { Id = id, Strategy = strategy, TimeFrame = timeFrame, SampleSizeType = type };

        /// <summary>A valid trade, dated <paramref name="day"/> days into September 2026.</summary>
        private static BaseTrade Valid(int id, SampleSize sampleSize, int day, int createdMinute = 0)
        {
            BaseTrade trade = sampleSize.Strategy == Strategy.Espresso ? new Espresso() : new SRS();
            trade.Id = id;
            trade.SampleSize = sampleSize;
            trade.SampleSizeId = sampleSize.Id;
            trade.Date = new DateOnly(2026, 9, day);
            trade.CreatedAt = new DateTime(2026, 9, day, 12, createdMinute, 0, DateTimeKind.Utc);
            trade.Symbol = "DAX";
            trade.Direction = EDirection.Long;
            trade.Outcome = EOutcome.Win;
            trade.Amount = 1;
            trade.EntryPrice = 100;
            trade.StopPrice = 90;
            trade.ExitPrice = 120;
            trade.MaxPrice = 125;
            trade.PnL = 20;
            return trade;
        }

        private static BaseTrade Invalid(int id, SampleSize sampleSize, int day, int createdMinute = 0)
        {
            var trade = Valid(id, sampleSize, day, createdMinute);
            trade.PnL = 19; // should be 20
            return trade;
        }

        private static Task<TradeValidationReport> Run(IEnumerable<SampleSize> sampleSizes, params BaseTrade[] trades) =>
            new TradeValidationService(TestUnitOfWork.Create(sampleSizes, trades), TestSettings.NoSpreads(), new FakeTimeProvider(Now)).ValidateAllAsync();

        [Fact]
        public async Task Only_invalid_trades_are_listed_with_their_problems()
        {
            var ss = SampleSize(1);

            var report = await Run([ss], Valid(1, ss, 1), Invalid(2, ss, 2), Valid(3, ss, 3), Valid(4, ss, 4));

            report.InvalidTrades.Should().ContainSingle()
                .Which.Issues.Should().ContainSingle(i => i.Field == nameof(BaseTrade.PnL));
            report.InvalidTrades[0].TradeId.Should().Be(2);
            report.TradesChecked.Should().Be(3);
            report.CompletedAtUtc.Should().Be(Now.UtcDateTime);
        }

        [Fact]
        public async Task The_most_recent_trade_is_skipped_because_it_may_be_in_progress()
        {
            var ss = SampleSize(1);
            var inProgress = Valid(9, ss, 20);
            inProgress.ExitPrice = null;
            inProgress.PnL = null;

            var report = await Run([ss], Valid(1, ss, 1), inProgress);

            report.SkippedTradeId.Should().Be(9);
            report.InvalidTrades.Should().BeEmpty();
            report.TradesChecked.Should().Be(1);
        }

        [Fact]
        public async Task Only_the_single_most_recent_trade_is_skipped_not_the_one_before()
        {
            var ss = SampleSize(1);

            var report = await Run([ss], Invalid(1, ss, 18), Invalid(2, ss, 19), Invalid(3, ss, 20));

            report.SkippedTradeId.Should().Be(3);
            report.InvalidTrades.Select(t => t.TradeId).Should().Equal(1, 2);
        }

        [Fact]
        public async Task Most_recent_means_latest_date_then_creation_time_then_id_across_all_strategies()
        {
            var srs = SampleSize(1);
            var espresso = SampleSize(2, Strategy.Espresso, TimeFrame.M5);

            // Same day: the later-created one is the most recent, whatever its id or strategy.
            var report = await Run([srs, espresso],
                Invalid(50, srs, 20, createdMinute: 10),
                Invalid(7, espresso, 20, createdMinute: 30),
                Invalid(60, srs, 19, createdMinute: 59));

            report.SkippedTradeId.Should().Be(7);
        }

        [Fact]
        public async Task Research_trades_are_not_validated()
        {
            var demo = SampleSize(1);
            var research = SampleSize(2, type: SampleSizeType.Research);

            var report = await Run([demo, research], Invalid(1, research, 1), Invalid(2, demo, 2), Valid(3, demo, 3));

            report.InvalidTrades.Select(t => t.TradeId).Should().Equal(2);
            report.TradesChecked.Should().Be(1);
        }

        [Fact]
        public async Task Invalid_trades_carry_where_to_find_them_on_the_trades_page()
        {
            var ss3 = SampleSize(3);
            var ss8 = SampleSize(8);
            var paper = SampleSize(5, type: SampleSizeType.PaperTrade);

            var report = await Run([ss3, paper, ss8],
                Valid(21, ss8, 1), Invalid(34, ss8, 2), Valid(40, ss8, 3),   // trade 34 is the 2nd trade (by id) of sample size 8
                Valid(10, ss3, 1),
                Valid(11, paper, 1),
                Valid(99, ss8, 30));                                         // most recent, skipped

            var invalid = report.InvalidTrades.Should().ContainSingle().Subject;
            invalid.Should().BeEquivalentTo(new
            {
                TradeId = 34,
                Strategy = Strategy.SRS,
                TimeFrame = TimeFrame.M15,
                AccountType = SampleSizeType.DemoTrading,
                SampleSizeId = 8,
                SampleSizeNumber = 2,   // 2nd Demo SRS 15M sample size; the paper one doesn't count
                TradeNumber = 2,
                Date = new DateOnly(2026, 9, 2),
                Symbol = "DAX",
            });
        }

        [Fact]
        public async Task Invalid_trades_are_listed_in_the_order_of_the_trades_page_by_trade_within_a_sample_size()
        {
            var ss = SampleSize(1);

            // Dates are all over the place; the trades of a sample size are numbered by id.
            var report = await Run([ss], Invalid(5, ss, 12), Invalid(6, ss, 3), Invalid(7, ss, 8), Valid(8, ss, 30));

            report.InvalidTrades.Select(t => t.TradeId).Should().Equal(5, 6, 7);
            report.InvalidTrades.Select(t => t.TradeNumber).Should().Equal(1, 2, 3);
        }

        [Fact]
        public async Task Invalid_trades_are_listed_by_sample_size_oldest_first_whatever_the_strategy_or_the_dates()
        {
            var first = SampleSize(1);
            var second = SampleSize(2, Strategy.Espresso, TimeFrame.M5);
            var third = SampleSize(3);

            // Listed in a jumbled order and dated against the sample size order on purpose.
            var report = await Run([first, second, third],
                Invalid(30, third, 2), Invalid(20, second, 9), Invalid(11, first, 20), Invalid(10, first, 21), Valid(40, third, 29));

            report.InvalidTrades.Select(t => t.SampleSizeId).Should().Equal(1, 1, 2, 3);
            report.InvalidTrades.Select(t => t.TradeId).Should().Equal(10, 11, 20, 30);
        }

        [Fact]
        public async Task A_missing_symbol_is_shown_as_unknown()
        {
            var ss = SampleSize(1);
            var trade = Valid(1, ss, 1);
            trade.Symbol = null;

            var report = await Run([ss], trade, Valid(2, ss, 2));

            report.InvalidTrades.Single().Symbol.Should().Be("Unknown");
        }

        [Fact]
        public async Task No_trades_gives_an_empty_report()
        {
            var report = await Run([]);

            report.TradesChecked.Should().Be(0);
            report.SkippedTradeId.Should().BeNull();
            report.InvalidTrades.Should().BeEmpty();
        }
    }
}
