using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>Create and update share one shape.</summary>
    public class SchoolEventRequest
    {
        /// <summary>Optional: when left out, the academic year that contains the start date is used.</summary>
        public long? AcademicYearId { get; set; }
        [Required(ErrorMessage = "Event title is required."), StringLength(200, ErrorMessage = "The title can be at most 200 characters.")]
        public string? EventTitle { get; set; }
        [Required(ErrorMessage = "Start date is required.")] public DateOnly? StartDate { get; set; }
        /// <summary>Optional: defaults to the start date. Never before it.</summary>
        public DateOnly? EndDate { get; set; }
        public bool IsAllDay { get; set; } = true;
        /// <summary>Both times are required when the event is not all-day, and must not be sent when it is.</summary>
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        [Required(ErrorMessage = "Category is required.")] public long? EventCategoryId { get; set; }
        [StringLength(2000, ErrorMessage = "The description can be at most 2000 characters.")] public string? Description { get; set; }
    }

    public class SchoolEventDto
    {
        public long EventId { get; set; }
        public long AcademicYearId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool IsAllDay { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public long EventCategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public string CategoryCode { get; set; } = null!;
        public string ColorHex { get; set; } = null!;
        public string? Description { get; set; }
        public string? CreatedByName { get; set; }
        /// <summary>Only on /upcoming: the event started before today and is still running.</summary>
        public bool IsOngoing { get; set; }
    }

    public class EventCategoryDto
    {
        public long EventCategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public string CategoryCode { get; set; } = null!;
        public string ColorHex { get; set; } = null!;
    }
}
