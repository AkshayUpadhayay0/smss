using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    public static class TimetableStates { public const string Draft = "DRAFT"; public const string Active = "ACTIVE"; public const string Archived = "ARCHIVED"; }

    /// <summary>
    /// S6: one version of a section's weekly timetable in one academic year. A section has many (drafts, archived history) and exactly ONE ACTIVE (partial unique index).
    /// Only the ACTIVE one is read by dashboards, student / parent views, mobile, attendance and homework. Activating flips <see cref="IsActive"/>; the database cascades the
    /// flip to the mirror column on every entry (tb_timetable_entries.tt_is_active), which is what keeps teacher double-booking impossible among active timetables.
    /// </summary>
    [Table("tb_timetables", Schema = "public")]
    public class TbTimetable
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("timetable_id")] public long TimetableId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("timetable_name")][StringLength(80)] public string TimetableName { get; set; } = null!;
        /// <summary>The bell schedule the timetable is based on (shown in the list); which period a lesson may use is decided by the class / day resolution of the year.</summary>
        [Column("schedule_id")] public long ScheduleId { get; set; }
        [Column("state")][StringLength(8)] public string State { get; set; } = TimetableStates.Draft;
        [Column("is_active")] public bool IsActive { get; set; }
        /// <summary>True from the first activation on and never reset: such a timetable is archived, never deleted.</summary>
        [Column("ever_active")] public bool EverActive { get; set; }
        [Column("first_activated_at")] public DateTime? FirstActivatedAt { get; set; }
        [Column("activated_at")] public DateTime? ActivatedAt { get; set; }
        [Column("activated_by")] public long? ActivatedBy { get; set; }
        [Column("deactivated_at")] public DateTime? DeactivatedAt { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
    }
}
