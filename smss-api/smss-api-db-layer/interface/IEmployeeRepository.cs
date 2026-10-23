using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record EmployeeListRow(TbEmployee Employee, string DesignationName, string DepartmentName, string EmploymentStatusName, string StatusName, bool HasLogin);
    public record EmployeeDetailRow(TbEmployee Employee, string DesignationName, string DepartmentName, string EmploymentStatusName, string StatusName,
        string? GenderName, string? BloodGroupName, string? LoginId, List<string> LoginRoles);
    public record EmployeeDocumentRow(TbEmployeeDocument Document, string DocumentTypeName, string? UploadedByName, string StatusName);
    public record NamedItem(long Id, string Name);
    public record DocumentTypeItem(long Id, string Name, string Code, bool IsRequired);
    public record RoleItem(long RoleId, string RoleName, string? Description);

    // Every method takes the school id: there is no way to read another school's employees through this repository.
    public interface IEmployeeRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock (released at commit / rollback) so two employees created at once cannot be given the same id.</summary>
        Task LockEmployeeIdsAsync(string schoolId, CancellationToken ct);
        Task<int> GetMaxIdSequenceAsync(string schoolId, string prefix, CancellationToken ct);

        Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);

        Task<(List<EmployeeListRow> Rows, int Total)> ListAsync(string schoolId, string? search, long? designationId, long? departmentId, long? employmentStatusId,
            string? employeeType, int? statusId, int page, int pageSize, CancellationToken ct);
        Task<EmployeeDetailRow?> GetDetailAsync(string schoolId, string employeeId, CancellationToken ct);
        Task<TbEmployee?> GetForUpdateAsync(string schoolId, string employeeId, CancellationToken ct);
        Task AddAsync(TbEmployee employee, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<bool> EmailTakenAsync(string schoolId, string email, string? exceptEmployeeId, CancellationToken ct);

        // checks that a referenced row exists, belongs to the school (where school-owned) and is active
        Task<bool> DesignationUsableAsync(string schoolId, long id, int activeStatusId, CancellationToken ct);
        Task<bool> DepartmentUsableAsync(string schoolId, long id, int activeStatusId, CancellationToken ct);
        Task<bool> EmploymentStatusUsableAsync(long id, CancellationToken ct);
        /// <summary>Whether this employment status (Resigned, Terminated, Retired) keeps the login switched off.</summary>
        Task<bool> EmploymentStatusBlocksLoginAsync(long id, CancellationToken ct);
        Task<bool> GenderUsableAsync(long id, CancellationToken ct);
        Task<bool> BloodGroupUsableAsync(long id, CancellationToken ct);
        Task<long?> GetEmploymentStatusIdByCodeAsync(string code, CancellationToken ct);

        // lookups for the form
        Task<List<NamedItem>> GetDesignationsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<NamedItem>> GetDepartmentsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<NamedItem>> GetEmploymentStatusesAsync(CancellationToken ct);
        Task<List<NamedItem>> GetGendersAsync(CancellationToken ct);
        Task<List<NamedItem>> GetBloodGroupsAsync(CancellationToken ct);
        Task<List<DocumentTypeItem>> GetEmployeeDocumentTypesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>The active lut_roles rows whose role_code is in <paramref name="allowedRoleCodes"/> (an allow-list), in role id order.</summary>
        Task<List<RoleItem>> GetAssignableRolesAsync(int activeStatusId, IReadOnlyCollection<string> allowedRoleCodes, CancellationToken ct);

        // photo is a column on the employee; documents:
        Task<bool> DocumentTypeUsableAsync(string schoolId, long documentTypeId, int activeStatusId, CancellationToken ct);
        Task AddDocumentAsync(TbEmployeeDocument document, CancellationToken ct);
        Task<List<EmployeeDocumentRow>> GetDocumentsAsync(string schoolId, string employeeId, CancellationToken ct);
        Task<TbEmployeeDocument?> GetDocumentAsync(string schoolId, string employeeId, long documentId, bool tracked, CancellationToken ct);

        // login
        Task<bool> HasLoginAsync(string schoolId, string employeeId, CancellationToken ct);
        Task<TbUsers?> GetLoginUserAsync(string schoolId, string employeeId, CancellationToken ct);
        Task<List<LutRole>> GetRolesByIdsAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct);
        Task AddUserAsync(TbUsers user, CancellationToken ct);
    }
}
