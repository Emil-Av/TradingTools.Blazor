using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Tests.Validation
{
    public class TradeValidatorTests
    {
        /// <summary>A long winning trade with every field filled in correctly.</summary>
        internal static SRS ValidTrade() => new()
        {
            Date = new DateOnly(2026, 8, 21),
            Symbol = "US500",
            Direction = EDirection.Long,
            Outcome = EOutcome.Win,
            TradeRating = ETradeRating.A,
            Amount = 3,
            EntryPrice = 7671,
            StopPrice = 7658,
            ExitPrice = 7692,
            MaxPrice = 7697,
            PnL = 21,
            CandleType = ECandleType.Bullish,
        };

        private static IReadOnlyList<TradeValidationIssue> Validate(BaseTrade trade) => TradeValidator.Validate(trade);

        private static IEnumerable<string> Fields(BaseTrade trade) => Validate(trade).Select(i => i.Field);

        [Fact]
        public void A_complete_consistent_trade_has_no_issues()
        {
            Validate(ValidTrade()).Should().BeEmpty();
        }

        #region Required fields

        [Fact]
        public void Every_missing_trade_data_field_is_reported()
        {
            var trade = new SRS { Date = default, Symbol = null, Amount = null, EntryPrice = null, StopPrice = null, ExitPrice = null, MaxPrice = null, PnL = null };

            Fields(trade).Should().BeEquivalentTo(
                nameof(BaseTrade.Date), nameof(BaseTrade.Symbol), nameof(BaseTrade.Amount), nameof(BaseTrade.EntryPrice),
                nameof(BaseTrade.StopPrice), nameof(BaseTrade.ExitPrice), nameof(BaseTrade.MaxPrice), nameof(BaseTrade.PnL));
        }

        [Theory]
        [InlineData(nameof(BaseTrade.Symbol))]
        [InlineData(nameof(BaseTrade.Amount))]
        [InlineData(nameof(BaseTrade.EntryPrice))]
        [InlineData(nameof(BaseTrade.StopPrice))]
        [InlineData(nameof(BaseTrade.ExitPrice))]
        [InlineData(nameof(BaseTrade.MaxPrice))]
        [InlineData(nameof(BaseTrade.PnL))]
        public void One_missing_field_is_reported_on_its_own(string field)
        {
            var trade = ValidTrade();
            typeof(BaseTrade).GetProperty(field)!.SetValue(trade, null);

            Validate(trade).Should().ContainSingle(i => i.Field == field)
                .Which.Message.Should().EndWith("is missing.");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_symbol_counts_as_missing(string symbol)
        {
            var trade = ValidTrade();
            trade.Symbol = symbol;

            Fields(trade).Should().Equal(nameof(BaseTrade.Symbol));
        }

        [Theory]
        [InlineData(nameof(BaseTrade.MaxPrice))]     // real data: 21 trades have max price 0
        [InlineData(nameof(BaseTrade.StopPrice))]
        [InlineData(nameof(BaseTrade.Amount))]       // real data: 3 trades have amount 0
        public void Zero_is_not_a_filled_in_value(string field)
        {
            var trade = ValidTrade();
            typeof(BaseTrade).GetProperty(field)!.SetValue(trade, 0.0);

            Validate(trade).Should().ContainSingle(i => i.Field == field)
                .Which.Message.Should().Contain("must be above 0");
        }

        [Fact]
        public void Out_of_range_enum_values_are_reported()
        {
            var trade = ValidTrade();
            trade.Direction = (EDirection)9;
            trade.TradeRating = (ETradeRating)9;
            trade.CandleType = (ECandleType)9;

            Fields(trade).Should().BeEquivalentTo(nameof(BaseTrade.Direction), nameof(BaseTrade.TradeRating), "CandleType");
        }

        [Fact]
        public void Research_fields_of_every_strategy_are_checked()
        {
            var espresso = new Espresso { CandleType = (ECandleType)7 };
            var brunchBreak = new BrunchBreak { CandleType = (ECandleType)7 };

            Fields(espresso).Should().Contain("CandleType");
            Fields(brunchBreak).Should().Contain("CandleType");
        }

        #endregion

        #region Exit on the right side of the entry

        [Theory]
        [InlineData(EDirection.Long, EOutcome.Win, 100, 120)]
        [InlineData(EDirection.Short, EOutcome.Win, 120, 100)]
        [InlineData(EDirection.Long, EOutcome.Loss, 120, 100)]
        [InlineData(EDirection.Short, EOutcome.Loss, 100, 120)]
        public void Exit_on_the_correct_side_is_valid(EDirection direction, EOutcome outcome, double entry, double exit)
        {
            var trade = Trade(direction, outcome, entry, exit);

            Validate(trade).Should().BeEmpty();
        }

        [Theory]
        [InlineData(EDirection.Long, EOutcome.Win, 29717, 29637, "Winning Long trade: the exit price (29637) must be above the entry price (29717).")]
        [InlineData(EDirection.Short, EOutcome.Win, 26042, 26078, "Winning Short trade: the exit price (26078) must be below the entry price (26042).")]
        [InlineData(EDirection.Long, EOutcome.Loss, 7671, 7691, "Losing Long trade: the exit price (7691) must be below the entry price (7671).")]
        [InlineData(EDirection.Short, EOutcome.Loss, 26104, 26038, "Losing Short trade: the exit price (26038) must be above the entry price (26104).")]
        public void Exit_on_the_wrong_side_is_reported_on_the_exit_price(EDirection direction, EOutcome outcome, double entry, double exit, string expected)
        {
            var trade = Trade(direction, outcome, entry, exit);

            Validate(trade).Should().ContainSingle()
                .Which.Should().Be(new TradeValidationIssue(nameof(BaseTrade.ExitPrice), expected));
        }

        [Theory]
        [InlineData(EOutcome.Win)]
        [InlineData(EOutcome.Loss)]
        public void Exit_equal_to_entry_is_wrong_for_a_win_or_a_loss(EOutcome outcome)
        {
            var trade = Trade(EDirection.Long, outcome, 100, 100);

            Fields(trade).Should().Equal(nameof(BaseTrade.ExitPrice));
        }

        [Fact]
        public void Changing_a_valid_win_to_a_loss_makes_it_invalid()
        {
            var trade = ValidTrade();
            Validate(trade).Should().BeEmpty();

            trade.Outcome = EOutcome.Loss;

            Fields(trade).Should().Equal(nameof(BaseTrade.ExitPrice));
        }

        [Fact]
        public void Changing_the_direction_of_a_valid_win_makes_it_invalid()
        {
            var trade = ValidTrade();
            trade.Direction = EDirection.Short;

            Fields(trade).Should().Equal(nameof(BaseTrade.ExitPrice));
        }

        [Theory]
        [InlineData(100, 100)]
        [InlineData(100, 103)]
        [InlineData(103, 100)]
        public void Breakeven_has_no_required_exit_side(double entry, double exit)
        {
            var trade = Trade(EDirection.Long, EOutcome.Breakeven, entry, exit);

            Fields(trade).Should().NotContain(nameof(BaseTrade.ExitPrice));
        }

        [Fact]
        public void Exit_side_is_not_checked_when_a_price_is_missing()
        {
            var trade = ValidTrade();
            trade.ExitPrice = null;

            Validate(trade).Should().NotContain(i => i.Message.Contains("must be above the entry"));
        }

        #endregion

        #region P&L

        [Fact]
        public void Pnl_different_from_the_price_distance_is_reported()
        {
            var trade = ValidTrade();   // 7671 -> 7692 = 21 points
            trade.PnL = 20;              // real data

            Validate(trade).Should().ContainSingle()
                .Which.Should().Be(new TradeValidationIssue(nameof(BaseTrade.PnL), "P&L should be 21 points (|exit 7692 − entry 7671|), but is 20."));
        }

        [Fact]
        public void Pnl_is_the_same_positive_number_for_a_loss()
        {
            var trade = Trade(EDirection.Long, EOutcome.Loss, 26138, 26104);
            trade.PnL = 34;

            Validate(trade).Should().BeEmpty();
        }

        [Fact]
        public void Negative_pnl_is_reported_as_not_positive()
        {
            var trade = Trade(EDirection.Long, EOutcome.Loss, 26138, 26104);
            trade.PnL = -34;

            Validate(trade).Should().ContainSingle()
                .Which.Message.Should().Be("P&L must be a positive number of points (is -34).");
        }

        [Fact]
        public void Pnl_equal_to_the_distance_within_rounding_is_valid()
        {
            var trade = Trade(EDirection.Long, EOutcome.Win, 7655.1, 7671.25);
            trade.PnL = 16.15;  // the raw difference is 16.150000000000546

            Validate(trade).Should().BeEmpty();
        }

        [Fact]
        public void Pnl_off_by_one_cent_is_reported()
        {
            var trade = Trade(EDirection.Long, EOutcome.Win, 100, 120);
            trade.PnL = 20.01;

            Fields(trade).Should().Equal(nameof(BaseTrade.PnL));
        }

        [Fact]
        public void Breakeven_with_a_recorded_move_must_record_that_move()
        {
            // Real data: 25315 -> 25312 marked breakeven with P&L 0.
            var trade = Trade(EDirection.Short, EOutcome.Breakeven, 25315, 25312);
            trade.PnL = 0;

            Validate(trade).Should().ContainSingle()
                .Which.Message.Should().Be("P&L should be 3 points (|exit 25312 − entry 25315|), but is 0.");
        }

        [Fact]
        public void Pnl_is_not_compared_when_a_price_is_missing()
        {
            var trade = ValidTrade();
            trade.EntryPrice = null;
            trade.PnL = 999;

            Validate(trade).Should().NotContain(i => i.Field == nameof(BaseTrade.PnL));
        }

        #endregion

        [Fact]
        public void Several_problems_are_all_reported()
        {
            // Real data: a short win with the exit above the entry and a max price of 0.
            var trade = Trade(EDirection.Short, EOutcome.Win, 26042, 26078);
            trade.MaxPrice = 0;
            trade.PnL = 35;

            Fields(trade).Should().BeEquivalentTo(nameof(BaseTrade.MaxPrice), nameof(BaseTrade.ExitPrice), nameof(BaseTrade.PnL));
        }

        private static SRS Trade(EDirection direction, EOutcome outcome, double entry, double exit)
        {
            var trade = ValidTrade();
            trade.Direction = direction;
            trade.Outcome = outcome;
            trade.EntryPrice = entry;
            trade.ExitPrice = exit;
            trade.StopPrice = entry;
            trade.MaxPrice = Math.Max(entry, exit);
            trade.PnL = TradePnl.Points(entry, exit);
            return trade;
        }
    }
}
