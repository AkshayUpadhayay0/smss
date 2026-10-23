using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class EmployeeService : IEmployeeService
    {
        private const string EmailIndex = "uq_tb_employees_school_email";
        private const int MaxPageSize = 100;

        private readonly IEmployeeRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly INotificationService _notify;
        private readonly ILogger<EmployeeService> _logger;

        public EmployeeService(IEmployeeRepository repo, IFileStorageService storage, INotificationService notify, ILogger<EmployeeService> logger)
        {
            _repo = repo;
            _storage = storage;
            _notify = notify;
            _logger = logger;
        }

        // ---------------------------------------------------------------- lookups
        public async Task<ApiResponse<EmployeeLookupsDto>> GetLookupsAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeLookupsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<EmployeeLookupsDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                static List<LookupItemDto> Map(List<NamedItem> l) => l.Select(i => new LookupItemDto { Id = i.Id, Name = i.Name }).ToList();
                return Result(200, "Successfully fetched", new EmployeeLookupsDto
                {
                    Designations = Map(await _repo.GetDesignationsAsync(schoolId, activeId.Value, ct)),
                    Departments = Map(await _repo.GetDepartmentsAsync(schoolId, activeId.Value, ct)),
                    EmploymentStatuses = Map(await _repo.GetEmploymentStatusesAsync(ct)),
                    Genders = Map(await _repo.GetGendersAsync(ct)),
                    BloodGroups = Map(await _repo.GetBloodGroupsAsync(ct)),
                    DocumentTypes = (await _repo.GetEmployeeDocumentTypesAsync(schoolId, activeId.Value, ct))
                        .Select(t => new EmployeeDocumentTypeDto { Id = t.Id, Name = t.Name, Code = t.Code, IsRequired = t.IsRequired }).ToList(),
                    AssignableRoles = (await _repo.GetAssignableRolesAsync(activeId.Value, RoleCodes.AssignableToEmployees, ct))
                        .Select(r => new AssignableRoleDto { RoleId = r.RoleId, RoleName = r.RoleName, Description = r.Description }).ToList()
                });
            }
            catch (Exception ex) { return Error<EmployeeLookupsDto>(ex, "fetching employee lookups"); }
        }

        // ---------------------------------------------------------------- list / get
        public async Task<ApiResponse<EmployeePagedDto>> ListAsync(string? schoolId, string? search, long? designationId, long? departmentId, long? employmentStatusId,
            string? employeeType, string? status, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeePagedDto>(400, TenantMessages.NoSchool, null);
            if (page < 1) return Result<EmployeePagedDto>(400, "page must be 1 or more.", null);
            if (pageSize < 1 || pageSize > MaxPageSize) return Result<EmployeePagedDto>(400, $"pageSize must be between 1 and {MaxPageSize}.", null);
            string? type = null;
            if (!string.IsNullOrWhiteSpace(employeeType))
            {
                type = new[] { EmployeeTypes.Teaching, EmployeeTypes.NonTeaching }.FirstOrDefault(t => t.Equals(employeeType.Trim(), StringComparison.OrdinalIgnoreCase));
                if (type == null) return Result<EmployeePagedDto>(400, "employeeType must be Teaching or Non-Teaching.", null);
            }
            try
            {
                int? statusId = null;
                if (!string.IsNullOrWhiteSpace(status) && !status.Trim().Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    var name = new[] { StatusNames.Active, StatusNames.Inactive }.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (name == null) return Result<EmployeePagedDto>(400, "status must be All, Active or Inactive.", null);
                    statusId = await _repo.GetStatusIdByNameAsync(name, StatusNames.GeneralType, ct);
                    if (statusId == null) return ConfigError<EmployeePagedDto>($"Status '{name}' ({StatusNames.GeneralType}) is missing in lut_status");
                }

                var (rows, total) = await _repo.ListAsync(schoolId, search, designationId, departmentId, employmentStatusId, type, statusId, page, pageSize, ct);
                return Result(200, "Successfully fetched", new EmployeePagedDto
                {
                    Total = total, Page = page, PageSize = pageSize,
                    Items = rows.Select(r => new EmployeeListItemDto
                    {
                        EmployeeId = r.Employee.EmployeeId, FullName = FullName(r.Employee), EmployeeType = r.Employee.EmployeeType,
                        DesignationId = r.Employee.DesignationId, DesignationName = r.DesignationName,
                        DepartmentId = r.Employee.DepartmentId, DepartmentName = r.DepartmentName,
                        EmploymentStatusName = r.EmploymentStatusName, MobileNumber = r.Employee.MobileNumber, Email = r.Employee.Email,
                        DateOfJoining = r.Employee.DateOfJoining, StatusName = r.StatusName, PhotoUrl = r.Employee.PhotoPath, HasLogin = r.HasLogin
                    }).ToList()
                });
            }
            catch (Exception ex) { return Error<EmployeePagedDto>(ex, "listing employees"); }
        }

        public async Task<ApiResponse<EmployeeDto>> GetAsync(string? schoolId, string employeeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // 404 for another school's employee too, so ids from other schools can't be probed
                var row = await _repo.GetDetailAsync(schoolId, employeeId, ct);
                return row == null ? Result<EmployeeDto>(404, "Employee not found.", null) : Result(200, "Successfully fetched", ToDto(row));
            }
            catch (Exception ex) { return Error<EmployeeDto>(ex, "fetching an employee"); }
        }

        // ---------------------------------------------------------------- create / update
        public async Task<ApiResponse<EmployeeDto>> CreateAsync(string? schoolId, EmployeeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<EmployeeDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<EmployeeDto>(404, "School not found.", null);
                if (string.IsNullOrWhiteSpace(school.SchoolShortCode)) return Result<EmployeeDto>(409, StudentIdHelper.NoShortCodeMessage, null);

                var employmentStatusId = req.EmploymentStatusId ?? await _repo.GetEmploymentStatusIdByCodeAsync(EmploymentStatusCodes.Active, ct);
                if (employmentStatusId == null) return ConfigError<EmployeeDto>("Employment status 'ACTIVE' is missing in lut_employment_status");

                var error = await ValidateAsync(schoolId, req, employmentStatusId.Value, activeId.Value, null, ct);
                if (error != null) return Result<EmployeeDto>(error.Value.Code, error.Value.Message, null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockEmployeeIdsAsync(schoolId, ct);   // one new employee at a time per school gets the next number
                var prefix = EmployeeIdHelper.Prefix(school.SchoolShortCode!);
                var employeeId = EmployeeIdHelper.Build(school.SchoolShortCode!, await _repo.GetMaxIdSequenceAsync(schoolId, prefix, ct) + 1);

                var now = DateTime.UtcNow;
                var employee = new TbEmployee { EmployeeId = employeeId, SchoolId = schoolId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                Apply(employee, req, employmentStatusId.Value);
                await _repo.AddAsync(employee, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return await GetCreatedAsync(schoolId, employeeId, 201, "Employee created successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) == EmailIndex)
            {
                return Result<EmployeeDto>(409, "Another employee in this school already uses this email.", null);
            }
            catch (Exception ex) { return Error<EmployeeDto>(ex, "creating an employee"); }
        }

        public async Task<ApiResponse<EmployeeDto>> UpdateAsync(string? schoolId, string employeeId, EmployeeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<EmployeeDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var employee = await _repo.GetForUpdateAsync(schoolId, employeeId, ct);
                if (employee == null) return Result<EmployeeDto>(404, "Employee not found.", null);

                var employmentStatusId = req.EmploymentStatusId ?? employee.EmploymentStatusId;
                var error = await ValidateAsync(schoolId, req, employmentStatusId, activeId.Value, employee, ct);
                if (error != null) return Result<EmployeeDto>(error.Value.Code, error.Value.Message, null);

                Apply(employee, req, employmentStatusId);
                employee.UpdatedAt = DateTime.UtcNow;
                // a change to Resigned / Terminated / Retired (or back out of them) re-evaluates the login
                await SyncLoginAsync(schoolId, employee, activeId.Value, ct);
                await _repo.SaveChangesAsync(ct);

                return await GetCreatedAsync(schoolId, employeeId, 200, "Employee updated successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) == EmailIndex)
            {
                return Result<EmployeeDto>(409, "Another employee in this school already uses this email.", null);
            }
            catch (Exception ex) { return Error<EmployeeDto>(ex, "updating an employee"); }
        }

        /// <summary>Flips Active / Inactive (the hide flag). The employee's login, if any, follows it, so a hidden / duplicate record cannot still sign in.</summary>
        public async Task<ApiResponse<EmployeeDto>> ToggleStatusAsync(string? schoolId, string employeeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<EmployeeDto>("Active / Inactive statuses are missing in lut_status");

                var employee = await _repo.GetForUpdateAsync(schoolId, employeeId, ct);
                if (employee == null) return Result<EmployeeDto>(404, "Employee not found.", null);

                var target = employee.StatusId == activeId ? inactiveId.Value : activeId.Value;
                employee.StatusId = target;
                employee.UpdatedAt = DateTime.UtcNow;
                // activating the row does NOT by itself re-enable a login that the employment status (Resigned, ...) is holding off
                await SyncLoginAsync(schoolId, employee, activeId.Value, ct);
                await _repo.SaveChangesAsync(ct);

                return await GetCreatedAsync(schoolId, employeeId, 200, target == activeId ? "Employee activated." : "Employee deactivated.", ct);
            }
            catch (Exception ex) { return Error<EmployeeDto>(ex, "toggling an employee's status"); }
        }

        // ---------------------------------------------------------------- photo
        public async Task<ApiResponse<EmployeePhotoDto>> UploadPhotoAsync(string? schoolId, string employeeId, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeePhotoDto>(400, TenantMessages.NoSchool, null);
            string? savedUrl = null;
            var committed = false;
            try
            {
                var (image, error) = await ImageUploadValidator.ValidateAsync(content, fileName, length, "Photo", ct);
                if (image == null) return Result<EmployeePhotoDto>(400, error!, null);
                using var _ = image;

                var employee = await _repo.GetForUpdateAsync(schoolId, employeeId, ct);
                if (employee == null) return Result<EmployeePhotoDto>(404, "Employee not found.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(school?.SchoolCode))
                    return Result<EmployeePhotoDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedUrl = await _storage.SaveEmployeePhotoAsync(school!.SchoolCode, employeeId, image.Content, image.Extension, ct);
                var oldUrl = employee.PhotoPath;
                employee.PhotoPath = savedUrl;
                employee.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                committed = true;

                if (!string.IsNullOrEmpty(oldUrl)) _storage.Delete(oldUrl);   // replaced: remove the previous file
                return Result(200, "Photo uploaded successfully.", new EmployeePhotoDto { EmployeeId = employeeId, PhotoUrl = savedUrl });
            }
            catch (Exception ex)
            {
                if (savedUrl != null && !committed) _storage.Delete(savedUrl);   // no orphan file if the DB save failed
                return Error<EmployeePhotoDto>(ex, "uploading an employee photo");
            }
        }

        public async Task<ApiResponse<EmployeePhotoDto>> RemovePhotoAsync(string? schoolId, string employeeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeePhotoDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var employee = await _repo.GetForUpdateAsync(schoolId, employeeId, ct);
                if (employee == null) return Result<EmployeePhotoDto>(404, "Employee not found.", null);

                var oldUrl = employee.PhotoPath;
                if (!string.IsNullOrEmpty(oldUrl))
                {
                    employee.PhotoPath = null;
                    employee.UpdatedAt = DateTime.UtcNow;
                    await _repo.SaveChangesAsync(ct);
                    _storage.Delete(oldUrl);
                }
                return Result(200, "Photo removed.", new EmployeePhotoDto { EmployeeId = employeeId, PhotoUrl = null });
            }
            catch (Exception ex) { return Error<EmployeePhotoDto>(ex, "removing an employee photo"); }
        }

        // ---------------------------------------------------------------- documents
        public async Task<ApiResponse<List<EmployeeDocumentDto>>> GetDocumentsAsync(string? schoolId, string employeeId, bool includeRemoved, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<EmployeeDocumentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                if (await _repo.GetForUpdateAsync(schoolId, employeeId, ct) == null) return Result<List<EmployeeDocumentDto>>(404, "Employee not found.", null);
                var rows = await _repo.GetDocumentsAsync(schoolId, employeeId, ct);
                return Result(200, "Successfully fetched", rows.Where(r => includeRemoved || r.StatusName == StatusNames.Active).Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<EmployeeDocumentDto>>(ex, "listing employee documents"); }
        }

        public async Task<ApiResponse<EmployeeDocumentDto>> UploadDocumentAsync(string? schoolId, string employeeId, long documentTypeId, string? remarks, long uploadedByUserId,
            Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeDocumentDto>(400, TenantMessages.NoSchool, null);
            string? savedKey = null;
            var committed = false;
            try
            {
                remarks = Clean(remarks);
                if (remarks != null && remarks.Length > 500) return Result<EmployeeDocumentDto>(400, "Remarks can be at most 500 characters.", null);
                var (doc, error) = await DocumentUploadValidator.ValidateAsync(content, fileName, length, ct);
                if (doc == null) return Result<EmployeeDocumentDto>(400, error!, null);
                using var _ = doc;

                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<EmployeeDocumentDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                if (await _repo.GetForUpdateAsync(schoolId, employeeId, ct) == null) return Result<EmployeeDocumentDto>(404, "Employee not found.", null);
                if (!await _repo.DocumentTypeUsableAsync(schoolId, documentTypeId, activeId.Value, ct))
                    return Result<EmployeeDocumentDto>(400, "The selected document type was not found for employees in your school.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(school?.SchoolCode))
                    return Result<EmployeeDocumentDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedKey = await _storage.SaveEmployeeDocumentAsync(school!.SchoolCode, employeeId, doc.Content, doc.Extension, ct);
                var now = DateTime.UtcNow;
                var entity = new TbEmployeeDocument
                {
                    EmployeeId = employeeId, SchoolId = schoolId, DocumentTypeId = documentTypeId, DocumentPath = savedKey,
                    OriginalFileName = SafeFileName(fileName), ContentType = DocumentRules.ContentTypeOf(doc.Extension), FileSize = doc.Length,
                    Remarks = remarks, UploadedBy = uploadedByUserId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now
                };
                await _repo.AddDocumentAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                committed = true;

                var row = (await _repo.GetDocumentsAsync(schoolId, employeeId, ct)).First(r => r.Document.EmployeeDocumentId == entity.EmployeeDocumentId);
                return Result(201, "Document uploaded successfully.", ToDto(row));
            }
            catch (Exception ex)
            {
                if (savedKey != null && !committed) _storage.DeletePrivate(savedKey);   // no orphan file if the DB save failed
                return Error<EmployeeDocumentDto>(ex, "uploading an employee document");
            }
        }

        public async Task<(ApiResponse<EmployeeDocumentDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenDocumentAsync(
            string? schoolId, string employeeId, long documentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return (Result<EmployeeDocumentDto>(400, TenantMessages.NoSchool, null), null, null, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                var doc = await _repo.GetDocumentAsync(schoolId, employeeId, documentId, false, ct);
                // a removed document is hidden: 404, same as one that never existed (or belongs to another school)
                if (doc == null || doc.StatusId != activeId) return (Result<EmployeeDocumentDto>(404, "Document not found.", null), null, null, null);

                var stream = _storage.OpenPrivate(doc.DocumentPath);
                if (stream == null)
                {
                    _logger.LogError("Employee document {DocumentId} is in the database but its file is missing", documentId);
                    return (Result<EmployeeDocumentDto>(404, "The file for this document could not be found.", null), null, null, null);
                }
                return (null, stream, doc.ContentType, doc.OriginalFileName);
            }
            catch (Exception ex) { return (Error<EmployeeDocumentDto>(ex, "opening an employee document"), null, null, null); }
        }

        /// <summary>Soft remove: the row is marked Inactive and hidden; the file and the record stay for the audit trail.</summary>
        public async Task<ApiResponse<EmployeeDocumentDto>> RemoveDocumentAsync(string? schoolId, string employeeId, long documentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeDocumentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<EmployeeDocumentDto>("Active / Inactive statuses are missing in lut_status");

                var doc = await _repo.GetDocumentAsync(schoolId, employeeId, documentId, true, ct);
                if (doc == null || doc.StatusId != activeId) return Result<EmployeeDocumentDto>(404, "Document not found.", null);

                doc.StatusId = inactiveId.Value;
                doc.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                var row = (await _repo.GetDocumentsAsync(schoolId, employeeId, ct)).First(r => r.Document.EmployeeDocumentId == documentId);
                return Result(200, "Document removed.", ToDto(row));
            }
            catch (Exception ex) { return Error<EmployeeDocumentDto>(ex, "removing an employee document"); }
        }

        // ---------------------------------------------------------------- login
        /// <summary>
        /// Same shape as the student create-login: the login id is the employee's own id, a temporary password is generated and returned ONCE (only its
        /// hash is stored) and emailed when there is an address. Unlike the student version the caller chooses the roles, and the password is random.
        /// </summary>
        public async Task<ApiResponse<EmployeeLoginCreatedDto>> CreateLoginAsync(string? schoolId, string employeeId, CreateEmployeeLoginRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeLoginCreatedDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var employee = await _repo.GetForUpdateAsync(schoolId, employeeId, ct);
                if (employee == null) return Result<EmployeeLoginCreatedDto>(404, "Employee not found.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<EmployeeLoginCreatedDto>(404, "School not found.", null);

                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<EmployeeLoginCreatedDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                if (employee.StatusId != activeId) return Result<EmployeeLoginCreatedDto>(409, "This employee record is inactive, so a login cannot be created.", null);

                if (await _repo.EmploymentStatusBlocksLoginAsync(employee.EmploymentStatusId, ct))
                    return Result<EmployeeLoginCreatedDto>(409, "This employee's employment status (Resigned, Terminated or Retired) does not allow a login.", null);

                if (await _repo.HasLoginAsync(schoolId, employeeId, ct)) return Result<EmployeeLoginCreatedDto>(409, "This employee already has a login.", null);

                // roles: read from lut_roles; each must exist, be active and be on the employee allow-list (by role_code; never Super Admin, Student or Guardian)
                var wanted = (req.RoleIds ?? new()).Distinct().ToList();
                if (wanted.Count == 0) return Result<EmployeeLoginCreatedDto>(400, "Choose at least one role.", null);
                var roles = await _repo.GetRolesByIdsAsync(wanted, ct);
                var bad = wanted.Where(id => roles.All(r => r.RoleId != id) || roles.First(r => r.RoleId == id) is var r && (r.StatusId != activeId || !RoleCodes.AssignableToEmployees.Contains(r.RoleCode))).ToList();
                if (bad.Count > 0) return Result<EmployeeLoginCreatedDto>(400, "One or more of the selected roles can't be assigned.", null);

                var tempPassword = PasswordHelper.GenerateTemporaryPassword();
                var displayName = FullName(employee);
                var now = DateTime.UtcNow;
                var user = new TbUsers
                {
                    SchoolId = schoolId, UserType = UserTypes.Employee, OrgUserId = employee.EmployeeId, DisplayName = displayName,
                    Email = employee.Email, MobileNumber = employee.MobileNumber,
                    PasswordHash = PasswordHelper.Hash(tempPassword), IsFirstLogin = true, StatusId = activeId, CreatedAt = now, UpdatedAt = now
                };
                foreach (var role in roles) user.UserRoles.Add(new TbUserRoles { RoleId = role.RoleId, CreatedAt = now });
                await _repo.AddUserAsync(user, ct);

                // a failed email never fails the login creation (the password is shown on screen once anyway)
                var sentTo = await _notify.SendAsync(user.Email, null,
                    EmailTemplates.EmployeeCredentials(displayName, school.SchoolName, employee.EmployeeId, tempPassword, _notify.LoginUrl),
                    $"employee login credentials for {employee.EmployeeId}", ct);

                return Result(201, "Employee login created successfully.", new EmployeeLoginCreatedDto
                {
                    EmployeeId = employee.EmployeeId, DisplayName = displayName, OrgUserId = employee.EmployeeId, TemporaryPassword = tempPassword,
                    Roles = roles.OrderBy(r => r.RoleId).Select(r => r.RoleName).ToList(), EmailSent = sentTo != null, EmailSentTo = sentTo
                });
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two clicks at once: the unique (school_id, org_user_id) index let only one through
                return Result<EmployeeLoginCreatedDto>(409, "This employee already has a login.", null);
            }
            catch (Exception ex) { return Error<EmployeeLoginCreatedDto>(ex, "creating an employee login"); }
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>
        /// A login works only while BOTH switches say enabled: the row is Active AND the employment status does not block login (Resigned,
        /// Terminated, Retired; On Leave does not). The login's status is recomputed from both every time either changes, so turning one switch
        /// back on never re-enables a login the other still holds off. Does nothing when the employee has no login. The caller saves.
        /// </summary>
        private async Task SyncLoginAsync(string schoolId, TbEmployee employee, int activeStatusId, CancellationToken ct)
        {
            var login = await _repo.GetLoginUserAsync(schoolId, employee.EmployeeId, ct);
            if (login == null) return;
            var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
            if (inactiveId == null) throw new InvalidOperationException($"Status '{StatusNames.Inactive}' ({StatusNames.GeneralType}) is missing in lut_status");

            var enabled = employee.StatusId == activeStatusId && !await _repo.EmploymentStatusBlocksLoginAsync(employee.EmploymentStatusId, ct);
            var wanted = enabled ? activeStatusId : inactiveId.Value;
            if (login.StatusId == wanted) return;
            login.StatusId = wanted;
            login.UpdatedAt = DateTime.UtcNow;
        }

        private Task<int?> ActiveStatusIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        private async Task<ApiResponse<EmployeeDto>> GetCreatedAsync(string schoolId, string employeeId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetDetailAsync(schoolId, employeeId, ct);
            return row == null ? Result<EmployeeDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row));
        }

        /// <summary>Everything the request refers to must exist and (where it is a school master) belong to this school and be active; an unchanged inactive master is allowed on update.</summary>
        private async Task<(int Code, string Message)?> ValidateAsync(string schoolId, EmployeeRequest req, long employmentStatusId, int activeStatusId, TbEmployee? existing, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
            if (req.DateOfBirth!.Value >= today) return (400, "Date of birth must be in the past.");
            if (req.DateOfJoining!.Value < req.DateOfBirth.Value) return (400, "Date of joining cannot be before the date of birth.");
            if (req.DateOfLeaving != null && req.DateOfLeaving < req.DateOfJoining) return (400, "Date of leaving cannot be before the date of joining.");

            if (!(existing?.DesignationId == req.DesignationId || await _repo.DesignationUsableAsync(schoolId, req.DesignationId!.Value, activeStatusId, ct)))
                return (400, "The selected designation was not found for your school.");
            if (!(existing?.DepartmentId == req.DepartmentId || await _repo.DepartmentUsableAsync(schoolId, req.DepartmentId!.Value, activeStatusId, ct)))
                return (400, "The selected department was not found for your school.");
            if (!await _repo.EmploymentStatusUsableAsync(employmentStatusId, ct)) return (400, "The selected employment status was not found.");
            if (req.GenderId != null && !await _repo.GenderUsableAsync(req.GenderId.Value, ct)) return (400, "The selected gender was not found.");
            if (req.BloodGroupId != null && !await _repo.BloodGroupUsableAsync(req.BloodGroupId.Value, ct)) return (400, "The selected blood group was not found.");

            var email = Clean(req.Email);
            if (email != null && await _repo.EmailTakenAsync(schoolId, email, existing?.EmployeeId, ct)) return (409, "Another employee in this school already uses this email.");
            return null;
        }

        /// <summary>Copies the editable fields. Blank optional text becomes null (an empty string would fail the email / phone rules and clutter the data).</summary>
        private static void Apply(TbEmployee e, EmployeeRequest r, long employmentStatusId)
        {
            e.DesignationId = r.DesignationId!.Value;
            e.DepartmentId = r.DepartmentId!.Value;
            e.EmploymentStatusId = employmentStatusId;
            e.EmployeeType = r.EmployeeType!;
            e.FirstName = r.FirstName!.Trim();
            e.MiddleName = Clean(r.MiddleName);
            e.LastName = r.LastName!.Trim();
            e.GenderId = r.GenderId;
            e.BloodGroupId = r.BloodGroupId;
            e.DateOfBirth = r.DateOfBirth!.Value;
            e.Email = Clean(r.Email);
            e.MobileNumber = r.MobileNumber!.Trim();
            e.AlternateMobile = Clean(r.AlternateMobile);
            e.Address = r.Address!.Trim();
            e.DateOfJoining = r.DateOfJoining!.Value;
            e.DateOfLeaving = r.DateOfLeaving;
            e.Qualification = Clean(r.Qualification);
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string FullName(TbEmployee e) =>
            string.Join(' ', new[] { e.FirstName, e.MiddleName, e.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        /// <summary>The client's file name is only a label for the download: strip any path and keep it short.</summary>
        private static string SafeFileName(string name)
        {
            var n = Path.GetFileName(name ?? string.Empty).Trim();
            if (n.Length == 0) n = "document";
            return n.Length > 255 ? n[^255..] : n;
        }

        private static EmployeeDto ToDto(EmployeeDetailRow r)
        {
            var e = r.Employee;
            return new EmployeeDto
            {
                EmployeeId = e.EmployeeId, FullName = FullName(e), FirstName = e.FirstName, MiddleName = e.MiddleName, LastName = e.LastName, EmployeeType = e.EmployeeType,
                DesignationId = e.DesignationId, DesignationName = r.DesignationName, DepartmentId = e.DepartmentId, DepartmentName = r.DepartmentName,
                EmploymentStatusId = e.EmploymentStatusId, EmploymentStatusName = r.EmploymentStatusName,
                GenderId = e.GenderId, GenderName = r.GenderName, BloodGroupId = e.BloodGroupId, BloodGroupName = r.BloodGroupName,
                DateOfBirth = e.DateOfBirth, Email = e.Email, MobileNumber = e.MobileNumber, AlternateMobile = e.AlternateMobile, Address = e.Address,
                DateOfJoining = e.DateOfJoining, DateOfLeaving = e.DateOfLeaving, Qualification = e.Qualification, PhotoUrl = e.PhotoPath,
                StatusName = r.StatusName, LoginId = r.LoginId, LoginRoles = r.LoginRoles, CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt
            };
        }

        private static EmployeeDocumentDto ToDto(EmployeeDocumentRow r) => new()
        {
            EmployeeDocumentId = r.Document.EmployeeDocumentId, DocumentTypeId = r.Document.DocumentTypeId, DocumentTypeName = r.DocumentTypeName,
            FileName = r.Document.OriginalFileName, ContentType = r.Document.ContentType, FileSize = r.Document.FileSize, Remarks = r.Document.Remarks,
            UploadedByName = r.UploadedByName, UploadedAt = r.Document.CreatedAt, StatusName = r.StatusName
        };

        private static string? ConstraintOf(DbUpdateException ex) => (ex.InnerException as PostgresException)?.ConstraintName;

        private ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
