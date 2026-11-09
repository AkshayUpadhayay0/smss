using System.Net.Mail;
using System.Text.RegularExpressions;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    /// <summary>Cleaning and validation of a public enquiry. Pure functions (no database), so they are unit tested. The Angular form applies the same rules.</summary>
    public static class EnquiryRules
    {
        public const int MaxMessage = 2000;
        public const int MaxNotes = 4000;

        private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Phone = new(@"^\+?[0-9][0-9 ()\-]{5,28}[0-9]$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        /// <summary>Trims, drops HTML tags and control characters (a message keeps its line breaks), and turns blank into null.</summary>
        public static string? Clean(string? value, bool keepLineBreaks = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var noTags = Tags.Replace(value, string.Empty);
            var chars = noTags.Where(c => !char.IsControl(c) || (keepLineBreaks && (c == '\n' || c == '\r' || c == '\t')) || c == ' ').ToArray();
            var cleaned = new string(chars).Trim();
            if (!keepLineBreaks) cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return cleaned.Length == 0 ? null : cleaned;
        }

        public static bool IsValidEmail(string email)
        {
            if (email.Length > 200 || email.Contains(' ')) return false;
            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email && addr.Host.Contains('.');
            }
            catch (FormatException) { return false; }
        }

        public static bool IsValidPhone(string phone) => phone.Length <= 30 && Phone.IsMatch(phone);

        /// <summary>
        /// Cleans <paramref name="r"/> in place and returns field -> message for everything wrong (empty when valid).
        /// Contact needs name, email, message; Demo needs name, school name, email, phone. Consent is always required.
        /// </summary>
        public static Dictionary<string, string> CleanAndValidate(CreateEnquiryRequest r, DateOnly today)
        {
            var errors = new Dictionary<string, string>();

            r.EnquiryType = r.EnquiryType?.Trim().ToUpperInvariant();
            r.FullName = Clean(r.FullName);
            r.Designation = Clean(r.Designation);
            r.SchoolName = Clean(r.SchoolName);
            r.Email = Clean(r.Email)?.ToLowerInvariant();
            r.Phone = Clean(r.Phone);
            r.City = Clean(r.City);
            r.StudentCountRange = Clean(r.StudentCountRange);
            r.Subject = Clean(r.Subject);
            r.Message = Clean(r.Message, keepLineBreaks: true);
            r.PreferredTimeSlot = Clean(r.PreferredTimeSlot);

            if (r.EnquiryType is null || !EnquiryTypes.All.Contains(r.EnquiryType)) { errors["enquiryType"] = "Choose a valid enquiry type."; return errors; }
            var demo = r.EnquiryType == EnquiryTypes.Demo;

            Required(errors, "fullName", r.FullName, 150, "Full name");
            Max(errors, "designation", r.Designation, 100, "Designation");
            Max(errors, "city", r.City, 100, "City");
            Max(errors, "subject", r.Subject, 200, "Subject");

            if (r.Email is null) errors["email"] = "Email is required.";
            else if (!IsValidEmail(r.Email)) errors["email"] = "Enter a valid email address.";

            if (r.Phone is null) { if (demo) errors["phone"] = "Phone number is required."; }
            else if (!IsValidPhone(r.Phone)) errors["phone"] = "Enter a valid phone number.";

            if (demo)
            {
                Required(errors, "schoolName", r.SchoolName, 200, "School name");
                if (r.StudentCountRange != null && !EnquiryChoices.StudentCountRanges.Contains(r.StudentCountRange)) errors["studentCountRange"] = "Choose one of the listed ranges.";
                if (r.PreferredTimeSlot != null && !EnquiryChoices.TimeSlots.Contains(r.PreferredTimeSlot)) errors["preferredTimeSlot"] = "Choose one of the listed time slots.";
                if (r.PreferredDemoDate is { } d && (d < today || d > today.AddYears(1))) errors["preferredDemoDate"] = "Choose a date from today up to one year ahead.";
                if (r.Message != null && r.Message.Length > MaxMessage) errors["message"] = $"Message can be at most {MaxMessage} characters.";
            }
            else
            {
                Max(errors, "schoolName", r.SchoolName, 200, "School name");
                if (r.Message is null) errors["message"] = "Message is required.";
                else if (r.Message.Length > MaxMessage) errors["message"] = $"Message can be at most {MaxMessage} characters.";
                // Fields that only belong to a demo request are dropped from a contact message
                r.StudentCountRange = null; r.PreferredDemoDate = null; r.PreferredTimeSlot = null;
            }

            if (!r.ConsentGiven) errors["consentGiven"] = "Please accept the privacy policy to continue.";
            return errors;
        }

        private static void Required(Dictionary<string, string> errors, string field, string? value, int max, string label)
        {
            if (value is null) errors[field] = $"{label} is required.";
            else Max(errors, field, value, max, label);
        }

        private static void Max(Dictionary<string, string> errors, string field, string? value, int max, string label)
        {
            if (value != null && value.Length > max) errors[field] = $"{label} can be at most {max} characters.";
        }
    }
}
