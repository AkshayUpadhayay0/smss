using System.ComponentModel.DataAnnotations;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper.idcard
{
    /// <summary>A settings request after validation: trimmed, blanks turned into null, template and colours filled in.</summary>
    public sealed record IdCardSettingsClean(string TemplateCode, string PrimaryColor, string SecondaryColor, string? Tagline, string? Website, string? Email,
        string? TermsText, bool ShowDob, bool ShowAddress, bool ShowBloodGroup, bool ShowMotherName);

    /// <summary>The one place that decides whether ID-card settings are acceptable (used by Save and by Preview).</summary>
    public static class IdCardSettingsRules
    {
        public const int MaxTagline = 120, MaxWebsite = 150, MaxEmail = 150, MaxTerms = 300, MaxTermsLines = 5;

        public static (IdCardSettingsClean? Clean, string? Error) Validate(IdCardSettingsRequest r)
        {
            var template = Blank(r.TemplateCode) ?? IdCardTemplates.DefaultCode;
            var t = IdCardTemplates.Find(template);
            if (t == null)
                return (null, $"Unknown template. Choose one of: {string.Join(", ", IdCardTemplates.All.Select(x => x.Code))}.");

            var primary = Blank(r.PrimaryColor) ?? t.DefaultPrimary;
            var secondary = Blank(r.SecondaryColor) ?? t.DefaultSecondary;
            if (!IdCardColors.IsHex(primary)) return (null, "Primary colour must be a hex colour like #1E5BD8.");
            if (!IdCardColors.IsHex(secondary)) return (null, "Secondary colour must be a hex colour like #F5A623.");

            var tagline = Clean(r.Tagline);
            if (tagline != null && tagline.Length > MaxTagline) return (null, $"Tagline can be at most {MaxTagline} characters.");

            var website = Clean(r.Website);
            if (website != null && (website.Length > MaxWebsite || website.Any(char.IsWhiteSpace) || website.IndexOfAny(new[] { '<', '>', '"', '\'' }) >= 0))
                return (null, $"Website must be a single address of at most {MaxWebsite} characters (no spaces).");

            var email = Clean(r.Email);
            if (email != null && (email.Length > MaxEmail || !new EmailAddressAttribute().IsValid(email)))
                return (null, "Email is not a valid e-mail address.");

            string? terms = null;
            if (!string.IsNullOrWhiteSpace(r.TermsText))
            {
                var lines = r.TermsText.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(StripControl).Where(l => l.Length > 0).ToList();
                if (lines.Count > MaxTermsLines) return (null, $"Terms can have at most {MaxTermsLines} short lines.");
                terms = string.Join('\n', lines);
                if (terms.Length > MaxTerms) return (null, $"Terms can be at most {MaxTerms} characters.");
                if (terms.Length == 0) terms = null;
            }

            return (new IdCardSettingsClean(template, primary.ToUpperInvariant(), secondary.ToUpperInvariant(), tagline, website, email, terms,
                r.ShowDob, r.ShowAddress, r.ShowBloodGroup, r.ShowMotherName), null);
        }

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static string? Clean(string? s) => Blank(s) is { } v ? StripControl(v) : null;
        private static string StripControl(string s) => new(s.Where(c => !char.IsControl(c)).ToArray());
    }
}
