using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Global leave types. Two rows may share a name ("Sick Leave", "Other"): they apply to different applicants.</summary>
    [Table("lut_leave_type", Schema = "public")]
    public class LutLeaveType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("leave_type_id")]
        public long LeaveTypeId { get; set; }

        [Column("type_name")][StringLength(50)] public string TypeName { get; set; } = null!;
        [Column("type_code")][StringLength(30)] public string TypeCode { get; set; } = null!;
        [Column("applies_to_employee")] public bool AppliesToEmployee { get; set; }
        [Column("applies_to_student")] public bool AppliesToStudent { get; set; }
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One employee's leave request: Pending -> Approved / Rejected, or Pending -> Cancelled. approved_by / approved_at record who DECIDED it.</summary>
    [Table("tb_employee_leave_requests", Schema = "public")]
    public class TbEmployeeLeaveRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("leave_request_id")]
        public long LeaveRequestId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("employee_id")][StringLength(20)] public string EmployeeId { get; set; } = null!;
        [Column("leave_type_id")] public long LeaveTypeId { get; set; }
        [Column("from_date", TypeName = "date")] public DateOnly FromDate { get; set; }
        [Column("to_date", TypeName = "date")] public DateOnly ToDate { get; set; }
        [Column("reason")] public string Reason { get; set; } = null!;
        [Column("status")][StringLength(20)] public string Status { get; set; } = null!;
        [Column("applied_by")] public long AppliedBy { get; set; }
        [Column("applied_at")] public DateTime AppliedAt { get; set; }
        [Column("approved_by")] public long? ApprovedBy { get; set; }
        [Column("approved_at")] public DateTime? ApprovedAt { get; set; }
        [Column("decision_reason")] public string? DecisionReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One student's leave request. Same shape as the employee one, keyed by student.</summary>
    [Table("tb_student_leave_requests", Schema = "public")]
    public class TbStudentLeaveRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("leave_request_id")]
        public long LeaveRequestId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("leave_type_id")] public long LeaveTypeId { get; set; }
        [Column("from_date", TypeName = "date")] public DateOnly FromDate { get; set; }
        [Column("to_date", TypeName = "date")] public DateOnly ToDate { get; set; }
        [Column("reason")] public string Reason { get; set; } = null!;
        [Column("status")][StringLength(20)] public string Status { get; set; } = null!;
        [Column("applied_by")] public long AppliedBy { get; set; }
        [Column("applied_at")] public DateTime AppliedAt { get; set; }
        [Column("approved_by")] public long? ApprovedBy { get; set; }
        [Column("approved_at")] public DateTime? ApprovedAt { get; set; }
        [Column("decision_reason")] public string? DecisionReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
