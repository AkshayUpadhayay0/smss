using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record StudentDocStudentRow(string StudentId, string FullName, string AdmissionNumber, DateTime AdmissionDate, int StatusId, string? ClassName, string? SectionName);
    public record RequiredDocTypeRow(long TypeId, string Name, string Code, DateTime RequiredFrom);
    public record StudentDocumentRow(TbStudentDocument Document, string TypeName, string? ByName);
    public record PendingStudentRow(string StudentId, string FullName, string AdmissionNumber, string? ClassName, string? SectionName, List<string> Missing);

    // Every method takes the school id: another school's students, types and files can never be reached through this repository.
    public interface IStudentDocumentRepository
    {
        Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct);
        /// <summary>Null when the student is not this school's.</summary>
        Task<StudentDocStudentRow?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct);
        /// <summary>An Active document type of this school that applies to students (STUDENT or BOTH).</summary>
        Task<bool> DocumentTypeUsableAsync(string schoolId, long documentTypeId, int activeStatusId, CancellationToken ct);
        /// <summary>Active, required types that apply to students, each with the date the requirement started.</summary>
        Task<List<RequiredDocTypeRow>> GetRequiredTypesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<StudentDocumentRow>> GetDocumentsAsync(string schoolId, string studentId, bool includeVoided, CancellationToken ct);
        Task<TbStudentDocument?> GetDocumentAsync(string schoolId, string studentId, long documentId, bool tracked, CancellationToken ct);
        Task<TbStudentDocument?> GetActiveForTypeAsync(string schoolId, string studentId, long documentTypeId, CancellationToken ct);
        Task AddAsync(TbStudentDocument document, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task LockStudentAsync(string schoolId, string studentId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracking();

        /// <summary>Active students missing at least one required document (admitted on or after the type's required_from), with what each lacks.</summary>
        Task<List<PendingStudentRow>> ListPendingAsync(string schoolId, int activeStudentStatusId, int activeTypeStatusId, long? classId, long? sectionId, string? search, CancellationToken ct);
        Task<int> CountPendingStudentsAsync(string schoolId, int activeStudentStatusId, int activeTypeStatusId, CancellationToken ct);

        /// <summary>The signed-in user's login id (tb_users.org_user_id), which is the employee id of a staff login.</summary>
        Task<string?> GetLoginIdAsync(string schoolId, long userId, CancellationToken ct);
        /// <summary>True when the employee is the (active) class teacher of a section the student is enrolled in, in that enrolment's own year.</summary>
        Task<bool> IsClassTeacherOfStudentAsync(string schoolId, string employeeId, string studentId, int activeStatusId, CancellationToken ct);

        /// <summary>The application's still-active documents of types that apply to students, the latest one per type (oldest first).</summary>
        Task<List<TbAdmissionApplicationDocument>> GetApplicationDocumentsToCarryAsync(string schoolId, long admissionId, int activeStatusId, CancellationToken ct);
    }
}
