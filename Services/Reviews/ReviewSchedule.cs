using System.Text.RegularExpressions;
using Models;

namespace TradingTools.Blazor.Services.Reviews
{
    /// <summary>The five reviews of a sample size: one per 5 trades, then one for the whole sample size.</summary>
    public enum ReviewKind { First, Second, Third, Fourth, Summary }

    /// <summary>
    /// When a review is due. A sample size has 20 trades. After every 5 of them a review of those 5 trades is
    /// to be done (First: trades 1-5, Second: 6-10, Third: 11-15, Fourth: 16-20); once the sample size is
    /// complete, a Summary of the whole sample size too. A review is due as soon as the trade that completes its
    /// block has been logged ("when the 5th trade is opened"), and stays due until it has text.
    /// </summary>
    public static partial class ReviewSchedule
    {
        public const int TradesPerReview = 5;
        public const int TradesPerSampleSize = 20;

        public static IReadOnlyList<ReviewKind> All { get; } = Enum.GetValues<ReviewKind>();

        /// <summary>How many trades the sample size needs before the review is due.</summary>
        public static int TradesNeeded(ReviewKind kind) => kind switch
        {
            ReviewKind.First => TradesPerReview,
            ReviewKind.Second => 2 * TradesPerReview,
            ReviewKind.Third => 3 * TradesPerReview,
            ReviewKind.Fourth => 4 * TradesPerReview,
            ReviewKind.Summary => TradesPerSampleSize,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string Label(ReviewKind kind) => kind == ReviewKind.Summary ? "Summary" : $"{kind} review";

        /// <summary>What the review looks at, e.g. "trades 6-10" or "the whole sample size".</summary>
        public static string Covers(ReviewKind kind) => kind switch
        {
            ReviewKind.Summary => "the whole sample size",
            _ => $"trades {TradesNeeded(kind) - TradesPerReview + 1}-{TradesNeeded(kind)}"
        };

        /// <summary>The review's text as stored (HTML from the editor), or null.</summary>
        public static string? TextOf(Review review, ReviewKind kind) => kind switch
        {
            ReviewKind.First => review.First,
            ReviewKind.Second => review.Second,
            ReviewKind.Third => review.Third,
            ReviewKind.Fourth => review.Forth,
            ReviewKind.Summary => review.Summary,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static void SetText(Review review, ReviewKind kind, string? html)
        {
            switch (kind)
            {
                case ReviewKind.First: review.First = html; break;
                case ReviewKind.Second: review.Second = html; break;
                case ReviewKind.Third: review.Third = html; break;
                case ReviewKind.Fourth: review.Forth = html; break;
                case ReviewKind.Summary: review.Summary = html; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// Whether a review has been written: some text is left once the editor's HTML (empty paragraphs, line
        /// breaks, non-breaking spaces) is stripped away.
        /// </summary>
        public static bool IsFilled(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;

            string text = HtmlTag().Replace(html, string.Empty).Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
            return !string.IsNullOrWhiteSpace(System.Net.WebUtility.HtmlDecode(text).Replace(' ', ' '));
        }

        /// <summary>The reviews that are due for a sample size with <paramref name="tradeCount"/> trades and no text yet, oldest first.</summary>
        public static IReadOnlyList<ReviewKind> Due(int tradeCount, Review review) =>
            [.. All.Where(kind => tradeCount >= TradesNeeded(kind) && !IsFilled(TextOf(review, kind)))];

        /// <summary>The reviews that became due exactly when the sample size reached <paramref name="tradeCount"/> trades.</summary>
        public static IReadOnlyList<ReviewKind> ReachedAt(int tradeCount) =>
            [.. All.Where(kind => TradesNeeded(kind) == tradeCount)];

        [GeneratedRegex("<[^>]*>")]
        private static partial Regex HtmlTag();
    }
}
