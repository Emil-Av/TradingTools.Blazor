using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;

namespace TradingTools.Blazor.Tests.AddOns
{
    public class TradeAddOnsTests
    {
        private static TradeAddOn AddOn(double? entry, double? exit, int id = 0) => new() { Id = id, EntryPrice = entry, ExitPrice = exit };

        [Theory]
        [InlineData(EDirection.Long, 1010, 1025, 15)]    // long, closed higher: won
        [InlineData(EDirection.Long, 1020, 1015, -5)]    // long, closed lower: lost
        [InlineData(EDirection.Short, 1010, 995, 15)]    // short, closed lower: won
        [InlineData(EDirection.Short, 1000, 1004.5, -4.5)] // short, closed higher: lost
        [InlineData(EDirection.Long, 1000, 1000, 0)]
        [InlineData(EDirection.Short, 1000, 1000, 0)]
        public void Signed_points_come_from_the_direction_and_the_add_ons_prices(EDirection direction, double entry, double exit, double expected) =>
            TradeAddOns.SignedPoints(AddOn(entry, exit), direction).Should().Be(expected);

        [Theory]
        [InlineData(EDirection.Long, 1010, 1025, EOutcome.Win)]
        [InlineData(EDirection.Long, 1020, 1015, EOutcome.Loss)]
        [InlineData(EDirection.Short, 1010, 995, EOutcome.Win)]
        [InlineData(EDirection.Short, 1000, 1004.5, EOutcome.Loss)]
        [InlineData(EDirection.Long, 1000, 1000, EOutcome.Breakeven)]
        [InlineData(EDirection.Short, 1000, 1000, EOutcome.Breakeven)]
        public void An_add_ons_outcome_comes_from_its_prices_and_the_trades_direction(EDirection direction, double entry, double exit, EOutcome expected) =>
            TradeAddOns.OutcomeOf(AddOn(entry, exit), direction).Should().Be(expected);

        [Fact]
        public void The_same_add_on_has_the_opposite_outcome_in_the_other_direction()
        {
            var addOn = AddOn(1000, 1020);

            TradeAddOns.OutcomeOf(addOn, EDirection.Long).Should().Be(EOutcome.Win);
            TradeAddOns.OutcomeOf(addOn, EDirection.Short).Should().Be(EOutcome.Loss);
        }

        [Theory]
        [InlineData(null, 1000.0)]
        [InlineData(1000.0, null)]
        [InlineData(0.0, 1000.0)]
        [InlineData(null, null)]
        public void An_add_ons_outcome_is_unknown_without_both_prices(double? entry, double? exit) =>
            TradeAddOns.OutcomeOf(AddOn(entry, exit), EDirection.Long).Should().BeNull();

        [Fact]
        public void An_add_ons_outcome_always_matches_the_sign_of_its_points()
        {
            foreach (var direction in new[] { EDirection.Long, EDirection.Short })
            foreach (var (entry, exit) in new[] { (1000.0, 1010.0), (1010.0, 1000.0), (1000.0, 1000.0) })
            {
                var addOn = AddOn(entry, exit);
                double points = TradeAddOns.SignedPoints(addOn, direction)!.Value;

                TradeAddOns.OutcomeOf(addOn, direction).Should().Be(points > 0 ? EOutcome.Win : points < 0 ? EOutcome.Loss : EOutcome.Breakeven);
            }
        }

        [Fact]
        public void Signed_points_are_rounded_like_the_trades_pnl() =>
            // 7671.25 - 7655.1 = 16.150000000000546 in floating point
            TradeAddOns.SignedPoints(AddOn(7655.1, 7671.25), EDirection.Long).Should().Be(16.15);

        [Theory]
        [InlineData(null, 1000.0)]
        [InlineData(1000.0, null)]
        [InlineData(0.0, 1000.0)]
        public void Signed_points_are_unknown_without_both_prices(double? entry, double? exit) =>
            TradeAddOns.SignedPoints(AddOn(entry, exit), EDirection.Long).Should().BeNull();

        [Fact]
        public void Add_ons_are_sorted_in_the_order_they_were_added_with_unsaved_ones_last()
        {
            var unsavedA = AddOn(1, 1);
            var unsavedB = AddOn(2, 2);
            var trade = new SRS { AddOns = [unsavedA, AddOn(1, 1, id: 9), unsavedB, AddOn(1, 1, id: 4)] };

            trade.SortAddOns();

            trade.AddOns.Select(a => a.Id).Should().Equal(4, 9, 0, 0);
            trade.AddOns[2].Should().BeSameAs(unsavedA);
            trade.AddOns[3].Should().BeSameAs(unsavedB);
        }

        [Fact]
        public void A_new_add_on_starts_with_the_trades_exit_price()
        {
            var addOn = TradeAddOns.NewFor(new SRS { Id = 42, ExitPrice = 7692 });

            addOn.Should().BeEquivalentTo(new { Id = 0, BaseTradeId = 42, ExitPrice = 7692.0, EntryPrice = (double?)null, Volume = (double?)null, PnL = (double?)null });
        }
    }
}
