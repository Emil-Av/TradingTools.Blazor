namespace TradingTools.Blazor.Services.Validation
{
    /// <summary>
    /// The P&amp;L of a trade is the number of points won or lost: always positive, whatever the
    /// direction or outcome - the sign lives in the trade's Outcome.
    /// </summary>
    public static class TradePnl
    {
        /// <summary>Two P&amp;L values closer than this are the same (prices are quoted to 2 decimals).</summary>
        public const double Tolerance = 0.005;

        /// <summary>
        /// |exit - entry|, rounded to 2 decimals so price subtraction residue (7671.25 - 7655.1 =
        /// 16.150000000000546) doesn't leak into the stored value. Null when either price is missing
        /// or not a real price (0 or negative).
        /// </summary>
        public static double? Points(double? entryPrice, double? exitPrice) =>
            entryPrice is > 0 && exitPrice is > 0
                ? Math.Round(Math.Abs(exitPrice.Value - entryPrice.Value), 2, MidpointRounding.AwayFromZero)
                : null;
    }
}
