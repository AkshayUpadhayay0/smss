using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Global assessment types (Class Test, Unit Test, Monthly Test, Other).</summary>
    [Table("lut_assessment_type", Schema = "public")]
    public class LutAssessmentType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("assessment_type_id")]
        public long AssessmentTypeId { get; set; }

        [Column("type_name")][StringLength(50)] public string TypeName { get; set; } = null!;
        [Column("type_code")][StringLength(30)] public string TypeCode { get; set; } = null!;
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One test recorded for a section and subject, owned by the teacher who recorded it. Deleting hides it (status Inactive); its marks are kept.</summary>
    [Table("tb_class_assessments", Schema = "public")]
    public class TbClassAssessment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("assessment_id")]
        public long AssessmentId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        [Column("assessment_type_id")] public long AssessmentTypeId { get; set; }
        [Column("employee_id")][StringLength(20)] public string EmployeeId { get; set; } = null!;
        [Column("title")][StringLength(200)] public string Title { get; set; } = null!;
        [Column("test_date", TypeName = "date")] public DateOnly TestDate { get; set; }
        [Column("max_marks", TypeName = "numeric(6,2)")] public decimal MaxMarks { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One student's result in one test: a mark, or absent (never both, never neither: a database check).</summary>
    [Table("tb_student_assessment_marks", Schema = "public")]
    public class TbStudentAssessmentMark
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("mark_id")]
        public long MarkId { get; set; }

        [Column("assessment_id")] public long AssessmentId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("obtained_marks", TypeName = "numeric(6,2)")] public decimal? ObtainedMarks { get; set; }
        [Column("is_absent")] public bool IsAbsent { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
