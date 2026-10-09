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
    public class AcademicYearService : IAcademicYearService
    {
        private const string DuplicateMessage = "An academic year with this name already exists for your school.";

        private readonly IAcademicYearRepository _repo;
        private readonly ILogger<AcademicYearService> _logger;

        public AcademicYearService(IAcademicYearRepository repo, ILogger<AcademicYearService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetAllAsync(string? schoolId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var years = await _repo.GetBySchoolAsync(schoolId);
                var names = await StatusNamesAsync();
                return Ok(200, "Successfully fetched", years.Select(y => ToResponse(y, names)).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching academic years"); }
        }

        // 404 for another school's row too, so ids from other schools can't be probed
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var year = await _repo.GetByIdAsync(schoolId, id);
                if (year == null) return Fail(404, "Academic year not found");
                return Ok(200, "Successfully fetched", ToResponse(year, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "fetching academic year"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateAcademicYearRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var name = req.YearName.Trim();
                if (await _repo.ExistsAsync(schoolId, name)) return Fail(409, DuplicateMessage);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");

                var now = DateTime.UtcNow;
                var year = new TbAcademicYears
                {
                    SchoolId = schoolId,
                    YearName = name,
                    StartDate = req.StartDate!.Value,
                    EndDate = req.EndDate!.Value,
                    StatusId = activeId,
                    // A school always needs a current year, so its first one becomes current automatically
                    IsCurrent = !await _repo.AnyAsync(schoolId),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _repo.AddAsync(year);
                await _repo.SaveChangesAsync();

                return Ok(201, "Academic year created successfully", ToResponse(year, await StatusNamesAsync()));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating academic year"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateAcademicYearRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var year = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (year == null) return Fail(404, "Academic year not found");

                var name = req.YearName.Trim();
                if (await _repo.ExistsAsync(schoolId, name, excludeId: id)) return Fail(409, DuplicateMessage);

                year.YearName = name;
                year.StartDate = req.StartDate!.Value;
                year.EndDate = req.EndDate!.Value;
                year.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, "Academic year updated successfully", ToResponse(year, await StatusNamesAsync()));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "updating academic year"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var year = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (year == null) return Fail(404, "Academic year not found");

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType);
                if (activeId == null || inactiveId == null)
                    return ConfigError("'Active'/'Inactive' general status is missing in lut_status");

                var deactivating = year.StatusId == activeId;
                if (deactivating && year.IsCurrent)
                    return Fail(400, "Set a different year as current before deactivating this one.");

                year.StatusId = deactivating ? inactiveId : activeId;
                year.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, deactivating ? "Academic year deactivated successfully" : "Academic year activated successfully",
                    ToResponse(year, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "toggling academic year status"); }
        }

        // One SaveChanges = one transaction: the old current year is cleared and the new one set together
        public async Task<ApiResponse<object>> SetCurrentAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var year = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (year == null) return Fail(404, "Academic year not found");

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");
                if (year.StatusId != activeId)
                    return Fail(400, "This academic year is inactive. Activate it before setting it as current.");

                var now = DateTime.UtcNow;
                foreach (var other in await _repo.GetCurrentYearsAsync(schoolId))
                {
                    if (other.AcademicYearId == year.AcademicYearId) continue;
                    other.IsCurrent = false;
                    other.UpdatedAt = now;
                }
                if (!year.IsCurrent)
                {
                    year.IsCurrent = true;
                    year.UpdatedAt = now;
                }
                await _repo.SaveChangesAsync();

                return Ok(200, "Current academic year updated successfully", ToResponse(year, await StatusNamesAsync()));
            }
            catch (Exception ex) { return Error(ex, "setting current academic year"); }
        }

        // ---------------- helpers ----------------
        private async Task<Dictionary<int, string>> StatusNamesAsync() =>
            (await _repo.GetGeneralStatusesAsync(StatusNames.GeneralType)).ToDictionary(s => s.Sid, s => s.Name);

        private static AcademicYearResponse ToResponse(TbAcademicYears y, Dictionary<int, string> statusNames) => new()
        {
            AcademicYearId = y.AcademicYearId,
            SchoolId = y.SchoolId,
            YearName = y.YearName,
            StartDate = y.StartDate,
            EndDate = y.EndDate,
            IsCurrent = y.IsCurrent,
            StatusId = y.StatusId,
            StatusName = y.StatusId is int sid && statusNames.TryGetValue(sid, out var n) ? n : null,
            CreatedAt = y.CreatedAt,
            UpdatedAt = y.UpdatedAt
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
