using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record TransportStudentRef(string StudentId, string FullName);
    public record TransportYearRef(long AcademicYearId, string YearName, bool IsCurrent);
    /// <summary>An assignment row with the area it points at (the area's status is shown, never used to hide the row).</summary>
    public record StudentAssignmentRow(TbStudentTransport Row, string AreaName, int AreaStatusId);

    // Every method takes the school id: there is no way to read or write another school's data through this repository.
    public interface IStudentTransportRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);

        Task<TransportStudentRef?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The student id of the Student LOGIN <paramref name="userId"/> of this school (the login id IS the student id), or null.</summary>
        Task<string?> GetStudentIdForLoginAsync(string schoolId, long userId, CancellationToken ct);

        Task<TransportYearRef?> GetCurrentYearAsync(string schoolId, CancellationToken ct);
        Task<TransportYearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<AreaRef?> GetAreaAsync(string schoolId, long areaId, CancellationToken ct);

        Task<StudentAssignmentRow?> GetAssignmentAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        /// <summary>The tracked row (any status) for a write.</summary>
        Task<TbStudentTransport?> GetForUpdateAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        Task AddAsync(TbStudentTransport row, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
