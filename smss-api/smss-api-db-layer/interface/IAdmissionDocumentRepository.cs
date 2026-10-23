using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record AdmissionDocumentRow(TbAdmissionApplicationDocument Document, string DocumentTypeName, string? UploadedByName, string StatusName);
    /// <summary>The application's status (an ADMISSION_STATUS name).</summary>
    public record AdmissionStatusRow(long AdmissionId, string StatusName);

    // Every method takes the school id: another school's applications and files can never be reached through this repository.
    public interface IAdmissionDocumentRepository
    {
        Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct);
        /// <summary>Null when the application is not this school's.</summary>
        Task<AdmissionStatusRow?> GetApplicationStatusAsync(string schoolId, long admissionId, CancellationToken ct);
        /// <summary>An Active document type of this school that applies to students (STUDENT or BOTH): admission documents are the applicant's own.</summary>
        Task<bool> DocumentTypeUsableAsync(string schoolId, long documentTypeId, int activeStatusId, CancellationToken ct);
        Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct);
        Task AddAsync(TbAdmissionApplicationDocument document, CancellationToken ct);
        Task<List<AdmissionDocumentRow>> GetDocumentsAsync(string schoolId, long admissionId, CancellationToken ct);
        Task<TbAdmissionApplicationDocument?> GetDocumentAsync(string schoolId, long admissionId, long documentId, bool tracked, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
