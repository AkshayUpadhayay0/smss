namespace smss_api_service_layer.dto
{
    // Examination / Report Card, STAGE 5a: report card settings, the design chooser and the preview.

    public class ReportCardDesignDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Modes { get; set; } = new();
        public string Orientation { get; set; } = "";
        /// <summary>False for a design that is not built yet ("coming soon"): it cannot be chosen or printed.</summary>
        public bool Available { get; set; }
    }

    public class ReportCardSchemeChoiceDto
    {
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = "";
        public string EvaluationMode { get; set; } = "";
        public string TemplateKey { get; set; } = "";
        public List<string> ClassNames { get; set; } = new();
    }

    public class ReportCardSettingsDto
    {
        public string PrimaryColor { get; set; } = "";
        public string SecondaryColor { get; set; } = "";
        public string? HeaderLine { get; set; }
        public string? RecognitionText { get; set; }
        public string? UdiseCode { get; set; }
        public string? FooterText { get; set; }
        public string? RulesText { get; set; }
        public bool HasSignature { get; set; }
        public bool ShowCover { get; set; }
        public bool ShowRules { get; set; }
        public bool ShowAttendance { get; set; }
        public bool ShowGradeLegend { get; set; }
        /// <summary>True when the school has never saved settings (the defaults are in use).</summary>
        public bool IsDefault { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string SchoolName { get; set; } = "";
        /// <summary>Whether the school record has a logo (the cards print it; it is read from the school record, not stored here).</summary>
        public bool HasLogo { get; set; }
        public List<ReportCardDesignDto> Designs { get; set; } = new();
        public List<ReportCardSchemeChoiceDto> Schemes { get; set; } = new();
        public string DefaultPrimaryColor { get; set; } = "";
        public string DefaultSecondaryColor { get; set; } = "";
    }

    public class ReportCardSettingsRequest
    {
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? HeaderLine { get; set; }
        public string? RecognitionText { get; set; }
        public string? UdiseCode { get; set; }
        public string? FooterText { get; set; }
        public string? RulesText { get; set; }
        public bool ShowCover { get; set; }
        public bool ShowRules { get; set; }
        public bool ShowAttendance { get; set; } = true;
        public bool ShowGradeLegend { get; set; } = true;
    }

    public class ReportCardSignatureDto { public bool HasSignature { get; set; } }

    public class SetSchemeDesignRequest
    {
        public long SchemeId { get; set; }
        public string? TemplateKey { get; set; }
    }

    /// <summary>A live preview: the design and the settings to draw (unsaved edits included), on built-in sample data or on one real student of a published-or-not section.</summary>
    public class ReportCardPreviewRequest
    {
        public string? TemplateKey { get; set; }
        /// <summary>The settings as they are on screen right now. Null = the saved settings.</summary>
        public ReportCardSettingsRequest? Settings { get; set; }
        /// <summary>1 to 3 terms for the sample card.</summary>
        public int Terms { get; set; } = 2;
        // a real student (School Admin and the class teacher only; the Principal sees samples)
        public long? SchemeId { get; set; }
        public long? SectionId { get; set; }
        public string? Scope { get; set; }
        public long? TermId { get; set; }
        public string? StudentId { get; set; }
    }

    public class ReportCardPreviewDto
    {
        public string TemplateKey { get; set; } = "";
        public bool IsSample { get; set; }
        public int PageCount { get; set; }
        /// <summary>PNG images, base64, one per page (at most 4 are returned: the cover, the first card pages).</summary>
        public List<string> Pages { get; set; } = new();
    }
}
