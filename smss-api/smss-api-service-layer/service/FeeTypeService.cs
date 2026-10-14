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
    public class FeeTypeService : IFeeTypeService
    {
        private const string DuplicateMessage = "A fee type with this code already exists for your school.";

        private readonly IFeeTypeRepository _repo;
        private readonly ILogger<FeeTypeService> _logger;

        public FeeTypeService(IFeeTypeRepository repo, ILogger<FeeTypeService> logger)
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
                return Ok(200, "Successfully fetched", items.Select(ToResponse).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching fee types"); }
        }

        // 404 for another school's row too, so ids from other schools can't be probed
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id);
                if (entity == null) return Fail(404, "Fee type not found");
                return Ok(200, "Successfully fetched", ToResponse(entity));
            }
            catch (Exception ex) { return Error(ex, "fetching fee type"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateFeeTypeRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                // Codes are stored upper-case, so the duplicate check compares the normalised value
                var code = req.FeeTypeCode.Trim().ToUpperInvariant();
                if (await _repo.CodeExistsAsync(schoolId, code)) return Fail(409, DuplicateMessage);

                var now = DateTime.UtcNow;
                var entity = new TbFeeTypes
                {
                    SchoolId = schoolId,
                    FeeTypeName = req.FeeTypeName.Trim(),
                    FeeTypeCode = code,
                    Description = Clean(req.Description),
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();

                return Ok(201, "Fee type created successfully", ToResponse(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating fee type"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateFeeTypeRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Fee type not found");

                var code = req.FeeTypeCode.Trim().ToUpperInvariant();
                if (await _repo.CodeExistsAsync(schoolId, code, excludeId: id)) return Fail(409, DuplicateMessage);

                entity.FeeTypeName = req.FeeTypeName.Trim();
                entity.FeeTypeCode = code;
                entity.Description = Clean(req.Description);
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, "Fee type updated successfully", ToResponse(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "updating fee type"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Fee type not found");

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, entity.IsActive ? "Fee type activated successfully" : "Fee type deactivated successfully", ToResponse(entity));
            }
            catch (Exception ex) { return Error(ex, "toggling fee type status"); }
        }

        // ---------------- helpers ----------------
        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static FeeTypeResponse ToResponse(TbFeeTypes x) => new()
        {
            FeeTypeId = x.FeeTypeId,
            SchoolId = x.SchoolId,
            FeeTypeName = x.FeeTypeName,
            FeeTypeCode = x.FeeTypeCode,
            Description = x.Description,
            IsActive = x.IsActive,
            StatusName = x.IsActive ? StatusNames.Active : StatusNames.Inactive,
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
    }
}
