using Models;
using Models.Trades;
using Shared.Enums;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Validation;
using TradingTools.Blazor.Tests.Validation;

namespace TradingTools.Blazor.Tests.AddOns
{
    /// <summary>Add-ons are checked like the trade's own prices and P&amp;L, by the same validator.</summary>
    public class AddOnValidationTests
    {
        private static TradeAddOn AddOn(double? entry = 7680, double? exit = 7692, double? volume = 1, double? pnl = 12) =>
            new() { EntryPrice = entry, ExitPrice = exit, Volume = volume, PnL = pnl };

        private static SRS TradeWith(params TradeAddOn[] addOns)
        {
            var trade = TradeValidatorTests.ValidTrade();
            trade.AddOns = [.. addOns];
            return trade;
        }

        [Fact]
        public void Complete_consistent_add_ons_have_no_issues() =>
            TradeValidator.Validate(TradeWith(AddOn(), AddOn(entry: 7685, pnl: 7))).Should().BeEmpty();

        [Fact]
        public void Every_missing_add_on_field_is_reported_against_that_add_on()
        {
            var issues = TradeValidator.Validate(TradeWith(AddOn(), AddOn(entry: null, exit: null, volume: null, pnl: null)));

            issues.Select(i => i.Field).Should().BeEquivalentTo(
                "AddOns[1].EntryPrice", "AddOns[1].ExitPrice", "AddOns[1].Volume", "AddOns[1].PnL");
            issues.Should().OnlyContain(i => i.Message.StartsWith("Add-on 2: "));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Prices_and_volume_must_be_above_zero(double value)
        {
            var issues = TradeValidator.Validate(TradeWith(AddOn(entry: value, exit: value, volume: value, pnl: 0)));

            issues.Select(i => i.Field).Should().BeEquivalentTo("AddOns[0].EntryPrice", "AddOns[0].ExitPrice", "AddOns[0].Volume");
        }

        [Fact]
        public void Pnl_must_match_the_points_between_entry_and_exit()
        {
            var issue = TradeValidator.Validate(TradeWith(AddOn(entry: 7680, exit: 7692, pnl: 10))).Should().ContainSingle().Subject;

            issue.Field.Should().Be("AddOns[0].PnL");
            issue.Message.Should().Be("Add-on 1: P&L should be 12 points (|exit 7692 − entry 7680|), but is 10.");
        }

        [Fact]
        public void Pnl_is_a_positive_number_of_points()
        {
            var issue = TradeValidator.Validate(TradeWith(AddOn(pnl: -12))).Should().ContainSingle().Subject;

            issue.Field.Should().Be("AddOns[0].PnL");
            issue.Message.Should().Be("Add-on 1: P&L must be a positive number of points (is -12).");
        }

        [Fact]
        public void Pnl_within_rounding_tolerance_is_accepted() =>
            TradeValidator.Validate(TradeWith(AddOn(entry: 7655.1, exit: 7671.25, pnl: 16.15))).Should().BeEmpty();

        [Fact]
        public void A_losing_add_on_on_a_winning_trade_is_valid()
        {
            // Long win: first entry 7671, exit 7692. Added at 7700 - that part lost 8 points, which is fine.
            TradeValidator.Validate(TradeWith(AddOn(entry: 7700, exit: 7692, pnl: 8))).Should().BeEmpty();
        }

        [Fact]
        public void Only_the_broken_add_on_is_reported()
        {
            var issues = TradeValidator.Validate(TradeWith(AddOn(), AddOn(volume: null), AddOn()));

            issues.Should().ContainSingle().Which.Field.Should().Be("AddOns[1].Volume");
        }

        [Fact]
        public async Task The_data_check_reports_add_on_problems_in_the_order_they_were_added()
        {
            var sampleSize = new SampleSize { Id = 1, Strategy = Strategy.SRS, TimeFrame = TimeFrame.M15, SampleSizeType = SampleSizeType.DemoTrading };
            var trade = TradeValidatorTests.ValidTrade();
            trade.Id = 10;
            trade.SampleSize = sampleSize;
            trade.SampleSizeId = 1;
            // Loaded out of order (EF doesn't order an included collection): Id 5 was added before Id 8.
            trade.AddOns = [new() { Id = 8, EntryPrice = 7680, ExitPrice = 7692, Volume = 1, PnL = 12 },
                            new() { Id = 5, EntryPrice = 7675, ExitPrice = 7692, Volume = null, PnL = 17 }];

            var newer = TradeValidatorTests.ValidTrade();
            newer.Id = 11;
            newer.SampleSize = sampleSize;
            newer.SampleSizeId = 1;
            newer.Date = trade.Date.AddDays(1); // the most recent trade isn't checked

            var report = await new TradeValidationService(TestUnitOfWork.Create([sampleSize], [trade, newer]), TestSettings.NoSpreads(), TimeProvider.System).ValidateAllAsync();

            report.InvalidTrades.Should().ContainSingle().Which.Issues.Should().ContainSingle()
                .Which.Message.Should().Be("Add-on 1: volume is missing.");
        }
    }
}
