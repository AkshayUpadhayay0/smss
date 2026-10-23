using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token. The controller has already checked CanManageApplications.
    public interface IAdmissionDocumentService
    {
        Task<ApiResponse<List<AdmissionDocumentDto>>> GetDocumentsAsync(string? schoolId, long admissionId, bool includeRemoved, CancellationToken ct);
        /// <summary>PDF / image only, up to 5 MB, real file signature checked, private storage. Refused (409) once the application is Rejected, Cancelled or Admitted.</summary>
        Task<ApiResponse<AdmissionDocumentDto>> UploadDocumentAsync(string? schoolId, long admissionId, long documentTypeId, string? remarks, long uploadedByUserId,
            Stream content, string fileName, long length, CancellationToken ct);
        /// <summary>The authenticated download: the file is never reachable by URL.</summary>
        Task<(ApiResponse<AdmissionDocumentDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenDocumentAsync(string? schoolId, long admissionId, long documentId, CancellationToken ct);
        /// <summary>Soft remove: the row is hidden, the file and the record stay for the audit trail.</summary>
        Task<ApiResponse<AdmissionDocumentDto>> RemoveDocumentAsync(string? schoolId, long admissionId, long documentId, CancellationToken ct);
    }
}
