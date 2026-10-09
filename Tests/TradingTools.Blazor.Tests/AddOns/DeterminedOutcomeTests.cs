using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;

namespace TradingTools.Blazor.Tests.AddOns
{
    /// <summary>
    /// A trade's outcome is worked out, never typed in - like an add-on's: from its prices and direction, and with
    /// add-ons from the net result of the whole trade.
    /// </summary>
    public class DeterminedOutcomeTests
    {
        private static SRS Trade(EDirection direction, double? entry, double? exit, EOutcome stored = EOutcome.Win, double? amount = 1, params TradeAddOn[] addOns) => new()
        {
            Direction = direction, EntryPrice = entry, ExitPrice = exit, Amount = amount, Outcome = stored, AddOns = [.. addOns]
        };

        private static TradeAddOn AddOn(double entry, double exit, double volume = 1) =>
            new() { EntryPrice = entry, ExitPrice = exit, Volume = volume };

        [Theory]
        [InlineData(EDirection.Long, 1000, 1010, EOutcome.Win)]
        [InlineData(EDirection.Long, 1000, 990, EOutcome.Loss)]
        [InlineData(EDirection.Short, 1000, 990, EOutcome.Win)]
        [InlineData(EDirection.Short, 1000, 1010, EOutcome.Loss)]
        [InlineData(EDirection.Long, 1000, 1000, EOutcome.Breakeven)]
        [InlineData(EDirection.Short, 1000, 1000, EOutcome.Breakeven)]
        public void Without_add_ons_the_prices_and_direction_decide(EDirection direction, double entry, double exit, EOutcome expected)
        {
            TradeNet.DeterminedOutcome(Trade(direction, entry, exit)).Should().Be(expected);
        }

        [Theory]
        [InlineData(EOutcome.Win)]
        [InlineData(EOutcome.Loss)]
        [InlineData(EOutcome.Breakeven)]
        public void Whatever_was_stored_does_not_matter(EOutcome stored)
        {
            TradeNet.DeterminedOutcome(Trade(EDirection.Long, 1000, 1010, stored)).Should().Be(EOutcome.Win);
        }

        [Fact]
        public void The_spread_does_not_change_the_outcome_of_a_trade_without_add_ons()
        {
            // A small win stays a win, like an add-on's outcome: it is the prices that decide.
            TradeNet.DeterminedOutcome(Trade(EDirection.Long, 1000, 1000.5), spread: 1).Should().Be(EOutcome.Win);
        }

        [Theory]
        [InlineData(null, 1010.0)]
        [InlineData(1000.0, null)]
        [InlineData(null, null)]
        [InlineData(0.0, 1010.0)]
        public void It_is_unknown_while_a_price_is_missing(double? entry, double? exit)
        {
            TradeNet.DeterminedOutcome(Trade(EDirection.Long, entry, exit)).Should().BeNull();
        }

        [Fact]
        public void With_add_ons_the_net_result_decides_not_the_main_position()
        {
            // +50 on the main position, the add-on loses 60 on the same volume: the trade lost.
            var trade = Trade(EDirection.Long, 1000, 1050, EOutcome.Win, 1, AddOn(1110, 1050));

            TradeNet.DeterminedOutcome(trade).Should().Be(EOutcome.Loss);
        }

        [Fact]
        public void With_add_ons_the_spread_is_part_of_the_net_result()
        {
            // +10 on the main position and +10 on the add-on is +20; with a spread of 12 per position it is -4.
            var trade = Trade(EDirection.Long, 1000, 1010, EOutcome.Win, 1, AddOn(1000, 1010));

            TradeNet.DeterminedOutcome(trade, 0).Should().Be(EOutcome.Win);
            TradeNet.DeterminedOutcome(trade, 12).Should().Be(EOutcome.Loss);
        }

        [Fact]
        public void With_add_ons_it_is_unknown_until_the_amount_and_every_price_and_volume_are_there()
        {
            TradeNet.DeterminedOutcome(Trade(EDirection.Long, 1000, 1010, EOutcome.Win, amount: null, AddOn(1000, 1010))).Should().BeNull();
            TradeNet.DeterminedOutcome(Trade(EDirection.Long, 1000, 1010, EOutcome.Win, 1, new TradeAddOn { EntryPrice = 1000 })).Should().BeNull();
        }

        [Fact]
        public void With_add_ons_a_breakeven_everywhere_is_a_breakeven_even_with_a_spread()
        {
            var trade = Trade(EDirection.Long, 1000, 1000, EOutcome.Loss, 2, AddOn(1010, 1010, volume: 3));

            TradeNet.DeterminedOutcome(trade, 1.2).Should().Be(EOutcome.Breakeven);
        }
    }
}
