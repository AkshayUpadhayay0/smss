using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. Another school's ids answer 404. The student of my-bus comes from the token (userId).
    public interface IStudentTransportService
    {
        /// <summary>The student's assignment for the given (default: current) academic year. Assignment is null when there is none.</summary>
        Task<ApiResponse<StudentTransportDto>> GetAsync(string? schoolId, string studentId, long? academicYearId, CancellationToken ct);
        /// <summary>Assign or change the area (reactivating the same row when it was removed). 201 for a first assignment, 200 otherwise.</summary>
        Task<ApiResponse<StudentTransportDto>> AssignAsync(string? schoolId, string studentId, StudentTransportRequest? request, CancellationToken ct);
        /// <summary>Soft delete (status Inactive). Removing what is not there is a 200 with a null assignment.</summary>
        Task<ApiResponse<StudentTransportDto>> RemoveAsync(string? schoolId, string studentId, long? academicYearId, CancellationToken ct);
        /// <summary>GET api/Transport/my-bus for a Student login.</summary>
        Task<ApiResponse<MyBusDto>> GetMyBusAsync(string? schoolId, long userId, CancellationToken ct);
    }
}
