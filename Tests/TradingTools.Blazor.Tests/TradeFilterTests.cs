using Models.Trades;
using Shared.Enums;
using TradingTools.Blazor.Services;

namespace TradingTools.Blazor.Tests
{
    /// <summary>The filter of the Trades page: All or a period, a symbol and a result, all applied together.</summary>
    public class TradeFilterTests
    {
        private static SRS T(int id, int month, int day, string symbol, EOutcome outcome, int createdMinute = 0) => new()
        {
            Id = id, Date = new DateOnly(2026, month, day), Symbol = symbol, Outcome = outcome,
            CreatedAt = new DateTime(2026, month, day, 12, createdMinute, 0, DateTimeKind.Utc)
        };

        private static readonly SRS[] Trades =
        [
            T(1, 2, 3, "DAX", EOutcome.Win),
            T(2, 2, 10, "US500", EOutcome.Loss),
            T(3, 9, 1, "DAX", EOutcome.Loss),
            T(4, 9, 15, "US500", EOutcome.Win),
            T(5, 10, 2, "US500", EOutcome.Loss),
            T(6, 11, 30, "DAX", EOutcome.Breakeven),
            T(7, 12, 1, "US500", EOutcome.Loss),
        ];

        private static IEnumerable<int> Ids(TradeFilter filter) => filter.Apply(Trades).Select(t => t.Id);

        private static DateOnly D(int month, int day) => new(2026, month, day);

        [Fact]
        public void Nothing_set_is_no_filter()
        {
            new TradeFilter().IsSet.Should().BeFalse();
            new TradeFilter(AllDates: true).IsSet.Should().BeTrue();
            new TradeFilter(Symbol: ESymbol.DAX).IsSet.Should().BeTrue();
            new TradeFilter(Outcome: EOutcome.Win).IsSet.Should().BeTrue();
            new TradeFilter(From: D(1, 1)).IsSet.Should().BeTrue();
        }

        [Fact]
        public void All_shows_every_trade_oldest_first()
        {
            Ids(new TradeFilter(AllDates: true)).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        }

        [Fact]
        public void All_trades_for_DAX_wins_only()
        {
            Ids(new TradeFilter(AllDates: true, Symbol: ESymbol.DAX, Outcome: EOutcome.Win)).Should().Equal(1);
        }

        [Fact]
        public void All_trades_from_September_until_November_US500_losses_only()
        {
            Ids(new TradeFilter(From: D(9, 1), To: D(11, 30), Symbol: ESymbol.US500, Outcome: EOutcome.Loss)).Should().Equal(5);
        }

        [Fact]
        public void All_trades_since_February_DAX_only_regardless_of_the_result()
        {
            Ids(new TradeFilter(From: D(2, 1), Symbol: ESymbol.DAX)).Should().Equal(1, 3, 6);
        }

        [Fact]
        public void A_result_alone_covers_all_dates()
        {
            Ids(new TradeFilter(Outcome: EOutcome.Loss)).Should().Equal(2, 3, 5, 7);
            Ids(new TradeFilter(Outcome: EOutcome.Breakeven)).Should().Equal(6);
        }

        [Fact]
        public void With_All_the_dates_do_not_count()
        {
            var filter = new TradeFilter(AllDates: true, From: D(9, 1), To: D(9, 30));

            Ids(filter).Should().Equal(1, 2, 3, 4, 5, 6, 7);
            filter.Describe().Should().Be("all dates");
        }

        [Fact]
        public void The_symbol_matches_whatever_its_case_or_spacing()
        {
            var trades = new[] { T(1, 3, 1, " dax ", EOutcome.Win), T(2, 3, 2, "Dax", EOutcome.Win), T(3, 3, 3, "DAX40", EOutcome.Win), T(4, 3, 4, "", EOutcome.Win) };

            new TradeFilter(Symbol: ESymbol.DAX).Apply(trades).Select(t => t.Id).Should().Equal(1, 2);
        }

        [Fact]
        public void A_trade_without_a_symbol_only_shows_while_the_symbol_is_not_filtered()
        {
            var noSymbol = new SRS { Id = 9, Date = D(3, 1), Symbol = null, Outcome = EOutcome.Win };

            new TradeFilter(AllDates: true).Matches(noSymbol).Should().BeTrue();
            new TradeFilter(Symbol: ESymbol.US500).Matches(noSymbol).Should().BeFalse();
        }

        [Fact]
        public void Both_days_of_a_period_are_included()
        {
            Ids(new TradeFilter(From: D(9, 1), To: D(9, 15))).Should().Equal(3, 4);
        }

        [Fact]
        public void From_after_to_has_nothing_in_it()
        {
            var filter = new TradeFilter(From: D(10, 1), To: D(9, 1));

            filter.IsBackwards.Should().BeTrue();
            Ids(filter).Should().BeEmpty();
            new TradeFilter(AllDates: true, From: D(10, 1), To: D(9, 1)).IsBackwards.Should().BeFalse("All ignores the dates");
        }

        [Fact]
        public void Trades_on_the_same_day_are_in_the_order_they_were_logged_then_by_id()
        {
            var trades = new[] { T(3, 5, 5, "DAX", EOutcome.Win, 30), T(2, 5, 5, "DAX", EOutcome.Win, 10), T(1, 5, 5, "DAX", EOutcome.Win, 10) };

            new TradeFilter(AllDates: true).Apply(trades).Select(t => t.Id).Should().Equal(1, 2, 3);
        }

        [Theory]
        [InlineData(true, null, null, null, null, "all dates")]
        [InlineData(true, null, null, ESymbol.DAX, EOutcome.Win, "DAX · wins · all dates")]
        [InlineData(false, "2026-09-01", "2026-11-30", ESymbol.US500, EOutcome.Loss, "US500 · losses · 1 Sep 2026 - 30 Nov 2026")]
        [InlineData(false, "2026-02-01", null, ESymbol.DAX, null, "DAX · from 1 Feb 2026")]
        [InlineData(false, null, "2026-03-31", null, EOutcome.Breakeven, "breakevens · until 31 Mar 2026")]
        [InlineData(false, null, null, null, EOutcome.Loss, "losses · all dates")]
        public void A_filter_reads_as_what_it_shows(bool all, string? from, string? to, ESymbol? symbol, EOutcome? outcome, string expected)
        {
            var filter = new TradeFilter(all, from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), symbol, outcome);

            filter.Describe().Should().Be(expected);
        }
    }
}
