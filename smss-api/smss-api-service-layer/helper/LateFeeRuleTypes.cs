namespace smss_api_service_layer.helper
{
    public static class LateFeeRuleTypes
    {
        public const string Flat = "FLAT", PerDay = "PER_DAY", Percent = "PERCENT";
        public static readonly string[] All = { Flat, PerDay, Percent };
    }
}
