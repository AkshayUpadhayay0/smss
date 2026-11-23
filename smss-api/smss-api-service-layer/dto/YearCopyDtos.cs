using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // S7: copy configuration from one academic year to another. No school id: it comes from the token.
    public class YearCopyRequest
    {
        /// <summary>CLASSES_SECTIONS, SUBJECTS, SCHEDULES, CLASS_TEACHERS, TIMETABLE (any of them, run in this order).</summary>
        [Required(ErrorMessage = "Choose at least one part to copy."), MinLength(1, ErrorMessage = "Choose at least one part to copy.")] public List<string>? Parts { get; set; }
        /// <summary>A real run needs this to be true. A preview ignores it and never writes.</summary>
        public bool Confirm { get; set; }
    }

    public class YearCopyCountDto { public string Kind { get; set; } = null!; public int Count { get; set; } }

    public class YearCopySkipDto
    {
        public string Reason { get; set; } = null!;
        public int Count { get; set; }
        /// <summary>A few of the rows skipped for this reason (so the admin can see which).</summary>
        public List<string> Examples { get; set; } = new();
    }

    public class YearCopyPartDto
    {
        public string Part { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>Rows created (a preview: that WOULD be created).</summary>
        public int Created { get; set; }
        public List<YearCopyCountDto> Details { get; set; } = new();
        /// <summary>Already in the target year: left exactly as it is.</summary>
        public int SkippedExisting { get; set; }
        public List<YearCopySkipDto> Skipped { get; set; } = new();
        public int SkippedWithReason { get; set; }
        public List<YearCopyCountDto> Notes { get; set; } = new();
    }

    public class YearCopyResultDto
    {
        public long SourceYearId { get; set; }
        public string SourceYearName { get; set; } = null!;
        public long TargetYearId { get; set; }
        public string TargetYearName { get; set; } = null!;
        public bool Preview { get; set; }
        public long? CopyId { get; set; }
        public List<YearCopyPartDto> Parts { get; set; } = new();
    }

    public class YearCopyLogDto
    {
        public long CopyId { get; set; }
        public long SourceYearId { get; set; }
        public string SourceYearName { get; set; } = null!;
        public long TargetYearId { get; set; }
        public List<string> Parts { get; set; } = new();
        public string Status { get; set; } = null!;
        public string? ErrorNote { get; set; }
        public string? RunByName { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public List<YearCopyPartDto> Result { get; set; } = new();
    }
}
