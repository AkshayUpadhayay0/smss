namespace smss_api_service_layer.helper
{
    /// <summary>
    /// The one place tax is computed. Tax is ADDED ON TOP of the fee: tax = ROUND_HALF_UP(fee x rate / 100, 2 decimals), total = fee + tax.
    /// Exact: a fee (2 decimals) times a rate (2 decimals) is exact in <see cref="decimal"/>, so "half up" is decided on the true value, never a floating-point residue.
    /// Half rounds away from zero (the same mode the report card engine uses unless a scheme says TRUNCATE); amounts here are never negative.
    /// </summary>
    public static class TaxMath
    {
        public static decimal TaxOn(decimal feeAmount, decimal ratePercent) =>
            Math.Round(feeAmount * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
