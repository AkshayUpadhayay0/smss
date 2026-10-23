using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class EmployeeRepository : IEmployeeRepository
    {
        private readonly dbContext _db;
        public EmployeeRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockEmployeeIdsAsync(string schoolId, CancellationToken ct)
        {
            var key = "employee-id:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<int> GetMaxIdSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            // Ids are prefix + digits; the highest number is the longest then the largest string
            var last = await _db.Employees.AsNoTracking()
                .Where(e => e.SchoolId == schoolId && e.EmployeeId.StartsWith(prefix))
                .OrderByDescending(e => e.EmployeeId.Length).ThenByDescending(e => e.EmployeeId)
                .Select(e => e.EmployeeId).FirstOrDefaultAsync(ct);
            return last != null && int.TryParse(last[prefix.Length..], out var n) ? n : 0;
        }

        public Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).FirstOrDefaultAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<(List<EmployeeListRow> Rows, int Total)> ListAsync(string schoolId, string? search, long? designationId, long? departmentId, long? employmentStatusId,
            string? employeeType, int? statusId, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId);
            if (designationId != null) q = q.Where(e => e.DesignationId == designationId);
            if (departmentId != null) q = q.Where(e => e.DepartmentId == departmentId);
            if (employmentStatusId != null) q = q.Where(e => e.EmploymentStatusId == employmentStatusId);
            if (employeeType != null) q = q.Where(e => e.EmployeeType == employeeType);
            if (statusId != null) q = q.Where(e => e.StatusId == statusId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(e => EF.Functions.ILike(e.EmployeeId, like) || EF.Functions.ILike(e.FirstName + " " + (e.MiddleName ?? "") + " " + e.LastName, like)
                                 || EF.Functions.ILike(e.MobileNumber, like) || (e.Email != null && EF.Functions.ILike(e.Email, like)));
            }

            var total = await q.CountAsync(ct);
            var rows = await (
                from e in q
                join d in _db.EmployeeDesignations.AsNoTracking() on e.DesignationId equals d.DesignationId
                join dp in _db.EmployeeDepartments.AsNoTracking() on e.DepartmentId equals dp.DepartmentId
                join es in _db.LutEmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals es.EmploymentStatusId
                join st in _db.LutStatus.AsNoTracking() on e.StatusId equals st.Sid
                orderby e.FirstName, e.LastName, e.EmployeeId
                select new { e, d.DesignationName, dp.DepartmentName, es.StatusName, StatusLabel = st.Sname })
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            var ids = rows.Select(r => r.e.EmployeeId).ToList();
            var withLogin = ids.Count == 0 ? new HashSet<string>()
                : (await _db.Users.AsNoTracking().Where(u => u.SchoolId == schoolId && u.OrgUserId != null && ids.Contains(u.OrgUserId)).Select(u => u.OrgUserId!).ToListAsync(ct)).ToHashSet();

            return (rows.Select(r => new EmployeeListRow(r.e, r.DesignationName, r.DepartmentName, r.StatusName, r.StatusLabel, withLogin.Contains(r.e.EmployeeId))).ToList(), total);
        }

        public async Task<EmployeeDetailRow?> GetDetailAsync(string schoolId, string employeeId, CancellationToken ct)
        {
            var r = await (
                from e in _db.Employees.AsNoTracking()
                where e.SchoolId == schoolId && e.EmployeeId == employeeId
                join d in _db.EmployeeDesignations.AsNoTracking() on e.DesignationId equals d.DesignationId
                join dp in _db.EmployeeDepartments.AsNoTracking() on e.DepartmentId equals dp.DepartmentId
                join es in _db.LutEmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals es.EmploymentStatusId
                join st in _db.LutStatus.AsNoTracking() on e.StatusId equals st.Sid
                select new
                {
                    e, d.DesignationName, dp.DepartmentName, EmploymentStatusName = es.StatusName, StatusLabel = st.Sname,
                    GenderName = _db.LutGenders.Where(g => g.GenderId == e.GenderId).Select(g => g.GenderName).FirstOrDefault(),
                    BloodGroupName = _db.LutBloodGroups.Where(b => b.BloodGroupId == e.BloodGroupId).Select(b => b.BloodGroupName).FirstOrDefault()
                }).FirstOrDefaultAsync(ct);
            if (r == null) return null;

            var user = await _db.Users.AsNoTracking().Where(u => u.SchoolId == schoolId && u.OrgUserId == employeeId).Select(u => new { u.UserId, u.OrgUserId }).FirstOrDefaultAsync(ct);
            var roles = user == null ? new List<string>()
                : await (from ur in _db.UserRoles.AsNoTracking() join ro in _db.LutRoles.AsNoTracking() on ur.RoleId equals ro.RoleId
                         where ur.UserId == user.UserId orderby ro.RoleName select ro.RoleName).ToListAsync(ct);
            return new EmployeeDetailRow(r.e, r.DesignationName, r.DepartmentName, r.EmploymentStatusName, r.StatusLabel, r.GenderName, r.BloodGroupName, user?.OrgUserId, roles);
        }

        public Task<TbEmployee?> GetForUpdateAsync(string schoolId, string employeeId, CancellationToken ct) =>
            _db.Employees.Where(e => e.SchoolId == schoolId && e.EmployeeId == employeeId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbEmployee employee, CancellationToken ct) => await _db.Employees.AddAsync(employee, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<bool> EmailTakenAsync(string schoolId, string email, string? exceptEmployeeId, CancellationToken ct)
        {
            var lower = email.ToLower();
            return _db.Employees.AnyAsync(e => e.SchoolId == schoolId && e.Email != null && e.Email.ToLower() == lower && e.EmployeeId != exceptEmployeeId, ct);
        }

        public Task<bool> DesignationUsableAsync(string schoolId, long id, int activeStatusId, CancellationToken ct) =>
            _db.EmployeeDesignations.AnyAsync(d => d.SchoolId == schoolId && d.DesignationId == id && d.StatusId == activeStatusId, ct);
        public Task<bool> DepartmentUsableAsync(string schoolId, long id, int activeStatusId, CancellationToken ct) =>
            _db.EmployeeDepartments.AnyAsync(d => d.SchoolId == schoolId && d.DepartmentId == id && d.StatusId == activeStatusId, ct);
        public Task<bool> EmploymentStatusUsableAsync(long id, CancellationToken ct) =>
            _db.LutEmploymentStatuses.AnyAsync(s => s.EmploymentStatusId == id && s.IsActive, ct);
        public Task<bool> EmploymentStatusBlocksLoginAsync(long id, CancellationToken ct) =>
            _db.LutEmploymentStatuses.AnyAsync(s => s.EmploymentStatusId == id && s.BlocksLogin, ct);
        public Task<bool> GenderUsableAsync(long id, CancellationToken ct) => _db.LutGenders.AnyAsync(g => g.GenderId == id && g.IsActive, ct);
        public Task<bool> BloodGroupUsableAsync(long id, CancellationToken ct) => _db.LutBloodGroups.AnyAsync(b => b.BloodGroupId == id && b.IsActive, ct);
        public Task<long?> GetEmploymentStatusIdByCodeAsync(string code, CancellationToken ct) =>
            _db.LutEmploymentStatuses.AsNoTracking().Where(s => s.StatusCode == code).Select(s => (long?)s.EmploymentStatusId).FirstOrDefaultAsync(ct);

        public Task<List<NamedItem>> GetDesignationsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.EmployeeDesignations.AsNoTracking().Where(d => d.SchoolId == schoolId && d.StatusId == activeStatusId).OrderBy(d => d.DesignationName)
                .Select(d => new NamedItem(d.DesignationId, d.DesignationName)).ToListAsync(ct);
        public Task<List<NamedItem>> GetDepartmentsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.EmployeeDepartments.AsNoTracking().Where(d => d.SchoolId == schoolId && d.StatusId == activeStatusId).OrderBy(d => d.DepartmentName)
                .Select(d => new NamedItem(d.DepartmentId, d.DepartmentName)).ToListAsync(ct);
        public Task<List<NamedItem>> GetEmploymentStatusesAsync(CancellationToken ct) =>
            _db.LutEmploymentStatuses.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.EmploymentStatusId).Select(s => new NamedItem(s.EmploymentStatusId, s.StatusName)).ToListAsync(ct);
        public Task<List<NamedItem>> GetGendersAsync(CancellationToken ct) =>
            _db.LutGenders.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.GenderId).Select(g => new NamedItem(g.GenderId, g.GenderName)).ToListAsync(ct);
        public Task<List<NamedItem>> GetBloodGroupsAsync(CancellationToken ct) =>
            _db.LutBloodGroups.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.BloodGroupId).Select(b => new NamedItem(b.BloodGroupId, b.BloodGroupName)).ToListAsync(ct);
        public Task<List<DocumentTypeItem>> GetEmployeeDocumentTypesAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.DocumentTypes.AsNoTracking().Where(t => t.SchoolId == schoolId && t.StatusId == activeStatusId && (t.AppliesTo == "EMPLOYEE" || t.AppliesTo == "BOTH"))
                .OrderBy(t => t.DocumentName).Select(t => new DocumentTypeItem(t.DocumentTypeId, t.DocumentName, t.DocumentCode, t.IsRequired)).ToListAsync(ct);
        public Task<List<RoleItem>> GetAssignableRolesAsync(int activeStatusId, IReadOnlyCollection<string> allowedRoleCodes, CancellationToken ct)
        {
            var allowed = allowedRoleCodes.ToArray();
            return _db.LutRoles.AsNoTracking().Where(r => r.StatusId == activeStatusId && allowed.Contains(r.RoleCode)).OrderBy(r => r.RoleId)
                .Select(r => new RoleItem(r.RoleId, r.RoleName, r.Description)).ToListAsync(ct);
        }

        public Task<bool> DocumentTypeUsableAsync(string schoolId, long documentTypeId, int activeStatusId, CancellationToken ct) =>
            _db.DocumentTypes.AnyAsync(t => t.SchoolId == schoolId && t.DocumentTypeId == documentTypeId && t.StatusId == activeStatusId && (t.AppliesTo == "EMPLOYEE" || t.AppliesTo == "BOTH"), ct);
        public async Task AddDocumentAsync(TbEmployeeDocument document, CancellationToken ct) => await _db.EmployeeDocuments.AddAsync(document, ct);

        public async Task<List<EmployeeDocumentRow>> GetDocumentsAsync(string schoolId, string employeeId, CancellationToken ct)
        {
            var rows = await (
                from d in _db.EmployeeDocuments.AsNoTracking()
                where d.SchoolId == schoolId && d.EmployeeId == employeeId
                join t in _db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals t.DocumentTypeId
                join st in _db.LutStatus.AsNoTracking() on d.StatusId equals st.Sid
                orderby d.CreatedAt descending, d.EmployeeDocumentId descending
                select new
                {
                    d, t.DocumentName, StatusLabel = st.Sname,
                    UploadedByName = _db.Users.Where(u => u.UserId == d.UploadedBy).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault()
                }).ToListAsync(ct);
            return rows.Select(r => new EmployeeDocumentRow(r.d, r.DocumentName, r.UploadedByName, r.StatusLabel)).ToList();
        }

        public Task<TbEmployeeDocument?> GetDocumentAsync(string schoolId, string employeeId, long documentId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.EmployeeDocuments.AsQueryable() : _db.EmployeeDocuments.AsNoTracking();
            return q.Where(d => d.SchoolId == schoolId && d.EmployeeId == employeeId && d.EmployeeDocumentId == documentId).FirstOrDefaultAsync(ct);
        }

        public Task<bool> HasLoginAsync(string schoolId, string employeeId, CancellationToken ct) =>
            _db.Users.AnyAsync(u => u.SchoolId == schoolId && u.OrgUserId == employeeId, ct);
        public Task<TbUsers?> GetLoginUserAsync(string schoolId, string employeeId, CancellationToken ct) =>
            _db.Users.Where(u => u.SchoolId == schoolId && u.OrgUserId == employeeId).FirstOrDefaultAsync(ct);
        public Task<List<LutRole>> GetRolesByIdsAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct)
        {
            var ids = roleIds.ToArray();
            return _db.LutRoles.AsNoTracking().Where(r => ids.Contains(r.RoleId)).ToListAsync(ct);
        }
        public async Task AddUserAsync(TbUsers user, CancellationToken ct)
        {
            await _db.Users.AddAsync(user, ct);
            await _db.SaveChangesAsync(ct);
        }
    }
}
