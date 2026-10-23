using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class LeaveTypeDto
    {
        public long LeaveTypeId { get; set; }
        public string TypeName { get; set; } = null!;
        public string TypeCode { get; set; } = null!;
        public bool AppliesToEmployee { get; set; }
        public bool AppliesToStudent { get; set; }
    }

    public class ApplyEmployeeLeaveRequest
    {
        /// <summary>Optional: the caller's own employee id when left out. Only a School Admin may name another employee.</summary>
        [StringLength(20)] public string? EmployeeId { get; set; }
        [Required(ErrorMessage = "Leave type is required.")] public long? LeaveTypeId { get; set; }
        [Required(ErrorMessage = "From date is required.")] public DateOnly? FromDate { get; set; }
        [Required(ErrorMessage = "To date is required.")] public DateOnly? ToDate { get; set; }
        [Required(ErrorMessage = "A reason is required."), StringLength(500, ErrorMessage = "The reason can be at most 500 characters.")] public string? Reason { get; set; }
    }

    public class ApplyStudentLeaveRequest
    {
        [Required(ErrorMessage = "Student is required."), StringLength(20)] public string? StudentId { get; set; }
        [Required(ErrorMessage = "Leave type is required.")] public long? LeaveTypeId { get; set; }
        [Required(ErrorMessage = "From date is required.")] public DateOnly? FromDate { get; set; }
        [Required(ErrorMessage = "To date is required.")] public DateOnly? ToDate { get; set; }
        [Required(ErrorMessage = "A reason is required."), StringLength(500, ErrorMessage = "The reason can be at most 500 characters.")] public string? Reason { get; set; }
    }

    public class DecideLeaveRequest
    {
        /// <summary>"Approve" or "Reject" (also accepted: "Approved" / "Rejected"), case-insensitive.</summary>
        [Required(ErrorMessage = "A decision is required.")] public string? Decision { get; set; }
        /// <summary>Required (non-blank) when rejecting; optional note when approving.</summary>
        [StringLength(500, ErrorMessage = "The decision reason can be at most 500 characters.")] public string? DecisionReason { get; set; }
    }

    public class LeaveRequestDto
    {
        public long LeaveRequestId { get; set; }
        public long LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = null!;
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        /// <summary>Calendar days, both ends included.</summary>
        public int Days { get; set; }
        public string Reason { get; set; } = null!;
        /// <summary>Pending / Approved / Rejected / Cancelled.</summary>
        public string Status { get; set; } = null!;
        public long AppliedBy { get; set; }
        public string? AppliedByName { get; set; }
        public DateTime AppliedAt { get; set; }
        /// <summary>Who approved OR rejected it (null while Pending / Cancelled).</summary>
        public long? DecidedBy { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionReason { get; set; }
    }

    public class EmployeeLeaveDto : LeaveRequestDto
    {
        public string EmployeeId { get; set; } = null!;
        public string EmployeeName { get; set; } = null!;
    }

    public class StudentLeaveDto : LeaveRequestDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        /// <summary>The student's section in the school's current academic year (null when not enrolled in it).</summary>
        public long? SectionId { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
    }
}
