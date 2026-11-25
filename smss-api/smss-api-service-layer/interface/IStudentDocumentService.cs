using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Who is asking: the controller decides the tier from the token (CanManageApplications = manage; a Teacher = read the statuses of the students they are class teacher of; nobody else).</summary>
    public record StudentDocumentCaller(long UserId, bool CanManage, bool IsTeacher);

    // schoolId is the caller's own school from the token; every read and write is scoped to it. Documents NEVER block admission or any other flow.
    public interface IStudentDocumentService
    {
        /// <summary>The student's required types with their status, and every active document. A Teacher gets statuses only, and only for the class teacher's own section (403 otherwise).</summary>
        Task<ApiResponse<StudentDocumentsDto>> GetAsync(string? schoolId, string studentId, StudentDocumentCaller caller, CancellationToken ct);
        /// <summary>PDF / image only, up to 5 MB, real file signature checked, private storage. A new file replaces (voids) the type's current document or waiver.</summary>
        Task<ApiResponse<StudentDocumentDto>> UploadAsync(string? schoolId, string studentId, long documentTypeId, string? remarks, long userId, Stream content, string fileName, long length, CancellationToken ct);
        /// <summary>Waive one document type for the student: a reason is required and the waiver records who and when. Refused while the type has a submitted file (void it first) or is already waived.</summary>
        Task<ApiResponse<StudentDocumentDto>> WaiveAsync(string? schoolId, string studentId, WaiveStudentDocumentRequestDto request, long userId, CancellationToken ct);
        /// <summary>Soft void: the row and the file stay for the audit trail, the type becomes pending again.</summary>
        Task<ApiResponse<StudentDocumentDto>> VoidAsync(string? schoolId, string studentId, long documentId, string? reason, long userId, CancellationToken ct);
        /// <summary>The authenticated download of a SUBMITTED, not voided file: the file is never reachable by URL.</summary>
        Task<(ApiResponse<StudentDocumentDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenAsync(string? schoolId, string studentId, long documentId, CancellationToken ct);
        /// <summary>Students missing a required document and what each lacks (active students admitted on or after the type's required-from date).</summary>
        Task<ApiResponse<PendingStudentDocumentsDto>> ListPendingAsync(string? schoolId, long? classId, long? sectionId, string? search, CancellationToken ct);
        /// <summary>How many students are missing at least one required document (the dashboard tiles).</summary>
        Task<ApiResponse<int>> CountPendingAsync(string? schoolId, CancellationToken ct);
        /// <summary>After an application is confirmed: copy its documents to the student. Never throws and never blocks the admission; returns how many were carried.</summary>
        Task<int> CarryOverFromApplicationAsync(string schoolId, long admissionId, string studentId, long? userId, CancellationToken ct);
    }
}
