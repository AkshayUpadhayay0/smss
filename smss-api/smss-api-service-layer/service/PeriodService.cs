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
    public class PeriodService : IPeriodService
    {
        private const string NumberConstraint = "uq_tb_periods_school_number";

        private readonly IPeriodRepository _repo;
        private readonly ILogger<PeriodService> _logger;

        public PeriodService(IPeriodRepository repo, ILogger<PeriodService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<List<PeriodDto>>> ListAsync(string? schoolId, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<PeriodDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<PeriodDto>>();
                return Result(200, "Successfully fetched", (await _repo.ListAsync(schoolId, includeInactive, activeId.Value, ct)).Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<PeriodDto>>(ex, "listing periods"); }
        }

        public async Task<ApiResponse<PeriodDto>> CreateAsync(string? schoolId, PeriodRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PeriodDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<PeriodDto>();
                var number = (short)req.PeriodNumber!.Value;
                if (req.EndTime <= req.StartTime) return Result<PeriodDto>(400, "The end time must be after the start time.", null);
                if (await _repo.NumberTakenAsync(schoolId, number, null, ct)) return Result<PeriodDto>(409, $"Period {number} already exists (it may be inactive: activate it instead).", null);
                var overlap = await _repo.FindOverlapAsync(schoolId, req.StartTime!.Value, req.EndTime!.Value, null, activeId.Value, ct);
                if (overlap != null) return Result<PeriodDto>(409, $"These times overlap period {overlap}.", null);

                var now = DateTime.UtcNow;
                var p = new TbPeriod { SchoolId = schoolId, PeriodNumber = number, StartTime = req.StartTime.Value, EndTime = req.EndTime.Value, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                await _repo.AddAsync(p, ct);
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, p.PeriodId, activeId.Value, 201, "Period created.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NumberConstraint)
            {
                return Result<PeriodDto>(409, "That period number was just created by someone else.", null);
            }
            catch (Exception ex) { return Error<PeriodDto>(ex, "creating a period"); }
        }

        public async Task<ApiResponse<PeriodDto>> UpdateAsync(string? schoolId, long periodId, PeriodRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PeriodDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<PeriodDto>();
                var p = await _repo.GetForUpdateAsync(schoolId, periodId, ct);
                if (p == null) return Result<PeriodDto>(404, "Period not found.", null);

                var number = (short)req.PeriodNumber!.Value;
                if (req.EndTime <= req.StartTime) return Result<PeriodDto>(400, "The end time must be after the start time.", null);
                if (await _repo.NumberTakenAsync(schoolId, number, periodId, ct)) return Result<PeriodDto>(409, $"Period {number} already exists.", null);
                if (p.StatusId == activeId)
                {
                    var overlap = await _repo.FindOverlapAsync(schoolId, req.StartTime!.Value, req.EndTime!.Value, periodId, activeId.Value, ct);
                    if (overlap != null) return Result<PeriodDto>(409, $"These times overlap period {overlap}.", null);
                }

                p.PeriodNumber = number;
                p.StartTime = req.StartTime!.Value;
                p.EndTime = req.EndTime!.Value;
                p.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, periodId, activeId.Value, 200, "Period updated.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NumberConstraint)
            {
                return Result<PeriodDto>(409, "That period number is already taken.", null);
            }
            catch (Exception ex) { return Error<PeriodDto>(ex, "updating a period"); }
        }

        public async Task<ApiResponse<PeriodDto>> ToggleStatusAsync(string? schoolId, long periodId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PeriodDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<PeriodDto>();
                var p = await _repo.GetForUpdateAsync(schoolId, periodId, ct);
                if (p == null) return Result<PeriodDto>(404, "Period not found.", null);

                if (p.StatusId == activeId)
                {
                    var used = await _repo.ActiveEntryCountAsync(schoolId, periodId, activeId.Value, ct);
                    if (used > 0) return Result<PeriodDto>(409, $"Period {p.PeriodNumber} is used by {used} timetable {(used == 1 ? "entry" : "entries")}. Clear those first.", null);
                    p.StatusId = inactiveId.Value;
                }
                else
                {
                    var overlap = await _repo.FindOverlapAsync(schoolId, p.StartTime, p.EndTime, periodId, activeId.Value, ct);
                    if (overlap != null) return Result<PeriodDto>(409, $"Its times overlap period {overlap}, so it can't be activated.", null);
                    p.StatusId = activeId.Value;
                }
                p.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, periodId, activeId.Value, 200, p.StatusId == activeId ? "Period activated." : "Period deactivated.", ct);
            }
            catch (Exception ex) { return Error<PeriodDto>(ex, "toggling a period"); }
        }

        private async Task<ApiResponse<PeriodDto>> RowAsync(string schoolId, long periodId, int activeId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetRowAsync(schoolId, periodId, activeId, ct);
            return row == null ? Result<PeriodDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row));
        }

        private static PeriodDto ToDto(PeriodListRow r) => new()
        {
            PeriodId = r.Period.PeriodId, PeriodNumber = r.Period.PeriodNumber, StartTime = r.Period.StartTime, EndTime = r.Period.EndTime, StatusName = r.StatusName, ActiveEntryCount = r.ActiveEntryCount
        };

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
