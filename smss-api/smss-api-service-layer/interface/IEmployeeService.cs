using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400.
    public interface IEmployeeService
    {
        Task<ApiResponse<EmployeeLookupsDto>> GetLookupsAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<EmployeePagedDto>> ListAsync(string? schoolId, string? search, long? designationId, long? departmentId, long? employmentStatusId,
            string? employeeType, string? status, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<EmployeeDto>> GetAsync(string? schoolId, string employeeId, CancellationToken ct);
        Task<ApiResponse<EmployeeDto>> CreateAsync(string? schoolId, EmployeeRequest req, CancellationToken ct);
        Task<ApiResponse<EmployeeDto>> UpdateAsync(string? schoolId, string employeeId, EmployeeRequest req, CancellationToken ct);
        Task<ApiResponse<EmployeeDto>> ToggleStatusAsync(string? schoolId, string employeeId, CancellationToken ct);

        Task<ApiResponse<EmployeePhotoDto>> UploadPhotoAsync(string? schoolId, string employeeId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<EmployeePhotoDto>> RemovePhotoAsync(string? schoolId, string employeeId, CancellationToken ct);

        Task<ApiResponse<List<EmployeeDocumentDto>>> GetDocumentsAsync(string? schoolId, string employeeId, bool includeRemoved, CancellationToken ct);
        Task<ApiResponse<EmployeeDocumentDto>> UploadDocumentAsync(string? schoolId, string employeeId, long documentTypeId, string? remarks, long uploadedByUserId,
            Stream content, string fileName, long length, CancellationToken ct);
        /// <summary>The file for an authenticated download: the open stream, the type and the name to save as (null data = see the response).</summary>
        Task<(ApiResponse<EmployeeDocumentDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenDocumentAsync(string? schoolId, string employeeId, long documentId, CancellationToken ct);
        Task<ApiResponse<EmployeeDocumentDto>> RemoveDocumentAsync(string? schoolId, string employeeId, long documentId, CancellationToken ct);

        Task<ApiResponse<EmployeeLoginCreatedDto>> CreateLoginAsync(string? schoolId, string employeeId, CreateEmployeeLoginRequest req, CancellationToken ct);
    }
}
