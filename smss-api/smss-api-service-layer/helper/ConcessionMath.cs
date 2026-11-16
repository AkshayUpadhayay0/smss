namespace smss_api_service_layer.helper
{
    /// <summary>
    /// The arithmetic of a concession chosen at payment time. Everything is exact decimal / integer-cent arithmetic (no floating point), and the rounding is the same
    /// ROUND_HALF_UP to 2 decimals as <see cref="TaxMath"/>.
    /// </summary>
    public static class ConcessionMath
    {
        public const string Percentage = "PERCENTAGE";
        public const string Fixed = "FIXED";

        /// <summary>
        /// The discount on <paramref name="baseAmount"/> (the sum of the allocations the concession applies to).
        /// PERCENTAGE: round-half-up(base x value / 100, 2 dp). FIXED: the value, applied once per payment. Either way never more than the base.
        /// </summary>
        public static decimal DiscountOn(string type, decimal value, decimal baseAmount)
        {
            if (baseAmount <= 0 || value <= 0) return 0m;
            var d = string.Equals(type, Percentage, StringComparison.OrdinalIgnoreCase)
                ? Math.Round(baseAmount * value / 100m, 2, MidpointRounding.AwayFromZero)
                : Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return Math.Min(d, baseAmount);
        }

        /// <summary>
        /// Spreads <paramref name="discount"/> over the matching allocations (given in a fixed order: oldest due first) so the parts add up to the discount EXACTLY and no part
        /// is more than its allocation. Method: largest remainder, in whole cents. Each allocation first gets floor(discount x allocation / base) cents; the few cents left over
        /// (always fewer than the number of allocations) go one each to the allocations with the largest fractional remainder, ties to the earlier one. Because
        /// discount &lt;= base, every exact share is at most its allocation, so the rounded-up share never exceeds it either.
        /// </summary>
        public static decimal[] Spread(decimal discount, IReadOnlyList<decimal> allocations)
        {
            var n = allocations.Count;
            var parts = new decimal[n];
            if (n == 0 || discount <= 0) return parts;

            var d = Cents(discount);
            var a = allocations.Select(Cents).ToArray();
            var total = a.Sum();
            if (total <= 0) return parts;
            if (d > total) throw new ArgumentException("The discount can't exceed the allocations it is spread over.", nameof(discount));

            var floor = new long[n];
            var rem = new long[n];
            for (var i = 0; i < n; i++)
            {
                // d and a are at most ~10^12 cents; the product fits in a decimal exactly
                var product = (decimal)d * a[i];
                floor[i] = (long)decimal.Floor(product / total);
                rem[i] = (long)(product - (decimal)floor[i] * total);   // remainder out of `total`
            }
            var left = d - floor.Sum();
            foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => rem[i]).ThenBy(i => i).Take((int)left)) floor[i]++;

            for (var i = 0; i < n; i++) parts[i] = floor[i] / 100m;
            return parts;
        }

        private static long Cents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }
}
