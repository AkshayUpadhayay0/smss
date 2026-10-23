using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    public static class LeaveStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";

        public static readonly string[] All = { Pending, Approved, Rejected, Cancelled };
    }

    /// <summary>Rules and mapping shared by employee and student leave.</summary>
    public static class LeaveRules
    {
        public const int MaxDays = 366;
        /// <summary>SQLSTATE of the exclusion constraint that keeps one person's Approved leaves from overlapping.</summary>
        public const string ExclusionViolation = "23P01";

        public static string? ValidateDates(DateOnly from, DateOnly to)
        {
            if (to < from) return "The to date can't be before the from date.";
            if (to.DayNumber - from.DayNumber + 1 > MaxDays) return $"A leave request can cover at most {MaxDays} days.";
            return null;
        }

        /// <summary>The canonical status for a filter value ("pending" -> "Pending"); null with an error for an unknown one. Null / blank = no filter.</summary>
        public static bool TryNormalizeStatus(string? status, out string? normalized, out string? error)
        {
            normalized = null; error = null;
            if (string.IsNullOrWhiteSpace(status)) return true;
            normalized = LeaveStatuses.All.FirstOrDefault(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normalized != null) return true;
            error = $"Status must be one of: {string.Join(", ", LeaveStatuses.All)}.";
            return false;
        }

        /// <summary>Approve / reject, and the rejection rule: a non-blank reason. Returns an error message or null.</summary>
        public static string? ParseDecision(DecideLeaveRequest req, out bool approve, out string? reason)
        {
            approve = false;
            reason = string.IsNullOrWhiteSpace(req.DecisionReason) ? null : req.DecisionReason.Trim();
            var d = (req.Decision ?? string.Empty).Trim();
            if (string.Equals(d, "Approve", StringComparison.OrdinalIgnoreCase) || string.Equals(d, "Approved", StringComparison.OrdinalIgnoreCase)) { approve = true; return null; }
            if (string.Equals(d, "Reject", StringComparison.OrdinalIgnoreCase) || string.Equals(d, "Rejected", StringComparison.OrdinalIgnoreCase))
                return reason == null ? "A reason is required when rejecting a leave request." : null;
            return "Decision must be Approve or Reject.";
        }

        public static string FullName(string first, string? middle, string? last) =>
            string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        public static string Range(DateOnly from, DateOnly to) => from == to ? $"{from:dd MMM yyyy}" : $"{from:dd MMM yyyy} to {to:dd MMM yyyy}";

        private static T Fill<T>(T dto, LeaveRow r) where T : LeaveRequestDto
        {
            dto.LeaveRequestId = r.LeaveRequestId; dto.LeaveTypeId = r.LeaveTypeId; dto.LeaveTypeName = r.LeaveTypeName;
            dto.FromDate = r.FromDate; dto.ToDate = r.ToDate; dto.Days = r.ToDate.DayNumber - r.FromDate.DayNumber + 1; dto.Reason = r.Reason; dto.Status = r.Status;
            dto.AppliedBy = r.AppliedBy; dto.AppliedByName = r.AppliedByName; dto.AppliedAt = r.AppliedAt;
            dto.DecidedBy = r.ApprovedBy; dto.DecidedByName = r.ApprovedByName; dto.DecidedAt = r.ApprovedAt; dto.DecisionReason = r.DecisionReason;
            return dto;
        }

        public static EmployeeLeaveDto ToEmployeeDto(LeaveRow r) =>
            Fill(new EmployeeLeaveDto { EmployeeId = r.PersonId, EmployeeName = FullName(r.FirstName, r.MiddleName, r.LastName) }, r);

        public static StudentLeaveDto ToStudentDto(LeaveRow r) =>
            Fill(new StudentLeaveDto { StudentId = r.PersonId, StudentName = FullName(r.FirstName, r.MiddleName, r.LastName), SectionId = r.SectionId, ClassName = r.ClassName, SectionName = r.SectionName }, r);
    }
}
