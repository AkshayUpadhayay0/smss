using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Who is calling, decided by the controller from the token (role names) and passed in.</summary>
    public record HomeworkCaller(long UserId, bool IsSchoolAdmin, bool IsTeacher, bool IsStudent);

    // schoolId is the caller's own school from the token; null / empty answers 400.
    public interface IHomeworkService
    {
        /// <summary>A School Admin may publish for any section + subject (naming the owning teacher); a Teacher only where the Timetable says they teach that subject to that section (403 otherwise).</summary>
        Task<ApiResponse<HomeworkDto>> CreateAsync(string? schoolId, HomeworkCaller caller, CreateHomeworkRequest req, CancellationToken ct);
        /// <summary>Only the owning teacher or a School Admin.</summary>
        Task<ApiResponse<HomeworkDto>> UpdateAsync(string? schoolId, HomeworkCaller caller, long id, UpdateHomeworkRequest req, CancellationToken ct);
        /// <summary>Soft delete (status Inactive). Same authorization as update.</summary>
        Task<ApiResponse<HomeworkDto>> DeleteAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct);
        /// <summary>One item: its owning teacher, a School Admin, or a student currently in its section.</summary>
        Task<ApiResponse<HomeworkDto>> GetAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct);

        /// <summary>The homework the caller (a teacher) created, not everything they could create.</summary>
        Task<ApiResponse<List<HomeworkDto>>> GetMyAsync(string? schoolId, HomeworkCaller caller, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct);
        /// <summary>School Admin: the whole school, filterable.</summary>
        Task<ApiResponse<List<HomeworkDto>>> ListAsync(string? schoolId, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct);
        /// <summary>School Admin: everything assigned to one section, whoever set it.</summary>
        Task<ApiResponse<List<HomeworkDto>>> GetSectionAsync(string? schoolId, long sectionId, long? academicYearId, DateOnly? from, DateOnly? to, CancellationToken ct);
        /// <summary>A student: the homework of their own CURRENT section (current academic year, Active enrollment), every subject, read-only.</summary>
        Task<ApiResponse<List<HomeworkDto>>> GetStudentMyAsync(string? schoolId, HomeworkCaller caller, DateOnly? from, DateOnly? to, CancellationToken ct);

        /// <summary>Set or replace the single attachment. Same authorization as update.</summary>
        Task<ApiResponse<HomeworkDto>> UploadAttachmentAsync(string? schoolId, HomeworkCaller caller, long id, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<HomeworkDto>> RemoveAttachmentAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct);
        /// <summary>The authenticated download: refused unless the caller may see the homework row itself.</summary>
        Task<(ApiResponse<HomeworkDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenAttachmentAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct);
    }
}
