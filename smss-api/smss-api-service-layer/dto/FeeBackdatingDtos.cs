using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>The date to work the "apply to earlier months" preview from.</summary>
    public class BackdatingPreviewRequest
    {
        [Required(ErrorMessage = "A date to apply from is required.")]
        public DateOnly? FromDate { get; set; }
    }

    public class ApplyBackdatingRequest
    {
        [Required(ErrorMessage = "A date to apply from is required.")]
        public DateOnly? FromDate { get; set; }

        /// <summary>Why the earlier months are being billed now: required (1-500 characters) and written to the log.</summary>
        [Required(ErrorMessage = "A reason is required."), StringLength(500, MinimumLength = 1, ErrorMessage = "The reason must be 1 to 500 characters.")]
        public string Reason { get; set; } = null!;

        /// <summary>Must be true: the admin has seen the preview and confirms.</summary>
        public bool Confirm { get; set; }

        /// <summary>Optional: the number of dues the preview showed. When given and the real number now differs the apply is refused (409) so nothing unexpected is created.</summary>
        public int? ExpectedDuesCount { get; set; }
    }

    public class BackdatingStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public int DuesCount { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>What "apply to earlier months" would create (nothing is written by a preview).</summary>
    public class BackdatingPreviewDto
    {
        public long FeeStructureId { get; set; }
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public DateOnly FromDate { get; set; }
        /// <summary>Today: the dues counted are dated from <see cref="FromDate"/> up to and including today.</summary>
        public DateOnly ToDate { get; set; }
        /// <summary>The line's current effective date (it moves back to <see cref="FromDate"/> when this is applied).</summary>
        public DateOnly CurrentEffectiveFrom { get; set; }
        public int StudentsCount { get; set; }
        public int DuesCount { get; set; }
        public decimal TotalAmount { get; set; }
        public List<BackdatingStudentDto> Students { get; set; } = new();
    }

    public class BackdatingResultDto
    {
        /// <summary>Null when nothing needed creating (it was already applied): no log row is written then.</summary>
        public long? BackdatingLogId { get; set; }
        public int StudentsCount { get; set; }
        public int DuesCount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateOnly EffectiveFrom { get; set; }
    }
}
