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
    public class FeeConcessionService : IFeeConcessionService
    {
        private const string DuplicateMessage = "A concession with this code already exists for your school.";
        private const string Percentage = "PERCENTAGE";
        private const string Fixed = "FIXED";

        private readonly IFeeConcessionRepository _repo;
        private readonly ILogger<FeeConcessionService> _logger;

        public FeeConcessionService(IFeeConcessionRepository repo, ILogger<FeeConcessionService> logger)
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
                var feeTypes = await _repo.GetFeeTypeNamesAsync(schoolId);
                return Ok(200, "Successfully fetched", items.Select(x => ToResponse(x, feeTypes)).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching fee concessions"); }
        }

        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id);
                if (entity == null) return Fail(404, "Fee concession not found");
                return Ok(200, "Successfully fetched", ToResponse(entity, await _repo.GetFeeTypeNamesAsync(schoolId)));
            }
            catch (Exception ex) { return Error(ex, "fetching fee concession"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateFeeConcessionRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var rule = ValidateRules(req);
                if (rule != null) return Fail(400, rule);

                var feeTypes = await _repo.GetFeeTypeNamesAsync(schoolId);
                if (req.ApplicableFeeTypeId is long ft && !feeTypes.ContainsKey(ft))
                    return Fail(400, "The selected fee type was not found for your school.");

                // Codes are stored upper-case, so the duplicate check compares the normalised value
                var code = req.ConcessionCode.Trim().ToUpperInvariant();
                if (await _repo.CodeExistsAsync(schoolId, code)) return Fail(409, DuplicateMessage);

                var now = DateTime.UtcNow;
                var entity = new TbFeeConcessions
                {
                    SchoolId = schoolId,
                    ConcessionName = req.ConcessionName.Trim(),
                    ConcessionCode = code,
                    ConcessionType = req.ConcessionType,
                    Value = req.Value!.Value,
                    ApplicableFeeTypeId = req.ApplicableFeeTypeId,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();

                return Ok(201, "Fee concession created successfully", ToResponse(entity, feeTypes));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating fee concession"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateFeeConcessionRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Fee concession not found");

                var rule = ValidateRules(req);
                if (rule != null) return Fail(400, rule);

                var feeTypes = await _repo.GetFeeTypeNamesAsync(schoolId);
                if (req.ApplicableFeeTypeId is long ft && !feeTypes.ContainsKey(ft))
                    return Fail(400, "The selected fee type was not found for your school.");

                var code = req.ConcessionCode.Trim().ToUpperInvariant();
                if (await _repo.CodeExistsAsync(schoolId, code, excludeId: id)) return Fail(409, DuplicateMessage);

                entity.ConcessionName = req.ConcessionName.Trim();
                entity.ConcessionCode = code;
                entity.ConcessionType = req.ConcessionType;
                entity.Value = req.Value!.Value;
                entity.ApplicableFeeTypeId = req.ApplicableFeeTypeId;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, "Fee concession updated successfully", ToResponse(entity, feeTypes));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "updating fee concession"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Fee concession not found");

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, entity.IsActive ? "Fee concession activated successfully" : "Fee concession deactivated successfully",
                    ToResponse(entity, await _repo.GetFeeTypeNamesAsync(schoolId)));
            }
            catch (Exception ex) { return Error(ex, "toggling fee concession status"); }
        }

        // ---------------- helpers ----------------
        /// <summary>
        /// The business rules, checked here as well as by the request validation and the table's CHECK constraints:
        /// PERCENTAGE needs 0 &lt; value &lt;= 100, FIXED needs value &gt; 0 (no upper bound, the school decides).
        /// </summary>
        private static string? ValidateRules(FeeConcessionRequestBase req)
        {
            if (req.ConcessionType is not (Percentage or Fixed)) return "Concession type must be PERCENTAGE or FIXED.";
            var value = req.Value ?? 0;
            if (value <= 0) return "Value must be greater than 0.";
            if (req.ConcessionType == Percentage && value > 100) return "A percentage concession cannot be more than 100.";
            return null;
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static FeeConcessionResponse ToResponse(TbFeeConcessions x, Dictionary<long, string> feeTypes) => new()
        {
            ConcessionId = x.ConcessionId,
            SchoolId = x.SchoolId,
            ConcessionName = x.ConcessionName,
            ConcessionCode = x.ConcessionCode,
            ConcessionType = x.ConcessionType,
            Value = x.Value,
            ApplicableFeeTypeId = x.ApplicableFeeTypeId,
            ApplicableFeeTypeName = x.ApplicableFeeTypeId is long f && feeTypes.TryGetValue(f, out var n) ? n : null,
            IsActive = x.IsActive,
            StatusName = x.IsActive ? StatusNames.Active : StatusNames.Inactive,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        };

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
