using System.Globalization;
using Models.Trades;

namespace TradingTools.Blazor.Services
{
    /// <summary>
    /// A date range for the Trades page: the trades dated from <see cref="From"/> to <see cref="To"/>, both days
    /// included, whatever sample size they belong to. Either end can be left open.
    /// </summary>
    public sealed record TradeDateRange(DateOnly? From, DateOnly? To)
    {
        /// <summary>Whether there is a range at all (at least one end).</summary>
        public bool IsSet => From is not null || To is not null;

        /// <summary>From after To: nothing can be in it.</summary>
        public bool IsBackwards => From is { } from && To is { } to && from > to;

        public bool Includes(DateOnly date) => (From is not { } from || date >= from) && (To is not { } to || date <= to);

        /// <summary>"1 Oct 2026 - 15 Oct 2026", "from 1 Oct 2026", "until 15 Oct 2026".</summary>
        public string Describe() => (From, To) switch
        {
            ({ } from, { } to) => $"{Format(from)} - {Format(to)}",
            ({ } from, null) => $"from {Format(from)}",
            (null, { } to) => $"until {Format(to)}",
            _ => "all dates"
        };

        /// <summary>The trades in the range, oldest first: by date, then by when they were logged, then by id.</summary>
        public IEnumerable<T> Apply<T>(IEnumerable<T> trades) where T : BaseTrade =>
            trades.Where(t => Includes(t.Date)).OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id);

        private static string Format(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
