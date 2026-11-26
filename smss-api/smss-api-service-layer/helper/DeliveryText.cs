using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper
{
    /// <summary>What may be written to the delivery log: a masked recipient and a short sanitised error. Never a token, a link, a password or a message text.</summary>
    public static class DeliveryText
    {
        private static readonly Regex Url = new(@"https?://\S+|www\.\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LongToken = new(@"[A-Za-z0-9_\-+/=]{20,}", RegexOptions.Compiled);
        private static readonly Regex Digits = new(@"[0-9]{7,}", RegexOptions.Compiled);
        public const int MaxError = 300;

        /// <summary>"+919876543210" -> "+91******3210"; an email "asha@gmail.com" -> "a***@gmail.com".</summary>
        public static string? MaskRecipient(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = value.Trim();
            var at = v.IndexOf('@');
            if (at > 0) return v[0] + "***" + v[at..];
            var digits = new string(v.Where(char.IsDigit).ToArray());
            if (digits.Length < 8) return "***";
            var plus = v.StartsWith('+');   // keep the country code ("+91") and the last four digits
            return (plus ? "+" + digits[..2] : "") + new string('*', digits.Length - (plus ? 2 : 0) - 4) + digits[^4..];
        }

        /// <summary>A short, safe error text: links, long token-like strings and long digit runs (numbers) are removed, and the length is capped.</summary>
        public static string? Sanitise(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = Url.Replace(text, "[link]");
            t = LongToken.Replace(t, "[removed]");
            t = Digits.Replace(t, "[number]");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t.Length <= MaxError ? t : t[..MaxError];
        }
    }
}
