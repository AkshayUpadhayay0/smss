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
    public class SectionService : ISectionService
    {
        private const string DuplicateMessage = "This class already has a section with this name.";
        private const string InvalidClassMessage = "The selected class was not found for your school.";

        private readonly ISectionRepository _repo;
        private readonly ILogger<SectionService> _logger;

        public SectionService(ISectionRepository repo, ILogger<SectionService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetAllAsync(string? schoolId, long? classId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var rows = await _repo.GetBySchoolAsync(schoolId, classId);
                var names = await StatusNamesAsync();
                return Ok(200, "Successfully fetched", rows.Select(r => ToResponse(r, names)).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching sections"); }
        }

        // 404 for another school's row too, so ids from other schools can't be probed
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var row = await _repo.GetWithClassByIdAsync(schoolId, id);
                if (row == null) return Fail(404, "Section not found");
                return Ok(200, "Successfully fetched", ToResponse(row, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "fetching section"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateSectionRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var classId = req.ClassId!.Value;
                // The DB would accept another school's class_id, so this check is what keeps tenants apart
                if (await _repo.GetClassAsync(schoolId, classId) == null) return Fail(400, InvalidClassMessage);

                var name = req.SectionName.Trim();
                if (await _repo.ExistsAsync(classId, name)) return Fail(409, DuplicateMessage);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");

                var now = DateTime.UtcNow;
                var entity = new TbSections
                {
                    SchoolId = schoolId,
                    ClassId = classId,
                    SectionName = name,
                    MaxStrength = (short?)req.MaxStrength,
                    StatusId = activeId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();

                return await RespondAsync(201, "Section created successfully", schoolId, entity.SectionId);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating section"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateSectionRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Section not found");

                var classId = req.ClassId!.Value;
                if (await _repo.GetClassAsync(schoolId, classId) == null) return Fail(400, InvalidClassMessage);

                var name = req.SectionName.Trim();
                if (await _repo.ExistsAsync(classId, name, excludeId: id)) return Fail(409, DuplicateMessage);

                entity.ClassId = classId;
                entity.SectionName = name;
                entity.MaxStrength = (short?)req.MaxStrength;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return await RespondAsync(200, "Section updated successfully", schoolId, id);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "updating section"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Section not found");

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType);
                if (activeId == null || inactiveId == null)
                    return ConfigError("'Active'/'Inactive' general status is missing in lut_status");

                var deactivating = entity.StatusId == activeId;
                entity.StatusId = deactivating ? inactiveId : activeId;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return await RespondAsync(200, deactivating ? "Section deactivated successfully" : "Section activated successfully", schoolId, id);
            }
            catch (Exception ex) { return Error(ex, "toggling section status"); }
        }

        // ---------------- helpers ----------------
        // Re-reads the saved row through the join so every response carries the class name
        private async Task<ApiResponse<object>> RespondAsync(int code, string message, string schoolId, long id)
        {
            var row = await _repo.GetWithClassByIdAsync(schoolId, id);
            return row == null ? Fail(404, "Section not found") : Ok(code, message, ToResponse(row, await StatusNamesAsync()));
        }

        private async Task<Dictionary<int, string>> StatusNamesAsync() =>
            (await _repo.GetGeneralStatusesAsync(StatusNames.GeneralType)).ToDictionary(s => s.Sid, s => s.Name);

        private static SectionResponse ToResponse(SectionWithClass r, Dictionary<int, string> statusNames)
        {
            var s = r.Section;
            return new SectionResponse
            {
                SectionId = s.SectionId,
                SchoolId = s.SchoolId,
                ClassId = s.ClassId,
                ClassName = r.ClassName,
                ClassSequenceOrder = r.ClassSequenceOrder,
                SectionName = s.SectionName,
                MaxStrength = s.MaxStrength,
                StatusId = s.StatusId,
                StatusName = s.StatusId is int sid && statusNames.TryGetValue(sid, out var n) ? n : null,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }

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
