namespace smss_api_service_layer.dto
{
    /// <summary>One of the built-in designs, with the colours it ships with.</summary>
    public class IdCardTemplateInfoDto
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string DefaultPrimaryColor { get; set; } = null!;
        public string DefaultSecondaryColor { get; set; } = null!;
    }

    /// <summary>The school's ID-card settings as the settings screen shows them. With no saved row these are the defaults and <see cref="IsDefault"/> is true.</summary>
    public class IdCardSettingsDto
    {
        public string TemplateCode { get; set; } = null!;
        public string PrimaryColor { get; set; } = null!;
        public string SecondaryColor { get; set; } = null!;
        public string? Tagline { get; set; }
        public string? Website { get; set; }
        public string? Email { get; set; }
        public string? TermsText { get; set; }
        /// <summary>Whether a principal signature is on file (the file itself is private and is never sent).</summary>
        public bool HasSignature { get; set; }
        public bool ShowDob { get; set; }
        public bool ShowAddress { get; set; }
        public bool ShowBloodGroup { get; set; }
        public bool ShowMotherName { get; set; }
        public bool IsDefault { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<IdCardTemplateInfoDto> Templates { get; set; } = new();
    }

    /// <summary>What the school can set. Never carries a school id or a signature path (the school comes from the token; the signature has its own upload).</summary>
    public class IdCardSettingsRequest
    {
        public string? TemplateCode { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? Tagline { get; set; }
        public string? Website { get; set; }
        public string? Email { get; set; }
        public string? TermsText { get; set; }
        public bool ShowDob { get; set; } = true;
        public bool ShowAddress { get; set; } = true;
        public bool ShowBloodGroup { get; set; } = true;
        public bool ShowMotherName { get; set; } = true;
    }

    /// <summary>A preview of a template with the (possibly unsaved) settings of the screen; <see cref="StudentId"/> picks a real student, else built-in sample data.</summary>
    public class IdCardPreviewRequest : IdCardSettingsRequest
    {
        public string? StudentId { get; set; }
    }

    public class IdCardPreviewDto
    {
        public string TemplateCode { get; set; } = null!;
        /// <summary>PNG, base64.</summary>
        public string FrontPng { get; set; } = null!;
        public string BackPng { get; set; } = null!;
        public bool IsSample { get; set; }
    }

    public class IdCardSignatureDto
    {
        public bool HasSignature { get; set; }
    }
}
