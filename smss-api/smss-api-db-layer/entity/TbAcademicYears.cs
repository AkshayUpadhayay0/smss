using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_academic_years", Schema = "public")]
    public class TbAcademicYears
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("academic_year_id")]
        public long AcademicYearId { get; set; }

        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("year_name")] public string YearName { get; set; } = null!;
        [Column("start_date")] public DateOnly StartDate { get; set; }
        [Column("end_date")] public DateOnly EndDate { get; set; }
        [Column("is_current")] public bool IsCurrent { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
