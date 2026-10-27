using System.Globalization;
using Models.Tom;

namespace TradingTools.Blazor.Services.Tom
{
    /// <summary>The video link of a stream, and links to a moment in it.</summary>
    public static class TomLinks
    {
        /// <summary>
        /// Whether the text is a web link (http or https) - the only kind that is ever put in a link on the page, so a
        /// typed "javascript:..." can't become one.
        /// </summary>
        public static bool IsWebLink(string? url) =>
            Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// The link at a moment of the video: YouTube's "t" parameter (e.g. "...&amp;t=5025s"), replacing one that's
        /// already there. The link itself without a moment; null when it isn't a web link.
        /// </summary>
        public static string? At(string? url, int? seconds)
        {
            if (!IsWebLink(url)) return null;
            if (seconds is not { } s) return url!.Trim();
            var builder = new UriBuilder(url!.Trim());

            var query = builder.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("t=", StringComparison.OrdinalIgnoreCase) && part != "t")
                .Append($"t={s}s");
            builder.Query = string.Join('&', query);
            return builder.Uri.AbsoluteUri;
        }
    }

    /// <summary>A moment in a video, typed as "1:23:45", "23:45" or "45" (seconds).</summary>
    public static class VideoTime
    {
        /// <summary>
        /// Reads a time. True with null for an empty text (no time), true with the seconds for a valid one, false when
        /// it isn't a time ("abc", "1:75", "-5").
        /// </summary>
        public static bool TryParse(string? text, out int? seconds)
        {
            seconds = null;
            if (string.IsNullOrWhiteSpace(text)) return true;

            var parts = text.Trim().Split(':');
            if (parts.Length > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit))) return false;

            var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            // Everything after the first part is minutes or seconds: 0-59.
            if (numbers.Skip(1).Any(n => n > 59)) return false;

            long total = numbers.Aggregate(0L, (acc, n) => acc * 60 + n);
            if (total > int.MaxValue) return false;

            seconds = (int)total;
            return true;
        }

        /// <summary>"1:23:45" from an hour on, "23:45" below that.</summary>
        public static string Format(int seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return seconds >= 3600
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes}:{time.Seconds:00}";
        }
    }

    /// <summary>The streams of one month.</summary>
    public sealed record TomMonth(int Year, int Month, IReadOnlyList<TomStream> Streams)
    {
        public string Label => new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    public static class TomMonths
    {
        /// <summary>The streams grouped by the month of their date: the newest month first, in it the newest stream first.</summary>
        public static IReadOnlyList<TomMonth> Group(IEnumerable<TomStream> streams) =>
            [.. streams
                .GroupBy(s => (s.Date.Year, s.Date.Month))
                .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
                .Select(g => new TomMonth(g.Key.Year, g.Key.Month,
                    [.. g.OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)]))];
    }
}

namespace TradingTools.Blazor.Services.Tom
{
    /// <summary>
    /// Which streams the list shows. Every part is optional and they all apply together: a month, a date range
    /// (both ends included), a session and a trader. A stream without a session or trader (added before those were
    /// recorded) only matches when that part isn't filtered.
    /// </summary>
    public sealed record TomFilter(
        DateOnly? Month = null, DateOnly? From = null, DateOnly? To = null, TomSession? Session = null, TomTrader? Trader = null)
    {
        public bool IsEmpty => Month is null && From is null && To is null && Session is null && Trader is null;

        public bool Matches(TomStream stream) =>
            (Month is not { } month || (stream.Date.Year == month.Year && stream.Date.Month == month.Month))
            && (From is not { } from || stream.Date >= from)
            && (To is not { } to || stream.Date <= to)
            && (Session is not { } session || stream.Session == session)
            && (Trader is not { } trader || stream.Trader == trader);

        public IEnumerable<TomStream> Apply(IEnumerable<TomStream> streams) => streams.Where(Matches);

        /// <summary>The months that have streams, newest first (the first day of each), for the month filter.</summary>
        public static IReadOnlyList<DateOnly> MonthsOf(IEnumerable<TomStream> streams) =>
            [.. streams.Select(s => new DateOnly(s.Date.Year, s.Date.Month, 1)).Distinct().OrderDescending()];
    }

    public static class TomLabels
    {
        /// <summary>
        /// How a stream is named in the list: "Tom · EU". A stream from before traders and sessions were recorded keeps
        /// the title it was given, or the site of its link.
        /// </summary>
        public static string Of(TomStream stream)
        {
            var parts = new[] { stream.Trader?.ToString(), stream.Session?.ToString() }.OfType<string>().ToList();
            if (parts.Count > 0) return string.Join(" · ", parts);
            if (!string.IsNullOrWhiteSpace(stream.Title)) return stream.Title.Trim();
            return TomLinks.IsWebLink(stream.Url) ? new Uri(stream.Url!.Trim()).Host.Replace("www.", "") : "Stream";
        }
    }
}
