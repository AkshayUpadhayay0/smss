using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record GuardianStudentRef(string StudentId, string StudentName, bool IsPrimary, bool StudentActive);
    /// <summary>A student of an incomplete guardian, with the class and section of the enrolment in the viewed academic year (null when not enrolled in it).</summary>
    public record IncompleteStudentRef(string StudentId, string StudentName, long? ClassId, string? ClassName, long? SectionId, string? SectionName);
    public record IncompleteGuardianRow(long GuardianId, string Name, string Relationship, string Mobile, string? WhatsApp, string? Email, List<IncompleteStudentRef> Students);

    public record SiblingGuardianRow(long GuardianId, string Name, string Relationship, string Mobile, bool HasWhatsApp, bool HasEmail, bool IsPrimary);
    public record SiblingStudentRow(string StudentId, string StudentName, string? ClassName, string? SectionName, List<SiblingGuardianRow> Guardians);

    // Every method takes the school id: another school's guardians can never be reached through this repository.
    public interface IGuardianRepository
    {
        Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct);
        /// <summary>Null when the guardian is not this school's.</summary>
        Task<TbGuardian?> GetGuardianAsync(string schoolId, long guardianId, bool tracked, CancellationToken ct);
        Task<List<TbGuardian>> GetGuardiansAsync(string schoolId, IReadOnlyCollection<long> guardianIds, bool tracked, CancellationToken ct);
        /// <summary>The students this guardian is linked to (this school's only).</summary>
        Task<List<GuardianStudentRef>> GetLinkedStudentsAsync(string schoolId, long guardianId, int activeStudentStatusId, CancellationToken ct);
        /// <summary>The year the lists are read for: the school's current-flagged year, else (when none is flagged) the year containing today, else null.</summary>
        Task<long?> GetViewedYearIdAsync(string schoolId, DateOnly today, CancellationToken ct);
        /// <summary>Primary guardians of ACTIVE students that lack a WhatsApp number or an email, with each one's students and their class and section in the viewed year.</summary>
        Task<List<IncompleteGuardianRow>> ListIncompletePrimaryAsync(string schoolId, int activeStatusId, long? viewedYearId, CancellationToken ct);
        Task<int> CountIncompletePrimaryAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>
        /// Active students of this school, enrolled in the viewed year, optionally of one class and matching a name, with their ACTIVE (not merged) guardians. At most <paramref name="limit"/> students.
        /// The mobile numbers are returned whole here; the SERVICE masks them before anything leaves the API.
        /// </summary>
        Task<List<SiblingStudentRow>> SearchSiblingStudentsAsync(string schoolId, int activeStatusId, long viewedYearId, long? classId, string? search, int limit, CancellationToken ct);
        Task AddChangesAsync(IEnumerable<TbGuardianChange> changes, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
