using Models.Tom;
using TradingTools.Blazor.Services.Tom;

namespace TradingTools.Blazor.Tests.Tom
{
    /// <summary>Filtering the streams by month, date range, session and trader, and how a stream is named in the list.</summary>
    public class TomFilterTests
    {
        private static TomStream S(int id, int month, int day, TomSession? session = TomSession.EU, TomTrader? trader = TomTrader.Tom, int year = 2026) =>
            new() { Id = id, Date = new DateOnly(year, month, day), Session = session, Trader = trader };

        private static readonly TomStream[] Streams =
        [
            S(1, 9, 3, TomSession.EU, TomTrader.Tom),
            S(2, 9, 30, TomSession.US, TomTrader.Andi),
            S(3, 10, 1, TomSession.US, TomTrader.Tom),
            S(4, 10, 15, TomSession.EU, TomTrader.Andi),
            S(5, 10, 31, TomSession.EU, TomTrader.Tom),
            S(6, 9, 15, TomSession.EU, TomTrader.Tom, year: 2025),
        ];

        private static IEnumerable<int> Ids(TomFilter filter) => filter.Apply(Streams).Select(s => s.Id);

        [Fact]
        public void No_filter_shows_everything()
        {
            var filter = new TomFilter();

            filter.IsEmpty.Should().BeTrue();
            Ids(filter).Should().Equal(1, 2, 3, 4, 5, 6);
        }

        [Fact]
        public void A_month_shows_only_that_month_of_that_year()
        {
            Ids(new TomFilter(Month: new DateOnly(2026, 9, 1))).Should().Equal(1, 2);
            Ids(new TomFilter(Month: new DateOnly(2026, 10, 1))).Should().Equal(3, 4, 5);
            Ids(new TomFilter(Month: new DateOnly(2025, 9, 1))).Should().Equal(6);
        }

        [Fact]
        public void A_date_range_includes_both_ends()
        {
            Ids(new TomFilter(From: new DateOnly(2026, 9, 30), To: new DateOnly(2026, 10, 15))).Should().Equal(2, 3, 4);
        }

        [Fact]
        public void A_range_can_be_open_on_either_side()
        {
            Ids(new TomFilter(From: new DateOnly(2026, 10, 15))).Should().Equal(4, 5);
            Ids(new TomFilter(To: new DateOnly(2026, 9, 3))).Should().Equal(1, 6);
        }

        [Fact]
        public void A_single_day_is_a_range_from_and_to_that_day()
        {
            var day = new DateOnly(2026, 10, 1);
            Ids(new TomFilter(From: day, To: day)).Should().Equal(3);
        }

        [Fact]
        public void Session_and_trader_each_filter()
        {
            Ids(new TomFilter(Session: TomSession.US)).Should().Equal(2, 3);
            Ids(new TomFilter(Trader: TomTrader.Andi)).Should().Equal(2, 4);
        }

        [Fact]
        public void All_filters_apply_together()
        {
            var filter = new TomFilter(Month: new DateOnly(2026, 10, 1), Session: TomSession.EU, Trader: TomTrader.Tom);

            filter.IsEmpty.Should().BeFalse();
            Ids(filter).Should().Equal(5);
        }

        [Fact]
        public void A_stream_without_a_session_or_trader_only_shows_while_that_part_is_not_filtered()
        {
            var old = S(9, 10, 2, session: null, trader: null);

            new TomFilter().Matches(old).Should().BeTrue();
            new TomFilter(Month: new DateOnly(2026, 10, 1)).Matches(old).Should().BeTrue();
            new TomFilter(Session: TomSession.EU).Matches(old).Should().BeFalse();
            new TomFilter(Trader: TomTrader.Tom).Matches(old).Should().BeFalse();
        }

        [Fact]
        public void The_month_filter_offers_the_months_that_have_streams_newest_first()
        {
            TomFilter.MonthsOf(Streams).Should().Equal(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), new DateOnly(2025, 9, 1));
        }

        [Fact]
        public void A_stream_is_named_by_its_trader_and_session()
        {
            TomLabels.Of(S(1, 10, 1, TomSession.US, TomTrader.Andi)).Should().Be("Andi · US");
            TomLabels.Of(S(1, 10, 1, TomSession.EU, null)).Should().Be("EU");
        }

        [Fact]
        public void An_older_stream_keeps_its_title_or_the_site_of_its_link()
        {
            TomLabels.Of(new TomStream { Title = " DAX open ", Url = "https://www.youtube.com/watch?v=a" }).Should().Be("DAX open");
            TomLabels.Of(new TomStream { Url = "https://www.youtube.com/watch?v=a" }).Should().Be("youtube.com");
            TomLabels.Of(new TomStream()).Should().Be("Stream");
        }
    }
}
