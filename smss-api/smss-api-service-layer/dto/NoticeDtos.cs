using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>The three audience tiers. A notice has exactly one.</summary>
    public static class NoticeAudiences
    {
        public const string All = "ALL";
        public const string Staff = "STAFF";
        public const string StudentsGuardians = "STUDENTS_GUARDIANS";

        public static readonly string[] Values = { All, Staff, StudentsGuardians };
    }

    public class SaveNoticeRequest
    {
        [Required(ErrorMessage = "Title is required."), StringLength(200, ErrorMessage = "The title can be at most 200 characters.")] public string? Title { get; set; }
        [Required(ErrorMessage = "The notice text is required."), StringLength(5000, ErrorMessage = "The notice can be at most 5000 characters.")] public string? Body { get; set; }
        /// <summary>ALL, STAFF or STUDENTS_GUARDIANS (any case). One tier only.</summary>
        [Required(ErrorMessage = "Audience is required.")] public string? Audience { get; set; }
    }

    public class NoticeDto
    {
        public long NoticeId { get; set; }
        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public long CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime PublishedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        /// <summary>Whether the CALLING user has opened it.</summary>
        public bool IsRead { get; set; }
        public string StatusName { get; set; } = null!;
    }

    public class UnreadCountDto
    {
        public int Count { get; set; }
    }
}
