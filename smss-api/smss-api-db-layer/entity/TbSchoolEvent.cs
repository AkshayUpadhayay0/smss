using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One calendar event of a school, for one academic year, over one or more days. Deleting hides it (status Inactive); nothing is removed.</summary>
    [Table("tb_school_events", Schema = "public")]
    public class TbSchoolEvent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("event_id")]
        public long EventId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("event_title")][StringLength(200)] public string EventTitle { get; set; } = null!;
        [Column("start_date", TypeName = "date")] public DateOnly StartDate { get; set; }
        [Column("end_date", TypeName = "date")] public DateOnly EndDate { get; set; }
        [Column("is_all_day")] public bool IsAllDay { get; set; }
        [Column("start_time", TypeName = "time")] public TimeOnly? StartTime { get; set; }
        [Column("end_time", TypeName = "time")] public TimeOnly? EndTime { get; set; }
        [Column("event_category_id")] public long EventCategoryId { get; set; }
        [Column("description")] public string? Description { get; set; }
        [Column("created_by")] public long CreatedBy { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
