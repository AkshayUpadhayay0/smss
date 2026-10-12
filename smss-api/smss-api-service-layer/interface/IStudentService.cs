using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token (the controller guarantees it).
    public interface IStudentService
    {
        Task<ApiResponse<List<StudentListItemDto>>> GetStudentsAsync(string schoolId, long academicYearId, long classSectionId, CancellationToken ct);
        Task<ApiResponse<StudentProfileDto>> GetStudentProfileAsync(string schoolId, long studentId, CancellationToken ct);
        Task<ApiResponse<StudentPhotoDto>> UploadPhotoAsync(string schoolId, long studentId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<StudentPhotoDto>> RemovePhotoAsync(string schoolId, long studentId, CancellationToken ct);
    }
}
