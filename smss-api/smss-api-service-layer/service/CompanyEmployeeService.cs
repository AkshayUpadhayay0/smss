using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class CompanyEmployeeService : ICompanyEmployeeService
    {
        private readonly ICompanyFinanceRepository _repo;
        private readonly ILogger<CompanyEmployeeService> _logger;

        public CompanyEmployeeService(ICompanyFinanceRepository repo, ILogger<CompanyEmployeeService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<List<CompanyEmployeeDto>>> ListAsync(string? search, string? status, CancellationToken ct)
        {
            try
            {
                var (activeId, inactiveId) = await StatusIdsAsync(ct);
                if (activeId == null || inactiveId == null) return ConfigError<List<CompanyEmployeeDto>>();

                int? filter = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (string.Equals(status.Trim(), StatusNames.Active, StringComparison.OrdinalIgnoreCase)) filter = activeId;
                    else if (string.Equals(status.Trim(), StatusNames.Inactive, StringComparison.OrdinalIgnoreCase)) filter = inactiveId;
                    else return Result<List<CompanyEmployeeDto>>(400, "Status must be Active or Inactive.", null);
                }
                var rows = await _repo.ListEmployeesAsync(search, filter, ct);
                return Result(200, "Successfully fetched", rows.Select(e => ToDto(e, activeId.Value)).ToList());
            }
            catch (Exception ex) { return Error<List<CompanyEmployeeDto>>(ex, "listing company employees"); }
        }

        public async Task<ApiResponse<CompanyEmployeeDto>> GetAsync(long id, CancellationToken ct)
        {
            try
            {
                var (activeId, _) = await StatusIdsAsync(ct);
                if (activeId == null) return ConfigError<CompanyEmployeeDto>();
                var e = await _repo.GetEmployeeAsync(id, false, ct);
                return e == null ? Result<CompanyEmployeeDto>(404, "Employee not found", null) : Result(200, "Successfully fetched", ToDto(e, activeId.Value));
            }
            catch (Exception ex) { return Error<CompanyEmployeeDto>(ex, "fetching a company employee"); }
        }

        public async Task<ApiResponse<CompanyEmployeeDto>> CreateAsync(CompanyEmployeeRequest req, CancellationToken ct)
        {
            try
            {
                var (activeId, _) = await StatusIdsAsync(ct);
                if (activeId == null) return ConfigError<CompanyEmployeeDto>();
                var now = DateTime.UtcNow;
                var e = new TbCompanyEmployee { StatusId = activeId.Value, CreatedAt = now };
                Apply(e, req, now);
                _repo.AddEmployee(e);
                await _repo.SaveChangesAsync(ct);
                return Result(201, "Employee added", ToDto(e, activeId.Value));
            }
            catch (Exception ex) { return Error<CompanyEmployeeDto>(ex, "adding a company employee"); }
        }

        public async Task<ApiResponse<CompanyEmployeeDto>> UpdateAsync(long id, CompanyEmployeeRequest req, CancellationToken ct)
        {
            try
            {
                var (activeId, _) = await StatusIdsAsync(ct);
                if (activeId == null) return ConfigError<CompanyEmployeeDto>();
                var e = await _repo.GetEmployeeAsync(id, true, ct);
                if (e == null) return Result<CompanyEmployeeDto>(404, "Employee not found", null);
                Apply(e, req, DateTime.UtcNow);   // status is not editable here; the salary is the current one only: recorded payments keep their own amounts
                await _repo.SaveChangesAsync(ct);
                return Result(200, "Employee updated", ToDto(e, activeId.Value));
            }
            catch (Exception ex) { return Error<CompanyEmployeeDto>(ex, "updating a company employee"); }
        }

        public async Task<ApiResponse<CompanyEmployeeDto>> ToggleStatusAsync(long id, CancellationToken ct)
        {
            try
            {
                var (activeId, inactiveId) = await StatusIdsAsync(ct);
                if (activeId == null || inactiveId == null) return ConfigError<CompanyEmployeeDto>();
                var e = await _repo.GetEmployeeAsync(id, true, ct);
                if (e == null) return Result<CompanyEmployeeDto>(404, "Employee not found", null);
                e.StatusId = e.StatusId == activeId ? inactiveId.Value : activeId.Value;
                e.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return Result(200, e.StatusId == activeId ? "Employee activated" : "Employee deactivated", ToDto(e, activeId.Value));
            }
            catch (Exception ex) { return Error<CompanyEmployeeDto>(ex, "toggling a company employee"); }
        }

        // ---------------------------------------------------------------- helpers
        private async Task<(int? Active, int? Inactive)> StatusIdsAsync(CancellationToken ct) =>
            (await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct), await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct));

        private static void Apply(TbCompanyEmployee e, CompanyEmployeeRequest r, DateTime now)
        {
            e.FirstName = r.FirstName!.Trim();
            e.LastName = r.LastName!.Trim();
            e.Designation = r.Designation!.Trim();
            e.Email = Clean(r.Email);
            e.Mobile = Clean(r.Mobile);
            e.DateOfJoining = r.DateOfJoining!.Value;
            e.DateOfLeaving = r.DateOfLeaving;
            e.MonthlySalary = Math.Round(r.MonthlySalary!.Value, 2, MidpointRounding.AwayFromZero);
            e.UpdatedAt = now;
        }

        private static CompanyEmployeeDto ToDto(TbCompanyEmployee e, int activeId) => new()
        {
            CompanyEmployeeId = e.CompanyEmployeeId, FirstName = e.FirstName, LastName = e.LastName, FullName = $"{e.FirstName} {e.LastName}", Designation = e.Designation,
            Email = e.Email, Mobile = e.Mobile, DateOfJoining = e.DateOfJoining, DateOfLeaving = e.DateOfLeaving, MonthlySalary = e.MonthlySalary,
            StatusId = e.StatusId, StatusName = e.StatusId == activeId ? StatusNames.Active : StatusNames.Inactive
        };

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
