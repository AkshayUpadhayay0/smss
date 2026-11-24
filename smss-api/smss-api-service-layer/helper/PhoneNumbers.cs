using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// WhatsApp numbers are stored WITH the country code ("+919876543210": the form Meta's WhatsApp Cloud API needs). A 10-digit Indian mobile number gets +91; "+91 98765 43210", "919876543210"
    /// and "09876543210" are read as the same number; another country's number must start with "+". The ordinary mobile number columns keep their existing 10-digit format.
    /// </summary>
    public static class PhoneNumbers
    {
        private static readonly Regex Separators = new(@"[\s\-().]", RegexOptions.Compiled);
        private static readonly Regex Digits = new(@"^[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex IndianMobile = new(@"^[6-9][0-9]{9}$", RegexOptions.Compiled);
        public const string StoredPattern = @"^\+[1-9][0-9]{7,14}$";

        /// <summary>The number as stored ("+91..."), or null when the text is blank or is not a valid number.</summary>
        public static string? Normalise(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = Separators.Replace(raw.Trim(), "");
            if (s.StartsWith('+'))
            {
                var rest = s[1..];
                if (!Digits.IsMatch(rest) || rest.Length < 8 || rest.Length > 15 || rest[0] == '0') return null;
                if (rest.StartsWith("91")) return IndianMobile.IsMatch(rest[2..]) && rest.Length == 12 ? "+" + rest : null;   // +91 must be a real Indian mobile number
                return "+" + rest;
            }
            if (!Digits.IsMatch(s)) return null;
            if (s.Length == 10 && IndianMobile.IsMatch(s)) return "+91" + s;
            if (s.Length == 12 && s.StartsWith("91") && IndianMobile.IsMatch(s[2..])) return "+" + s;
            if (s.Length == 11 && s[0] == '0' && IndianMobile.IsMatch(s[1..])) return "+91" + s[1..];
            return null;
        }

        /// <summary>
        /// The WhatsApp number to store: the mobile number itself when "same as mobile number" is ticked (the server decides, whatever number came with the tick), else the number given.
        /// Null when none can be made.
        /// </summary>
        public static string? WhatsAppFor(string? whatsapp, bool sameAsMobile, string? mobile) => Normalise(sameAsMobile ? mobile : whatsapp);

        /// <summary>The message for a missing or invalid WhatsApp number, or null when it is fine.</summary>
        public static string? WhatsAppError(string? whatsapp, bool sameAsMobile, string? mobile, string who)
        {
            if (sameAsMobile)
                return Normalise(mobile) == null ? $"{who} mobile number is not valid, so it cannot be used as the WhatsApp number." : null;
            if (string.IsNullOrWhiteSpace(whatsapp)) return $"{who} WhatsApp number is required (tick \"same as mobile number\" if it is the same).";
            return Normalise(whatsapp) == null ? $"{who} WhatsApp number is not valid. Enter a 10-digit mobile number, or a number with its country code, for example +91 98765 43210." : null;
        }

        /// <summary>The message for a missing or badly formed email, or null when it is fine. Format only: an email is not required to be unique.</summary>
        public static string? EmailError(string? email, string who)
        {
            if (string.IsNullOrWhiteSpace(email)) return $"{who} email is required.";
            if (email.Trim().Length > 150) return $"{who} email can be at most 150 characters.";
            return new EmailAddressAttribute().IsValid(email.Trim()) ? null : $"{who} email is not a valid email address.";
        }
    }
}
