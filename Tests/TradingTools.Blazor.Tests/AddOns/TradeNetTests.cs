using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.AddOns;
using TradingTools.Blazor.Services.Dashboard;
using TradingTools.Blazor.Services.Validation;
using TradingTools.Blazor.Tests.Validation;

namespace TradingTools.Blazor.Tests.AddOns
{
    /// <summary>
    /// A trade with add-ons is one trade with one result in euro: its Outcome is the sign of the net, not
    /// of the main position alone.
    /// </summary>
    public class TradeNetTests
    {
        private static TradeAddOn AddOn(double entry, double exit, double volume = 1) =>
            new() { EntryPrice = entry, ExitPrice = exit, Volume = volume, PnL = Math.Abs(exit - entry) };

        /// <summary>Long, entry 1000, exit 1050: the main position wins 50 points on amount 1.</summary>
        private static SRS Long(EOutcome outcome, params TradeAddOn[] addOns) => new()
        {
            Direction = EDirection.Long, EntryPrice = 1000, ExitPrice = 1050, Amount = 1, PnL = 50, Outcome = outcome, AddOns = [.. addOns]
        };

        #region The calculation

        [Fact]
        public void A_winning_main_position_outweighed_by_a_losing_add_on_is_a_net_loss()
        {
            // The case from the Trades page: +50 on the main position, the add-on (same volume) loses 60.
            var net = TradeNet.Calculate(Long(EOutcome.Loss, AddOn(1110, 1050)))!;

            net.MainEuro.Should().Be(50);
            net.AddOnsEuro.Should().Be(-60);
            net.NetEuro.Should().Be(-10);
            net.Outcome.Should().Be(EOutcome.Loss);
        }

        [Fact]
        public void A_net_gain_is_a_win_even_when_an_add_on_lost()
        {
            var net = TradeNet.Calculate(Long(EOutcome.Win, AddOn(1060, 1050)))!; // +50 − 10

            net.NetEuro.Should().Be(40);
            net.Outcome.Should().Be(EOutcome.Win);
        }

        [Fact]
        public void A_net_of_exactly_zero_is_a_breakeven()
        {
            var net = TradeNet.Calculate(Long(EOutcome.Breakeven, AddOn(1100, 1050)))!; // +50 − 50

            net.NetEuro.Should().Be(0);
            net.Outcome.Should().Be(EOutcome.Breakeven);
        }

        [Fact]
        public void Each_position_counts_with_its_own_volume()
        {
            // Main: +50 × 2 = +100. Add-on: −30 × 4 = −120. The add-on is smaller in points but bigger in volume.
            var trade = Long(EOutcome.Loss, AddOn(1080, 1050, volume: 4));
            trade.Amount = 2;

            var net = TradeNet.Calculate(trade)!;

            net.MainEuro.Should().Be(100);
            net.AddOnsEuro.Should().Be(-120);
            net.Outcome.Should().Be(EOutcome.Loss);
        }

        [Fact]
        public void For_a_short_trade_a_position_wins_when_it_closes_below_its_entry()
        {
            var trade = new SRS
            {
                Direction = EDirection.Short, EntryPrice = 1000, ExitPrice = 950, Amount = 1,
                AddOns = [AddOn(1010, 950)] // a short added at 1010 and closed at 950: +60
            };

            var net = TradeNet.Calculate(trade)!;

            net.MainEuro.Should().Be(50);
            net.AddOnsEuro.Should().Be(60);
        }

        [Fact]
        public void A_losing_short_main_position_with_a_winning_add_on()
        {
            var trade = new SRS
            {
                Direction = EDirection.Short, EntryPrice = 1000, ExitPrice = 1020, Amount = 1,   // −20
                AddOns = [AddOn(1040, 1020)]                                                      // a short added at 1040, closed at 1020: +20
            };

            TradeNet.Calculate(trade)!.NetEuro.Should().Be(0);
        }

        [Fact]
        public void Cents_are_rounded_so_floating_point_residue_cannot_make_a_breakeven_a_win()
        {
            // 7671.25 − 7655.1 = 16.150000000000546; the same distance on the add-on in the other direction.
            var trade = new SRS
            {
                Direction = EDirection.Long, EntryPrice = 7655.1, ExitPrice = 7671.25, Amount = 1,
                AddOns = [new() { EntryPrice = 7671.25, ExitPrice = 7655.1, Volume = 1 }]
            };

            TradeNet.Calculate(trade)!.Outcome.Should().Be(EOutcome.Breakeven);
        }

        [Fact]
        public void No_add_ons_means_the_main_position_alone()
        {
            var net = TradeNet.Calculate(Long(EOutcome.Win))!;

            net.NetEuro.Should().Be(50);
            net.AddOnsEuro.Should().Be(0);
        }

        public static TheoryData<string, SRS> UnknownParts => new()
        {
            { "no amount", Long(EOutcome.Win, AddOn(1060, 1050)).With(t => t.Amount = null) },
            { "zero amount", Long(EOutcome.Win, AddOn(1060, 1050)).With(t => t.Amount = 0) },
            { "no entry", Long(EOutcome.Win, AddOn(1060, 1050)).With(t => t.EntryPrice = null) },
            { "no exit", Long(EOutcome.Win, AddOn(1060, 1050)).With(t => t.ExitPrice = null) },
            { "add-on without entry", Long(EOutcome.Win, new TradeAddOn { ExitPrice = 1050, Volume = 1 }) },
            { "add-on without exit", Long(EOutcome.Win, new TradeAddOn { EntryPrice = 1060, Volume = 1 }) },
            { "add-on without volume", Long(EOutcome.Win, new TradeAddOn { EntryPrice = 1060, ExitPrice = 1050 }) },
            { "add-on with zero volume", Long(EOutcome.Win, AddOn(1060, 1050, volume: 0)) },
            { "unknown direction", Long(EOutcome.Win, AddOn(1060, 1050)).With(t => t.Direction = (EDirection)7) },
        };

        [Theory]
        [MemberData(nameof(UnknownParts))]
        public void A_partly_filled_in_trade_has_no_result_yet(string _, SRS trade) =>
            TradeNet.Calculate(trade).Should().BeNull();

        [Theory]
        [InlineData(EDirection.Long, 1000, 1010, 1)]
        [InlineData(EDirection.Long, 1000, 990, -1)]
        [InlineData(EDirection.Short, 1000, 990, 1)]
        [InlineData(EDirection.Short, 1000, 1010, -1)]
        [InlineData(EDirection.Long, 1000, 1000, 0)]
        [InlineData(EDirection.Short, 1000, 1000, 0)]
        public void A_position_made_money_when_it_closed_on_the_right_side_of_its_entry(EDirection direction, double entry, double exit, int expected) =>
            TradeNet.ResultSign(entry, exit, direction).Should().Be(expected);

        #endregion

        #region Validation: the outcome is checked against the net result

        [Fact]
        public void A_loss_is_valid_when_the_net_is_negative_although_the_main_position_won()
        {
            var trade = TradeValidatorTests.ValidTrade();   // Long 7671 -> 7692, +21 × amount 3 = +63
            trade.AddOns = [AddOn(7750, 7692, volume: 2)];  // −58 × 2 = −116 -> net −53
            trade.Outcome = EOutcome.Loss;

            TradeValidator.Validate(trade).Should().BeEmpty("the exit-side rule only applies to trades without add-ons");
        }

        [Fact]
        public void A_win_is_flagged_when_the_net_is_negative()
        {
            var trade = TradeValidatorTests.ValidTrade();
            trade.AddOns = [AddOn(7750, 7692, volume: 2)];
            trade.Outcome = EOutcome.Win;

            var issue = TradeValidator.Validate(trade).Should().ContainSingle().Subject;

            issue.Field.Should().Be(nameof(BaseTrade.Outcome));
            issue.Message.Should().Be("Net result is −€53.00 (main +€63.00, add-ons −€116.00), so the outcome should be Loss, not Win.");
        }

        [Fact]
        public void A_loss_is_flagged_when_the_net_is_positive()
        {
            var trade = TradeValidatorTests.ValidTrade();
            trade.AddOns = [AddOn(7680, 7692, volume: 1)];  // +12 -> net +75
            trade.Outcome = EOutcome.Loss;

            TradeValidator.Validate(trade).Should().ContainSingle()
                .Which.Message.Should().Be("Net result is +€75.00 (main +€63.00, add-ons +€12.00), so the outcome should be Win, not Loss.");
        }

        [Fact]
        public void A_breakeven_is_valid_for_a_net_of_exactly_zero()
        {
            var trade = TradeValidatorTests.ValidTrade();   // +63
            trade.AddOns = [AddOn(7692 + 31.5, 7692, volume: 2)]; // −31.5 × 2 = −63
            trade.Outcome = EOutcome.Breakeven;

            TradeValidator.Validate(trade).Should().BeEmpty();
        }

        [Fact]
        public void Without_add_ons_the_exit_side_rule_still_applies()
        {
            var trade = TradeValidatorTests.ValidTrade();   // Long, exit above entry
            trade.Outcome = EOutcome.Loss;

            TradeValidator.Validate(trade).Should().ContainSingle().Which.Field.Should().Be(nameof(BaseTrade.ExitPrice));
        }

        [Fact]
        public void The_outcome_isnt_judged_while_a_part_of_the_net_is_missing()
        {
            var trade = TradeValidatorTests.ValidTrade();
            trade.AddOns = [new() { EntryPrice = 7750, ExitPrice = 7692, Volume = null, PnL = 58 }];
            trade.Outcome = EOutcome.Loss;

            // Only the missing volume is reported, not a guess about the outcome.
            TradeValidator.Validate(trade).Should().ContainSingle().Which.Field.Should().Be("AddOns[0].Volume");
        }

        #endregion

        #region Dashboard: the main position is signed by its prices, not by the trade's outcome

        private static async Task<DashboardTrade> Load(SRS trade)
        {
            var sampleSize = new SampleSize { Id = 1, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.DemoTrading };
            trade.SampleSize = sampleSize;
            trade.SampleSizeId = 1;
            trade.Date = new DateOnly(2026, 9, 1);
            trade.Status = EStatus.Closed;
            trade.Symbol = "DAX";
            return (await new DashboardService(TestUnitOfWork.Create([sampleSize], [trade])).GetTradesAsync()).Single();
        }

        [Fact]
        public async Task A_net_loss_with_a_winning_main_position_counts_the_real_points_and_euro()
        {
            // Outcome Loss. The main position's +50 must not be turned into −50 by the outcome.
            var result = await Load(Long(EOutcome.Loss, AddOn(1110, 1050)));

            result.Outcome.Should().Be(EOutcome.Loss);
            result.Points.Should().Be(50);
            result.TotalPoints.Should().Be(-10);
            result.Euro.Should().Be(-10);
        }

        [Fact]
        public async Task A_breakeven_with_add_ons_still_counts_the_main_positions_own_points()
        {
            var result = await Load(Long(EOutcome.Breakeven, AddOn(1100, 1050)));

            result.Points.Should().Be(50, "only a trade without add-ons has no points when it is a breakeven");
            result.TotalPoints.Should().Be(0);
            result.Euro.Should().Be(0);
        }

        [Fact]
        public async Task A_trade_without_add_ons_is_signed_by_its_outcome_as_before()
        {
            (await Load(Long(EOutcome.Loss))).Points.Should().Be(-50);
            (await Load(Long(EOutcome.Breakeven))).Points.Should().Be(0);
        }

        [Fact]
        public async Task The_dashboard_figures_use_the_net_result_of_the_trade()
        {
            var loss = await Load(Long(EOutcome.Loss, AddOn(1110, 1050)));

            var summary = DashboardStats.Summarize([loss]);

            summary.Losses.Should().Be(1);
            summary.Wins.Should().Be(0);
            summary.AvgLossPoints.Should().Be(-10);
            summary.NetEuro.Should().Be(-10);
        }

        [Fact]
        public async Task A_main_position_whose_prices_are_missing_has_no_result_when_it_has_add_ons()
        {
            var trade = Long(EOutcome.Loss, AddOn(1110, 1050));
            trade.ExitPrice = null;

            (await Load(trade)).Points.Should().BeNull();
        }

        #endregion
    }

    internal static class SrsExtensions
    {
        public static SRS With(this SRS trade, Action<SRS> change)
        {
            change(trade);
            return trade;
        }
    }
}
