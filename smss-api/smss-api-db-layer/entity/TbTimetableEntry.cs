using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// One subject, taught by one teacher in one room, in one period of one weekday for one section in one academic year. Changing a slot deactivates
    /// the old row (Inactive, kept as history) and adds a new one. The database allows only ONE Active row per slot and ONE per teacher per period.
    /// </summary>
    [Table("tb_timetable_entries", Schema = "public")]
    public class TbTimetableEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("timetable_entry_id")]
        public long TimetableEntryId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        /// <summary>1 = Monday .. 6 = Saturday.</summary>
        [Column("day_of_week")] public short DayOfWeek { get; set; }
        [Column("period_id")] public long PeriodId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        [Column("employee_id")][StringLength(20)] public string EmployeeId { get; set; } = null!;
        [Column("room_no")][StringLength(50)] public string? RoomNo { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("assigned_by")] public long? AssignedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
