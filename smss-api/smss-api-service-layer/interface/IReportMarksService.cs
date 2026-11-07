using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Who is asking, as the controller read it from the token. The school is always the caller's own.</summary>
    public sealed record ReportCaller(long UserId, bool IsAdmin, bool IsPrincipal, bool IsTeacher);

    public class ReportFileResult
    {
        public byte[]? Content { get; set; }
        public string FileName { get; set; } = "";
        public int StatusCode { get; set; } = 200;
        public string Message { get; set; } = "";
    }

    /// <summary>Marks entry: what a user may enter, the grid, draft save, submit / accept / reopen, and the Excel template and upload.</summary>
    public interface IReportMarksService
    {
        Task<ApiResponse<MarksContextDto>> ContextAsync(string? schoolId, ReportCaller caller, long? academicYearId, CancellationToken ct);
        Task<ApiResponse<MarksGridDto>> GetGridAsync(string? schoolId, ReportCaller caller, long examSubjectId, long sectionId, CancellationToken ct);
        Task<ApiResponse<MarksGridDto>> SaveDraftAsync(string? schoolId, ReportCaller caller, SaveGridRequest req, CancellationToken ct);
        /// <summary>Per-subject remarks on their own (not marks: no accept step). Allowed to whoever may enter marks here or the class teacher, while the term-section is OPEN.</summary>
        Task<ApiResponse<MarksGridDto>> SaveRemarksAsync(string? schoolId, ReportCaller caller, SaveRemarksRequest req, CancellationToken ct);
        Task<ApiResponse<MarksGridDto>> SubmitAsync(string? schoolId, ReportCaller caller, GridActionRequest req, CancellationToken ct);
        Task<ApiResponse<MarksGridDto>> AcceptAsync(string? schoolId, ReportCaller caller, GridActionRequest req, CancellationToken ct);
        Task<ApiResponse<MarksGridDto>> ReopenAsync(string? schoolId, ReportCaller caller, GridActionRequest req, CancellationToken ct);
        Task<ReportFileResult> TemplateAsync(string? schoolId, ReportCaller caller, long examSubjectId, long sectionId, CancellationToken ct);
        /// <summary>Validates an uploaded sheet. With apply = false nothing is saved (the grid is filled on screen); with apply = true it is saved into the DRAFT grid. Never submits.</summary>
        Task<ApiResponse<UploadPreviewDto>> UploadAsync(string? schoolId, ReportCaller caller, long examSubjectId, long sectionId, int version, bool apply, byte[] file, CancellationToken ct);
    }

    /// <summary>The class teacher's side: completeness, activity grades, remarks and attendance, promotion, lock and unlock.</summary>
    public interface IReportClassService
    {
        Task<ApiResponse<CompletenessDto>> CompletenessAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, CancellationToken ct);
        Task<ApiResponse<ActivityGradesDto>> GetActivityGradesAsync(string? schoolId, ReportCaller caller, long termId, long sectionId, CancellationToken ct);
        Task<ApiResponse<ActivityGradesDto>> SaveActivityGradesAsync(string? schoolId, ReportCaller caller, SaveActivityGradesRequest req, CancellationToken ct);
        Task<ApiResponse<TermStudentsDto>> GetTermStudentsAsync(string? schoolId, ReportCaller caller, long termId, long sectionId, CancellationToken ct);
        Task<ApiResponse<TermStudentsDto>> SaveTermStudentsAsync(string? schoolId, ReportCaller caller, SaveTermStudentsRequest req, CancellationToken ct);
        Task<ApiResponse<YearStudentsDto>> GetYearStudentsAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, CancellationToken ct);
        Task<ApiResponse<YearStudentsDto>> SaveYearStudentsAsync(string? schoolId, ReportCaller caller, SaveYearStudentsRequest req, CancellationToken ct);
        Task<ApiResponse<TermAttendanceDto>> AttendanceAsync(string? schoolId, ReportCaller caller, long termId, long sectionId, CancellationToken ct);
        Task<ApiResponse<LockResultDto>> LockAsync(string? schoolId, ReportCaller caller, LockRequest req, CancellationToken ct);
        Task<ApiResponse<LockResultDto>> UnlockAsync(string? schoolId, ReportCaller caller, LockRequest req, CancellationToken ct);
    }
}
