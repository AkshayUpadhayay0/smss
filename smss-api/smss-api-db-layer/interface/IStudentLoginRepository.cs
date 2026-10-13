using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: a student of another school can never be loaded or counted through this.
    public interface IStudentLoginRepository
    {
        Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct);
        /// <summary>True when a login with this student's id as its login id already exists in the school.</summary>
        Task<bool> StudentHasLoginAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>Email of the student's primary guardian, or null when there is none / it is blank.</summary>
        Task<string?> GetPrimaryGuardianEmailAsync(string schoolId, string studentId, CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<long?> GetRoleIdByNameAsync(string roleName, CancellationToken ct);
        Task AddUserAsync(TbUsers user, CancellationToken ct);
    }
}
