using System.Linq.Expressions;
using DataAccess.Repository.IRepository;
using Models;
using Models.Trades;
using NSubstitute;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Tests
{
    /// <summary>
    /// Which trades reach the dashboard, in which order, and their "Sample #n". The fake
    /// repositories apply the service's real filter expressions to in-memory data.
    /// </summary>
    public class DashboardServiceTests
    {
        private static readonly DateOnly Day1 = new(2026, 3, 16);
        private static readonly DateOnly Day2 = new(2026, 3, 17);

        private static SampleSize SampleSize(int id, Strategy strategy = Strategy.SRS, TimeFrame timeFrame = TimeFrame.M15, SampleSizeType type = SampleSizeType.DemoTrading) =>
            new() { Id = id, Strategy = strategy, TimeFrame = timeFrame, SampleSizeType = type };

        private static BaseTrade Trade(
            int id,
            SampleSize sampleSize,
            DateOnly? date = null,
            EOutcome outcome = EOutcome.Win,
            EStatus status = EStatus.Closed,
            double? pnl = 10,
            double? amount = 1,
            string? symbol = "DAX",
            DateTime? createdAt = null)
        {
            BaseTrade trade = sampleSize.Strategy switch
            {
                Strategy.Espresso => new Espresso(),
                Strategy.BrunchBreak => new BrunchBreak(),
                _ => new SRS()
            };
            trade.Id = id;
            trade.SampleSize = sampleSize;
            trade.SampleSizeId = sampleSize.Id;
            trade.Date = date ?? Day1;
            trade.Outcome = outcome;
            trade.Status = status;
            trade.PnL = pnl;
            trade.Amount = amount;
            trade.Symbol = symbol;
            trade.CreatedAt = createdAt ?? new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
            return trade;
        }

        private static Task<List<T>> Where<T>(IEnumerable<T> items, Expression<Func<T, bool>>? filter) =>
            Task.FromResult(filter is null ? items.ToList() : items.Where(filter.Compile()).ToList());

        private static Task<List<DashboardTrade>> Load(IEnumerable<SampleSize> sampleSizes, params BaseTrade[] trades)
        {
            var tradeRepository = Substitute.For<IBaseTradeRepository>();
            tradeRepository.GetAllAsync(Arg.Any<Expression<Func<BaseTrade, bool>>?>(), Arg.Any<string?>())
                .Returns(call => Where(trades, call.ArgAt<Expression<Func<BaseTrade, bool>>?>(0)));

            var sampleSizeRepository = Substitute.For<ISampleSizeRepository>();
            sampleSizeRepository.GetAllAsync(Arg.Any<Expression<Func<SampleSize, bool>>?>(), Arg.Any<string?>())
                .Returns(call => Where(sampleSizes, call.ArgAt<Expression<Func<SampleSize, bool>>?>(0)));

            var unitOfWork = Substitute.For<IUnitOfWork>();
            unitOfWork.BaseTrade.Returns(tradeRepository);
            unitOfWork.SampleSize.Returns(sampleSizeRepository);

            return new DashboardService(unitOfWork).GetTradesAsync();
        }

        [Fact]
        public async Task Only_closed_srs_and_espresso_trades_outside_research_are_included()
        {
            var srs = SampleSize(1);
            var espresso = SampleSize(2, Strategy.Espresso, TimeFrame.M5);
            var brunchBreak = SampleSize(3, Strategy.BrunchBreak);
            var research = SampleSize(4, type: SampleSizeType.Research);

            var result = await Load([srs, espresso, brunchBreak, research],
                Trade(1, srs),
                Trade(2, espresso),
                Trade(3, srs, status: EStatus.Opened),
                Trade(4, srs, status: EStatus.Pending),
                Trade(5, brunchBreak),
                Trade(6, research));

            result.Select(t => t.Id).Should().BeEquivalentTo([1, 2], "open, pending, BrunchBreak and research trades are left out");
        }

        [Fact]
        public async Task Paper_and_demo_trades_are_both_included_and_tagged_with_their_account()
        {
            var demo = SampleSize(1, type: SampleSizeType.DemoTrading);
            var paper = SampleSize(2, type: SampleSizeType.PaperTrade);

            var result = await Load([demo, paper], Trade(1, demo), Trade(2, paper));

            result.Single(t => t.Id == 1).AccountType.Should().Be(SampleSizeType.DemoTrading);
            result.Single(t => t.Id == 2).AccountType.Should().Be(SampleSizeType.PaperTrade);
        }

        [Fact]
        public async Task Trades_are_ordered_by_date_then_creation_time_then_id()
        {
            var ss = SampleSize(1);
            var morning = new DateTime(2026, 3, 16, 8, 0, 0, DateTimeKind.Utc);
            var noon = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);

            // Deliberately stored out of order.
            var result = await Load([ss],
                Trade(10, ss, Day2, createdAt: morning),
                Trade(20, ss, Day1, createdAt: noon),
                Trade(5, ss, Day1, createdAt: noon),
                Trade(30, ss, Day1, createdAt: morning));

            result.Select(t => t.Id).Should().Equal(30, 5, 20, 10);
        }

        [Fact]
        public async Task Fields_are_mapped_and_points_are_signed_by_outcome()
        {
            var ss = SampleSize(7, Strategy.Espresso, TimeFrame.M5);

            var loss = (await Load([ss], Trade(1, ss, outcome: EOutcome.Loss, pnl: 34, amount: 0.5, symbol: "US500"))).Single();

            loss.Points.Should().Be(-34);
            loss.Volume.Should().Be(0.5);
            loss.Euro.Should().Be(-17);
            loss.Symbol.Should().Be("US500");
            loss.Strategy.Should().Be(Strategy.Espresso);
            loss.TimeFrame.Should().Be(TimeFrame.M5);
            loss.SampleSizeId.Should().Be(7);
            loss.Date.Should().Be(Day1);
        }

        [Theory]
        [InlineData("DAX", "DAX")]
        [InlineData(" dax ", "DAX")]
        [InlineData("us500", "US500")]
        [InlineData(null, "Unknown")]
        [InlineData("", "Unknown")]
        [InlineData("   ", "Unknown")]
        public async Task Symbols_are_normalized_so_they_group_together(string? stored, string expected)
        {
            var ss = SampleSize(1);

            var trade = (await Load([ss], Trade(1, ss, symbol: stored))).Single();

            trade.Symbol.Should().Be(expected);
        }

        [Fact]
        public async Task Sample_sizes_are_numbered_per_account_strategy_and_timeframe()
        {
            var demoSrs1 = SampleSize(3);
            var demoSrs2 = SampleSize(8);
            var paperSrs = SampleSize(5, type: SampleSizeType.PaperTrade);
            var demoEspresso = SampleSize(6, Strategy.Espresso, TimeFrame.M5);

            var result = await Load([demoSrs1, paperSrs, demoEspresso, demoSrs2],
                Trade(1, demoSrs1), Trade(2, demoSrs2), Trade(3, paperSrs), Trade(4, demoEspresso));

            int Number(int tradeId) => result.Single(t => t.Id == tradeId).SampleSizeNumber;
            Number(1).Should().Be(1);
            Number(2).Should().Be(2, "the paper sample size in between is a different account, so this is #2, not #3");
            Number(3).Should().Be(1);
            Number(4).Should().Be(1);
        }

        /// <summary>
        /// The Trades page numbers a sample size by its position among ALL sample sizes of the same
        /// type/strategy/timeframe. A sample size whose trades are all still open (or pending) must
        /// still take its place in the numbering, or every later one is shown one number too low.
        /// </summary>
        [Fact]
        public async Task Sample_size_numbers_match_the_trades_page_even_if_an_earlier_one_has_no_closed_trades()
        {
            var first = SampleSize(3);
            var second = SampleSize(8);

            var result = await Load([first, second],
                Trade(1, first, status: EStatus.Opened),
                Trade(2, second));

            result.Should().ContainSingle()
                .Which.SampleSizeNumber.Should().Be(2, "sample size 3 still counts even though its only trade is open");
        }
    }
}
