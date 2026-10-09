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
    public class ClassService : IClassService
    {
        private const string DuplicateMessage = "A class with this name already exists for your school.";

        private readonly IClassRepository _repo;
        private readonly ILogger<ClassService> _logger;

        public ClassService(IClassRepository repo, ILogger<ClassService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetAllAsync(string? schoolId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var classes = await _repo.GetBySchoolAsync(schoolId);
                var names = await StatusNamesAsync();
                return Ok(200, "Successfully fetched", classes.Select(c => ToResponse(c, names)).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching classes"); }
        }

        // 404 for another school's row too, so ids from other schools can't be probed
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id);
                if (entity == null) return Fail(404, "Class not found");
                return Ok(200, "Successfully fetched", ToResponse(entity, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "fetching class"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateClassRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var name = req.ClassName.Trim();
                if (await _repo.ExistsAsync(schoolId, name)) return Fail(409, DuplicateMessage);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");

                var now = DateTime.UtcNow;
                var entity = new TbClasses
                {
                    SchoolId = schoolId,
                    ClassName = name,
                    ClassCode = Clean(req.ClassCode),
                    SequenceOrder = (short)req.SequenceOrder!.Value,
                    StatusId = activeId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();

                return Ok(201, "Class created successfully", ToResponse(entity, await StatusNamesAsync()));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating class"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateClassRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Class not found");

                var name = req.ClassName.Trim();
                if (await _repo.ExistsAsync(schoolId, name, excludeId: id)) return Fail(409, DuplicateMessage);

                entity.ClassName = name;
                entity.ClassCode = Clean(req.ClassCode);
                entity.SequenceOrder = (short)req.SequenceOrder!.Value;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, "Class updated successfully", ToResponse(entity, await StatusNamesAsync()));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "updating class"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Class not found");

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType);
                if (activeId == null || inactiveId == null)
                    return ConfigError("'Active'/'Inactive' general status is missing in lut_status");

                var deactivating = entity.StatusId == activeId;
                entity.StatusId = deactivating ? inactiveId : activeId;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, deactivating ? "Class deactivated successfully" : "Class activated successfully",
                    ToResponse(entity, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "toggling class status"); }
        }

        // ---------------- helpers ----------------
        private async Task<Dictionary<int, string>> StatusNamesAsync() =>
            (await _repo.GetGeneralStatusesAsync(StatusNames.GeneralType)).ToDictionary(s => s.Sid, s => s.Name);

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static ClassResponse ToResponse(TbClasses c, Dictionary<int, string> statusNames) => new()
        {
            ClassId = c.ClassId,
            SchoolId = c.SchoolId,
            ClassName = c.ClassName,
            ClassCode = c.ClassCode,
            SequenceOrder = c.SequenceOrder,
            StatusId = c.StatusId,
            StatusName = c.StatusId is int sid && statusNames.TryGetValue(sid, out var n) ? n : null,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
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
