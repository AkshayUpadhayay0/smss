using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token (the controller guarantees it).
    public interface IStudentService
    {
        Task<ApiResponse<List<StudentListItemDto>>> GetStudentsAsync(string schoolId, long academicYearId, long classSectionId, string? feeStatus, CancellationToken ct);
        Task<ApiResponse<StudentProfileDto>> GetStudentProfileAsync(string schoolId, string studentId, bool limited, CancellationToken ct);
        /// <summary>The signed-in Student's OWN profile (student taken from the caller's login, never from the client); only what a student / parent should see.</summary>
        Task<ApiResponse<StudentMyProfileDto>> GetMyProfileAsync(string? schoolId, long callerUserId, CancellationToken ct);
        Task<ApiResponse<StudentPhotoDto>> UploadPhotoAsync(string schoolId, string studentId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<StudentPhotoDto>> RemovePhotoAsync(string schoolId, string studentId, CancellationToken ct);
    }
}
