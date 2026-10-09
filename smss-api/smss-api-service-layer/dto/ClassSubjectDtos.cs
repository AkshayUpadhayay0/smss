using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id: it comes from the token.
    public class CreateClassSubjectRequest
    {
        [Required(ErrorMessage = "Class is required.")]
        [Range(1, long.MaxValue, ErrorMessage = "Class is required.")]
        public long? ClassId { get; set; }

        [Required(ErrorMessage = "Subject is required.")]
        [Range(1, long.MaxValue, ErrorMessage = "Subject is required.")]
        public long? SubjectId { get; set; }
    }

    // The full set of subjects the class should have afterwards (empty = remove them all)
    public class BulkSetClassSubjectsRequest
    {
        [Required(ErrorMessage = "Class is required.")]
        [Range(1, long.MaxValue, ErrorMessage = "Class is required.")]
        public long? ClassId { get; set; }

        [Required(ErrorMessage = "Subject list is required (it may be empty).")]
        public List<long>? SubjectIds { get; set; }
    }

    public class ClassSubjectResponse
    {
        public long ClassSubjectId { get; set; }
        public string SchoolId { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public string? SubjectCode { get; set; }
        public int? StatusId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
