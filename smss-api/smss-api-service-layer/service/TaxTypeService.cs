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
    public class TaxTypeService : ITaxTypeService
    {
        private readonly ITaxTypeRepository _repo;
        private readonly ILogger<TaxTypeService> _logger;

        public TaxTypeService(ITaxTypeRepository repo, ILogger<TaxTypeService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<List<TaxTypeDto>>> GetAllAsync(string? schoolId, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<TaxTypeDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rows = await _repo.GetBySchoolAsync(schoolId, includeInactive, ct);
                return Result(200, "Successfully fetched", rows.Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<TaxTypeDto>>(ex, "fetching tax types"); }
        }

        public async Task<ApiResponse<TaxTypeDto>> CreateAsync(string? schoolId, TaxTypeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TaxTypeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = Validate(req, out var name, out var code, out var rate);
                if (bad != null) return Result<TaxTypeDto>(400, bad, null);
                var dup = await _repo.FindDuplicateAsync(schoolId, name, code, null, ct);
                if (dup != null) return Result<TaxTypeDto>(409, DuplicateMessage(dup), null);

                var now = DateTime.UtcNow;
                var entity = new TbTaxType
                {
                    SchoolId = schoolId, TaxName = name, TaxCode = code, RatePercent = rate,
                    Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                    IsActive = true, CreatedAt = now, UpdatedAt = now
                };
                await _repo.AddAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                return Result(201, "Tax type created successfully", ToDto(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Result<TaxTypeDto>(409, "A tax type with this name or code already exists for your school.", null); }
            catch (Exception ex) { return Error<TaxTypeDto>(ex, "creating a tax type"); }
        }

        /// <summary>The rate can be edited: it only affects NEW payments, because every payment keeps its own snapshot of name, code and rate.</summary>
        public async Task<ApiResponse<TaxTypeDto>> UpdateAsync(string? schoolId, long id, TaxTypeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TaxTypeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, true, ct);   // 404 for another school's id too
                if (entity == null) return Result<TaxTypeDto>(404, "Tax type not found.", null);

                var bad = Validate(req, out var name, out var code, out var rate);
                if (bad != null) return Result<TaxTypeDto>(400, bad, null);
                var dup = await _repo.FindDuplicateAsync(schoolId, name, code, id, ct);
                if (dup != null) return Result<TaxTypeDto>(409, DuplicateMessage(dup), null);

                entity.TaxName = name; entity.TaxCode = code; entity.RatePercent = rate;
                entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return Result(200, "Tax type updated successfully", ToDto(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Result<TaxTypeDto>(409, "A tax type with this name or code already exists for your school.", null); }
            catch (Exception ex) { return Error<TaxTypeDto>(ex, "updating a tax type"); }
        }

        public async Task<ApiResponse<TaxTypeDto>> SetActiveAsync(string? schoolId, long id, bool active, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TaxTypeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, true, ct);
                if (entity == null) return Result<TaxTypeDto>(404, "Tax type not found.", null);
                // deactivating a type that already has payments is fine: those payments keep their snapshot; it just can't be chosen again
                if (entity.IsActive != active)
                {
                    entity.IsActive = active;
                    entity.UpdatedAt = DateTime.UtcNow;
                    await _repo.SaveChangesAsync(ct);
                }
                return Result(200, active ? "Tax type activated successfully" : "Tax type deactivated successfully", ToDto(entity));
            }
            catch (Exception ex) { return Error<TaxTypeDto>(ex, "changing the status of a tax type"); }
        }

        // ---------------- helpers ----------------
        /// <summary>The rules again (the DTO attributes do the same): the server is the authority. Name and code are trimmed, the code upper-cased.</summary>
        public static string? Validate(TaxTypeRequest req, out string name, out string code, out decimal rate)
        {
            name = (req.TaxName ?? "").Trim(); code = (req.TaxCode ?? "").Trim().ToUpperInvariant(); rate = 0;
            if (name.Length == 0) return "Tax name is required.";
            if (name.Length > 100) return "Tax name can be at most 100 characters.";
            if (code.Length == 0) return "Tax code is required.";
            if (code.Length > 30) return "Tax code can be at most 30 characters.";
            if (!code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')) return "Tax code can contain only letters, numbers, hyphen and underscore.";
            if (req.RatePercent is not decimal r) return "Rate is required.";
            if (r < 0 || r > 100) return "Rate must be between 0 and 100.";
            if (decimal.Round(r, 2) != r) return "Rate can have at most 2 decimal places.";
            if (req.Description is { Length: > 500 }) return "Description can be at most 500 characters.";
            rate = r;
            return null;
        }

        private static string DuplicateMessage(string what) =>
            what == "code" ? "A tax type with this code already exists for your school." : "A tax type with this name already exists for your school.";

        private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static TaxTypeDto ToDto(TbTaxType x) => new()
        {
            TaxTypeId = x.TaxTypeId, TaxName = x.TaxName, TaxCode = x.TaxCode, RatePercent = x.RatePercent, Description = x.Description,
            IsActive = x.IsActive, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt
        };

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
