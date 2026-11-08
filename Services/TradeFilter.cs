using System.Globalization;
using Models.Trades;
using Shared.Enums;

namespace TradingTools.Blazor.Services
{
    /// <summary>
    /// The filter of the Trades page: which trades to go through, whatever sample size they belong to. All parts apply
    /// together - e.g. DAX, wins only, from September to November. The period is either all dates or From / To (both
    /// days included, either end open). Without any part set the page shows its sample sizes as usual.
    /// </summary>
    /// <param name="AllDates">Every trade, whatever its date; From and To don't count then.</param>
    /// <param name="Symbol">One symbol only; null for all.</param>
    /// <param name="Outcome">Wins, losses or breakevens only; null for all.</param>
    public sealed record TradeFilter(
        bool AllDates = false, DateOnly? From = null, DateOnly? To = null, ESymbol? Symbol = null, EOutcome? Outcome = null)
    {
        /// <summary>Whether anything is filtered at all.</summary>
        public bool IsSet => AllDates || From is not null || To is not null || Symbol is not null || Outcome is not null;

        public DateOnly? EffectiveFrom => AllDates ? null : From;

        public DateOnly? EffectiveTo => AllDates ? null : To;

        /// <summary>From after To: nothing can be in it.</summary>
        public bool IsBackwards => EffectiveFrom is { } from && EffectiveTo is { } to && from > to;

        public bool Matches(BaseTrade trade) =>
            (EffectiveFrom is not { } from || trade.Date >= from)
            && (EffectiveTo is not { } to || trade.Date <= to)
            && (Symbol is not { } symbol || string.Equals(trade.Symbol?.Trim(), symbol.ToString(), StringComparison.OrdinalIgnoreCase))
            && (Outcome is not { } outcome || trade.Outcome == outcome);

        /// <summary>The trades that match, oldest first: by date, then by when they were logged, then by id.</summary>
        public IEnumerable<T> Apply<T>(IEnumerable<T> trades) where T : BaseTrade =>
            trades.Where(Matches).OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id);

        /// <summary>"DAX · wins · 1 Sep 2026 - 30 Nov 2026", "losses · all dates", "US500 · from 1 Feb 2026".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Symbol is { } symbol) parts.Add(symbol.ToString());
            if (Outcome is { } outcome) parts.Add(outcome switch { EOutcome.Win => "wins", EOutcome.Loss => "losses", _ => "breakevens" });
            parts.Add((EffectiveFrom, EffectiveTo) switch
            {
                ({ } from, { } to) => $"{Format(from)} - {Format(to)}",
                ({ } from, null) => $"from {Format(from)}",
                (null, { } to) => $"until {Format(to)}",
                _ => "all dates"
            });
            return string.Join(" · ", parts);
        }

        private static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
