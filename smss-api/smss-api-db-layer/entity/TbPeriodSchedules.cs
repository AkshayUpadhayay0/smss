using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>S4: a named bell schedule of a school ("Regular", "Winter", "Saturday"). Exactly one per school is the default.</summary>
    [Table("tb_period_schedules", Schema = "public")]
    public class TbPeriodSchedule
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("schedule_id")] public long ScheduleId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("schedule_name")][StringLength(60)] public string ScheduleName { get; set; } = null!;
        [Column("is_default")] public bool IsDefault { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
    }

    /// <summary>S4: which schedule applies to which class on which day in a year. A null class means every class, a null day every day (1 = Monday .. 6 = Saturday).</summary>
    [Table("tb_academic_year_schedules", Schema = "public")]
    public class TbAcademicYearSchedule
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("year_schedule_id")] public long YearScheduleId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long? ClassId { get; set; }
        [Column("day_of_week")] public short? DayOfWeek { get; set; }
        [Column("schedule_id")] public long ScheduleId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
    }
}
