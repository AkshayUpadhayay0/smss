using smss_api_service_layer.helper.reportcard;

namespace smss_api_service_layer.dto
{
    // Examination / Report Card, STAGE 3: results and publishing.

    public class ResultStudentDto
    {
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string AdmissionNumber { get; set; } = "";
        /// <summary>PASS, FAIL or INCOMPLETE (INCOMPLETE students are left out of a publication).</summary>
        public string Result { get; set; } = "";
        public decimal? Obtained { get; set; }
        public decimal? OutOf { get; set; }
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public int? Rank { get; set; }
        public List<string> FailedSubjects { get; set; } = new();
    }

    public class ResultPreviewDto
    {
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = "";
        public long SectionId { get; set; }
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string Scope { get; set; } = "";
        public long? TermId { get; set; }
        public string? TermName { get; set; }
        public string Mode { get; set; } = "";
        public string TemplateKey { get; set; } = "";
        public bool TemplateAvailable { get; set; }
        public string? TemplateMessage { get; set; }
        public bool ShowRank { get; set; }
        /// <summary>Terms of the scope that are not locked for the section yet. Publishing needs this empty.</summary>
        public List<string> UnlockedTerms { get; set; } = new();
        public bool CanPublish { get; set; }
        public bool CanDownloadDraft { get; set; }
        public string AttendanceSource { get; set; } = "";
        public List<string> UnmarkedDates { get; set; } = new();
        public List<ResultStudentDto> Students { get; set; } = new();
        public int IncompleteCount { get; set; }
        public PublicationDto? Current { get; set; }
    }

    public class PublishRequest
    {
        public long SchemeId { get; set; }
        public long SectionId { get; set; }
        public string Scope { get; set; } = "";
        public long? TermId { get; set; }
        public string? Note { get; set; }
        public bool ConfirmExcluded { get; set; }
        public bool AcknowledgeAttendance { get; set; }
    }

    public class PublishResultDto
    {
        public bool Published { get; set; }
        public bool NeedsConfirmation { get; set; }
        /// <summary>EXCLUDED or ATTENDANCE when a confirmation is wanted.</summary>
        public string? Reason { get; set; }
        public List<ExcludedStudentDto> Excluded { get; set; } = new();
        public List<string> UnmarkedDates { get; set; } = new();
        public PublicationDto? Publication { get; set; }
    }

    public class PublicationDto
    {
        public long PublicationId { get; set; }
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = "";
        public long SectionId { get; set; }
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string Scope { get; set; } = "";
        public long? TermId { get; set; }
        public string? TermName { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = "";
        public DateTime PublishedAt { get; set; }
        public string? Note { get; set; }
        public int StudentCount { get; set; }
        public int ExcludedCount { get; set; }
    }

    public class PublicationCardDto
    {
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string AdmissionNumber { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string Result { get; set; } = "";
        public decimal? Obtained { get; set; }
        public decimal? OutOf { get; set; }
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public int? Rank { get; set; }
    }

    public class PublicationDetailDto
    {
        public PublicationDto Publication { get; set; } = new();
        public List<PublicationCardDto> Cards { get; set; } = new();
        public List<ExcludedStudentDto> Excluded { get; set; } = new();
    }

    public class MyReportCardDto
    {
        public long PublicationId { get; set; }
        public string SchemeName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string Scope { get; set; } = "";
        public string? TermName { get; set; }
        public int Version { get; set; }
        public DateTime PublishedAt { get; set; }
        public string Result { get; set; } = "";
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public int? Rank { get; set; }
    }

    public class MyReportCardDetailDto
    {
        public MyReportCardDto Card { get; set; } = new();
        /// <summary>The stored card as published, for the simple on-screen summary.</summary>
        public ReportCardModel Model { get; set; } = new();
    }
}
