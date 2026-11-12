namespace smss_api_db_layer.@interface
{
    /// <summary>An open application that lacks at least one REQUIRED document, with the names of the ones missing.</summary>
    public record AdmissionMissingDocsRow(long AdmissionId, string ApplicationNumber, string ApplicantName, string StatusName, DateTime ApplicationDate, List<string> MissingDocuments);

    // Used by AdmissionDashboardService only, and read-only. The Admission service has no cross-application document view, so this holds the one figure it cannot give: which open
    // applications are missing a required document. No new table: tb_admissions, tb_admission_application_documents and tb_document_types.
    public interface IAdmissionDashboardRepository
    {
        /// <summary>
        /// The applications in <paramref name="openStatuses"/> (admission status names) that have no ACTIVE uploaded document for at least one of the school's ACTIVE, REQUIRED document types
        /// that apply to students (STUDENT or BOTH). Newest application first. A school with no required document types has none.
        /// </summary>
        Task<List<AdmissionMissingDocsRow>> GetMissingDocumentsAsync(string schoolId, IReadOnlyCollection<string> openStatuses, CancellationToken ct);
    }
}
