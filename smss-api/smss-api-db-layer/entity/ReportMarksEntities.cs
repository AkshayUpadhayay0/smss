using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // Examination / Report Card, STAGE 2 (marks entry and class-teacher inputs). Mirrors Docs/smss-report-card-stage2.sql. No navigation properties, as elsewhere in the project.

    [Table("tb_report_entry_status", Schema = "public")]
    public class TbReportEntryStatus
    {
        [Column("exam_subject_id")] public long ExamSubjectId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("status")][StringLength(10)] public string Status { get; set; } = "DRAFT";
        /// <summary>The concurrency token: a save carries the version it read and only succeeds while it is still current.</summary>
        [Column("version")] public int Version { get; set; } = 1;
        [Column("submitted_by")] public long? SubmittedBy { get; set; }
        [Column("submitted_at")] public DateTime? SubmittedAt { get; set; }
        [Column("accepted_by")] public long? AcceptedBy { get; set; }
        [Column("accepted_at")] public DateTime? AcceptedAt { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_marks", Schema = "public")]
    public class TbReportMark
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("mark_id")] public long MarkId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("exam_subject_id")] public long ExamSubjectId { get; set; }
        [Column("scheme_subject_id")] public long SchemeSubjectId { get; set; }
        [Column("skill_id")] public long? SkillId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("section_id")] public long SectionId { get; set; }
        [Column("entry_state")][StringLength(8)] public string EntryState { get; set; } = null!;
        [Column("marks", TypeName = "numeric(6,2)")] public decimal? Marks { get; set; }
        [Column("grade_code")][StringLength(10)] public string? GradeCode { get; set; }
        [Column("entered_by")] public long? EnteredBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One short remark per (term, scheme subject, student). Not a mark: no accept workflow; locked together with the term-section. Mirrors Docs/smss-report-card-stage4.sql.</summary>
    [Table("tb_report_subject_remarks", Schema = "public")]
    public class TbReportSubjectRemark
    {
        [Column("term_id")] public long TermId { get; set; }
        [Column("scheme_subject_id")] public long SchemeSubjectId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("remark")][StringLength(200)] public string Remark { get; set; } = null!;
        [Column("updated_by")] public long UpdatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_term_section", Schema = "public")]
    public class TbReportTermSection
    {
        [Column("term_id")] public long TermId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("status")][StringLength(6)] public string Status { get; set; } = "OPEN";
        [Column("locked_by")] public long? LockedBy { get; set; }
        [Column("locked_at")] public DateTime? LockedAt { get; set; }
        [Column("unlocked_by")] public long? UnlockedBy { get; set; }
        [Column("unlocked_at")] public DateTime? UnlockedAt { get; set; }
        [Column("unlock_reason")][StringLength(300)] public string? UnlockReason { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_student_activity_grades", Schema = "public")]
    public class TbReportStudentActivityGrade
    {
        [Column("term_id")] public long TermId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("area_id")] public long AreaId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("grade_code")][StringLength(10)] public string GradeCode { get; set; } = null!;
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_term_student", Schema = "public")]
    public class TbReportTermStudent
    {
        [Column("term_id")] public long TermId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("remark")][StringLength(500)] public string? Remark { get; set; }
        [Column("conduct")][StringLength(60)] public string? Conduct { get; set; }
        [Column("rank_text")][StringLength(30)] public string? RankText { get; set; }
        [Column("manual_working_days")] public short? ManualWorkingDays { get; set; }
        [Column("manual_present_days")] public short? ManualPresentDays { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_year_student", Schema = "public")]
    public class TbReportYearStudent
    {
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("section_id")] public long SectionId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("promotion_decision")][StringLength(12)] public string? PromotionDecision { get; set; }
        [Column("promoted_to")][StringLength(60)] public string? PromotedTo { get; set; }
        [Column("new_session_date", TypeName = "date")] public DateTime? NewSessionDate { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_audit", Schema = "public")]
    public class TbReportAudit
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("audit_id")] public long AuditId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("event")][StringLength(10)] public string Event { get; set; } = null!;
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("term_id")] public long? TermId { get; set; }
        [Column("exam_subject_id")] public long? ExamSubjectId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("from_status")][StringLength(10)] public string? FromStatus { get; set; }
        [Column("to_status")][StringLength(10)] public string? ToStatus { get; set; }
        [Column("reason")][StringLength(300)] public string? Reason { get; set; }
        /// <summary>Comma-separated student ids a LOCK left out because they were added after the marks were accepted.</summary>
        [Column("excluded_students")] public string? ExcludedStudents { get; set; }
        /// <summary>Set on PUBLISH only: the publication this event created (a plain id).</summary>
        [Column("publication_id")] public long? PublicationId { get; set; }
        [Column("actor_user_id")] public long ActorUserId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
