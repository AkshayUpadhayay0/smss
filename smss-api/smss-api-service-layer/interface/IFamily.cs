using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Who is asking, as read from the TOKEN (never from the request): the school, the user, and whether the login holds the Student or the Guardian role.</summary>
    public sealed record AccessCaller(string? SchoolId, long UserId, bool IsStudent, bool IsGuardian, long? GuardianIdClaim = null);

    /// <summary>The student a request is for, once the helper has cleared it. ViaGuardian + GuardianId say how.</summary>
    public sealed record StudentAccess(string StudentId, string SchoolId, bool ViaGuardian, long? GuardianId);

    public sealed record AccessDecision(StudentAccess? Access, int StatusCode, string Message)
    {
        public bool Allowed => Access != null;
    }

    /// <summary>
    /// THE one place that answers "which student is this request for". A Student login gets its OWN record (a different student id is a 404). A Guardian login gets the requested child ONLY IF the guardian is Active and not
    /// merged, the child is a student of the SAME school, and the link between them has portal_access on; every other case is a 404 (the same answer for a child that does not exist, belongs to another school, is unlinked, or has
    /// portal access switched off, so nothing can be probed). Any other login is a 403. The guardian is always the one behind the token, never a value from the request.
    /// </summary>
    public interface IStudentAccessService
    {
        Task<AccessDecision> ResolveAsync(AccessCaller caller, string? requestedStudentId, CancellationToken ct);
    }

    /// <summary>
    /// The read-only family screens' data. Every per-child method asks the access helper first and then reuses the existing student-side service for that child; none of them takes a guardian id, a school id or any
    /// other identity from the request. A Student login is refused the children list (403).
    /// </summary>
    public interface IFamilyService
    {
        Task<ApiResponse<FamilyChildrenDto>> ListChildrenAsync(AccessCaller caller, CancellationToken ct);
        Task<ApiResponse<FamilyChildProfileDto>> ProfileAsync(AccessCaller caller, string studentId, CancellationToken ct);
        Task<ApiResponse<StudentOwnAttendanceDto>> AttendanceAsync(AccessCaller caller, string studentId, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<List<HomeworkDto>>> HomeworkAsync(AccessCaller caller, string studentId, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<TimetableGridDto>> TimetableAsync(AccessCaller caller, string studentId, CancellationToken ct);
        Task<ApiResponse<List<FamilyExamScheduleDto>>> ExamScheduleAsync(AccessCaller caller, string studentId, CancellationToken ct);
        Task<ApiResponse<List<MyReportCardDto>>> ReportCardsAsync(AccessCaller caller, string studentId, CancellationToken ct);
        Task<ApiResponse<MyReportCardDetailDto>> ReportCardAsync(AccessCaller caller, string studentId, long publicationId, CancellationToken ct);
        Task<(AccessDecision Decision, ReportFileResult? File)> ReportCardPdfAsync(AccessCaller caller, string studentId, long publicationId, CancellationToken ct);
        Task<ApiResponse<StudentMyFeesDto>> FeesAsync(AccessCaller caller, string studentId, CancellationToken ct);
        Task<ApiResponse<FeeReceiptDto>> ReceiptAsync(AccessCaller caller, string studentId, long paymentId, CancellationToken ct);
        Task<ApiResponse<List<StudentLeaveDto>>> LeaveAsync(AccessCaller caller, string studentId, string? status, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<(AccessDecision Decision, StudentPhotoResult? Photo)> PhotoAsync(AccessCaller caller, string studentId, CancellationToken ct);
        /// <summary>The school's notices for the Guardian tier (school-wide, not per child): the existing notice service with the guardian's own login.</summary>
        Task<ApiResponse<List<NoticeDto>>> NoticesAsync(AccessCaller caller, bool unreadOnly, CancellationToken ct);
    }
}
