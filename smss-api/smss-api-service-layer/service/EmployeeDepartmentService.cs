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
    public class EmployeeDepartmentService : IEmployeeDepartmentService
    {
        private const string DuplicateMessage = "A department with this name already exists for your school.";

        private readonly IEmployeeDepartmentRepository _repo;
        private readonly ILogger<EmployeeDepartmentService> _logger;

        public EmployeeDepartmentService(IEmployeeDepartmentRepository repo, ILogger<EmployeeDepartmentService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetAllAsync(string? schoolId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var items = await _repo.GetBySchoolAsync(schoolId);
                var names = await StatusNamesAsync();
                return Ok(200, "Successfully fetched", items.Select(x => ToResponse(x, names)).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching department list"); }
        }

        // 404 for another school's row too, so ids from other schools can't be probed
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id);
                if (entity == null) return Fail(404, "Department not found");
                return Ok(200, "Successfully fetched", ToResponse(entity, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "fetching department"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateEmployeeDepartmentRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var name = req.DepartmentName.Trim();
                if (await _repo.ExistsAsync(schoolId, name)) return Fail(409, DuplicateMessage);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");

                var now = DateTime.UtcNow;
                var entity = new TbEmployeeDepartments
                {
                    SchoolId = schoolId,
                    DepartmentName = name,
                    Description = Clean(req.Description),
                    StatusId = activeId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();

                return Ok(201, "Department created successfully", ToResponse(entity, await StatusNamesAsync()));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating department"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateEmployeeDepartmentRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Department not found");

                var name = req.DepartmentName.Trim();
                if (await _repo.ExistsAsync(schoolId, name, excludeId: id)) return Fail(409, DuplicateMessage);

                entity.DepartmentName = name;
                entity.Description = Clean(req.Description);
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, "Department updated successfully", ToResponse(entity, await StatusNamesAsync()));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "updating department"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Department not found");

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType);
                if (activeId == null || inactiveId == null)
                    return ConfigError("'Active'/'Inactive' general status is missing in lut_status");

                var deactivating = entity.StatusId == activeId;
                entity.StatusId = deactivating ? inactiveId : activeId;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, deactivating ? "Department deactivated successfully" : "Department activated successfully",
                    ToResponse(entity, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "toggling department status"); }
        }

        // ---------------- helpers ----------------
        private async Task<Dictionary<int, string>> StatusNamesAsync() =>
            (await _repo.GetGeneralStatusesAsync(StatusNames.GeneralType)).ToDictionary(s => s.Sid, s => s.Name);

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static EmployeeDepartmentResponse ToResponse(TbEmployeeDepartments x, Dictionary<int, string> statusNames) => new()
        {
            DepartmentId = x.DepartmentId,
            SchoolId = x.SchoolId,
            DepartmentName = x.DepartmentName,
            Description = x.Description,
            StatusId = x.StatusId,
            StatusName = x.StatusId is int sid && statusNames.TryGetValue(sid, out var n) ? n : null,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static ApiResponse<object> Ok(int code, string message, object data) =>
            new() { Status = true, StatusCode = code, Message = message, Data = data };

        private static ApiResponse<object> Fail(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message, Data = null };

        private ApiResponse<object> Error(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Fail(500, "Something went wrong. Please try again later.");
        }

        private ApiResponse<object> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Fail(500, "The system is not configured correctly. Please contact support.");
        }
    }
}
