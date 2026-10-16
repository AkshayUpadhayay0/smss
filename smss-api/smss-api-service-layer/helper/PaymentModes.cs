namespace smss_api_service_layer.helper
{
    /// <summary>The ways a fee can be paid. Stored exactly as spelled here.</summary>
    public static class PaymentModes
    {
        public static readonly IReadOnlyList<string> All = new[] { "Cash", "Cheque", "UPI", "Card", "Bank Transfer", "Online" };

        /// <summary>The canonical spelling for what the client sent (any case), or null when it is not a payment mode.</summary>
        public static string? Canonical(string? value) =>
            All.FirstOrDefault(m => string.Equals(m, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
