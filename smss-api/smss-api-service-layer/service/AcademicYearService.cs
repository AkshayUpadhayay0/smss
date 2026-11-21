using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.context;
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
                var usage = await _repo.GetUsageAsync(schoolId);
                return Ok(200, "Successfully fetched", years.Select(y => ToResponse(y, names, usage)).ToList());
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
                return Ok(200, "Successfully fetched", await ResponseAsync(schoolId, year));
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

                var overlap = await _repo.FindOverlapAsync(schoolId, req.StartDate!.Value, req.EndDate!.Value, activeId.Value, null);
                if (overlap != null) return Fail(409, OverlapMessage(overlap));

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

                return Ok(201, "Academic year created successfully", await ResponseAsync(schoolId, year));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (DbUpdateException ex) when (IsExclusionViolation(ex)) { return Fail(409, DatabaseOverlapMessage); }
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

                var newStart = req.StartDate!.Value;
                var newEnd = req.EndDate!.Value;
                if (newStart != year.StartDate || newEnd != year.EndDate)
                {
                    var datesError = await CheckDateChangeAsync(schoolId, year, newStart, newEnd);
                    if (datesError != null) return datesError;
                }

                year.YearName = name;
                year.StartDate = newStart;
                year.EndDate = newEnd;
                year.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, "Academic year updated successfully", await ResponseAsync(schoolId, year));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (DbUpdateException ex) when (IsExclusionViolation(ex)) { return Fail(409, DatabaseOverlapMessage); }
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

                if (deactivating)
                {
                    var used = (await _repo.GetUsageAsync(schoolId)).FirstOrDefault(u => u.AcademicYearId == year.AcademicYearId);
                    if (used != null && used.Total > 0)
                        return Fail(409, $"This academic year can't be deactivated because it already has data ({UsageText(used)}). Close it instead.");
                }
                else
                {
                    var overlap = await _repo.FindOverlapAsync(schoolId, year.StartDate, year.EndDate, activeId.Value, year.AcademicYearId);
                    if (overlap != null) return Fail(409, OverlapMessage(overlap));
                }

                year.StatusId = deactivating ? inactiveId : activeId;
                year.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, deactivating ? "Academic year deactivated successfully" : "Academic year activated successfully",
                    await ResponseAsync(schoolId, year));
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
                if (year.ClosedAt != null)
                    return Fail(409, "A closed academic year can't be made current. Reopen it first.");

                var now = DateTime.UtcNow;
                // the database allows ONE current year per school (a unique index), so the old one is cleared first, in the same transaction
                await using var tx = await _repo.BeginTransactionAsync();
                var cleared = false;
                foreach (var other in await _repo.GetCurrentYearsAsync(schoolId))
                {
                    if (other.AcademicYearId == year.AcademicYearId) continue;
                    other.IsCurrent = false;
                    other.UpdatedAt = now;
                    cleared = true;
                }
                if (cleared) await _repo.SaveChangesAsync();
                if (!year.IsCurrent)
                {
                    year.IsCurrent = true;
                    year.UpdatedAt = now;
                }
                await _repo.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(200, "Current academic year updated successfully", await ResponseAsync(schoolId, year));
            }
            catch (Exception ex) { return Error(ex, "setting current academic year"); }
        }

        // Closing blocks configuration, attendance, enrolments, homework, class tests and timetable edits for the year (see ClosedYear);
        // fee payments, voids, receipts and every read stay open. The current year can never be closed.
        public async Task<ApiResponse<object>> CloseAsync(string? schoolId, long id, long userId, CloseAcademicYearRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var year = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (year == null) return Fail(404, "Academic year not found");
                if (year.IsCurrent) return Fail(409, "The current academic year can't be closed. Set another year as current first.");
                if (year.ClosedAt != null) return Fail(409, "This academic year is already closed.");
                var note = string.IsNullOrWhiteSpace(req?.Note) ? null : req!.Note!.Trim();
                year.ClosedAt = DateTime.UtcNow;
                year.ClosedBy = userId;
                year.CloseNote = note;
                year.UpdatedAt = year.ClosedAt.Value;
                await _repo.SaveChangesAsync();
                _logger.LogInformation("Academic year {YearId} of school {SchoolId} closed by user {UserId}", id, schoolId, userId);
                return Ok(200, "Academic year closed successfully", await ResponseAsync(schoolId, year));
            }
            catch (Exception ex) { return Error(ex, "closing academic year"); }
        }

        public async Task<ApiResponse<object>> ReopenAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var year = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (year == null) return Fail(404, "Academic year not found");
                if (year.ClosedAt == null) return Fail(409, "This academic year is not closed.");
                year.ClosedAt = null;
                year.ClosedBy = null;
                year.CloseNote = null;
                year.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();
                return Ok(200, "Academic year reopened successfully", await ResponseAsync(schoolId, year));
            }
            catch (Exception ex) { return Error(ex, "reopening academic year"); }
        }

        // ---------------- helpers ----------------
        private const string DatabaseOverlapMessage = "These dates overlap another active academic year of your school. Active academic years can't overlap.";

        private static string OverlapMessage(TbAcademicYears other) =>
            $"These dates overlap the academic year {other.YearName} ({other.StartDate:dd MMM yyyy} to {other.EndDate:dd MMM yyyy}). Active academic years can't overlap.";

        private static bool IsExclusionViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };

        private static string UsageText(AcademicYearUsage u)
        {
            var parts = new List<string>();
            void Add(int n, string what) { if (n > 0) parts.Add($"{n} {what}"); }
            Add(u.Enrolments, "enrolment(s)"); Add(u.FeeStructures, "fee structure(s)"); Add(u.AttendanceRows, "attendance record(s)"); Add(u.TimetableEntries, "timetable slot(s)");
            Add(u.ClassTeachers, "class teacher assignment(s)"); Add(u.Homework, "homework item(s)"); Add(u.Tests, "class test(s)"); Add(u.Events, "calendar event(s)");
            return string.Join(", ", parts);
        }

        // New dates must keep every dated record of the year inside the year, must not overlap another active year, and a closed year's dates are frozen.
        private async Task<ApiResponse<object>?> CheckDateChangeAsync(string schoolId, TbAcademicYears year, DateOnly newStart, DateOnly newEnd)
        {
            if (year.ClosedAt != null) return Fail(409, "This academic year is closed, so its dates can't be changed. Reopen it first.");
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
            if (activeId != null && year.StatusId == activeId)
            {
                var overlap = await _repo.FindOverlapAsync(schoolId, newStart, newEnd, activeId.Value, year.AcademicYearId);
                if (overlap != null) return Fail(409, OverlapMessage(overlap));
            }
            var used = (await _repo.GetUsageAsync(schoolId)).FirstOrDefault(u => u.AcademicYearId == year.AcademicYearId);
            if (used?.MinDate is DateOnly first && used.MaxDate is DateOnly last && (first < newStart || last > newEnd))
                return Fail(409, $"The dates can't be changed to {newStart:dd MMM yyyy} - {newEnd:dd MMM yyyy} because this year already has records dated {first:dd MMM yyyy} to {last:dd MMM yyyy} ({UsageText(used)}). The new dates must include all of them.");
            return null;
        }

        private async Task<AcademicYearResponse> ResponseAsync(string schoolId, TbAcademicYears y) =>
            ToResponse(y, await StatusNamesAsync(), await _repo.GetUsageAsync(schoolId));

        private async Task<Dictionary<int, string>> StatusNamesAsync() =>
            (await _repo.GetGeneralStatusesAsync(StatusNames.GeneralType)).ToDictionary(s => s.Sid, s => s.Name);

        private static AcademicYearResponse ToResponse(TbAcademicYears y, Dictionary<int, string> statusNames, IReadOnlyList<AcademicYearUsage>? usage = null) => new()
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
            UpdatedAt = y.UpdatedAt,
            State = AcademicYearStates.Of(y.ClosedAt != null, y.IsCurrent, y.StartDate, y.EndDate, SchoolClock.Today()),
            IsClosed = y.ClosedAt != null,
            ClosedAt = y.ClosedAt,
            CloseNote = y.CloseNote,
            Usage = usage?.FirstOrDefault(u => u.AcademicYearId == y.AcademicYearId) is { } u
                ? new AcademicYearUsageDto { Enrolments = u.Enrolments, FeeStructures = u.FeeStructures, AttendanceRows = u.AttendanceRows, TimetableEntries = u.TimetableEntries, ClassTeachers = u.ClassTeachers, Homework = u.Homework, Tests = u.Tests, Events = u.Events }
                : new AcademicYearUsageDto()
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
