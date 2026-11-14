using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Which transport area a student uses (School Admin writes, Principal reads) and the student's own "Where is my bus" answer.</summary>
    public class StudentTransportService : TransportServiceBase, IStudentTransportService
    {
        private const string UniqueIndex = "uq_tb_student_transport_student_year";

        private readonly IStudentTransportRepository _repo;
        private readonly IDriverDutyRepository _duty;
        private readonly TransportLiveOptions _o;
        private readonly TimeProvider _clock;

        public StudentTransportService(IStudentTransportRepository repo, IDriverDutyRepository duty, IOptions<TransportLiveOptions> options, TimeProvider clock, ILogger<StudentTransportService> logger) : base(logger)
        {
            _repo = repo;
            _duty = duty;
            _o = options.Value;
            _clock = clock;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;
        private Task<int?> ActiveIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        /// <summary>The year to work with: the given one (404 when it is not this school's) or the school's current one (409 when none is set).</summary>
        private async Task<(TransportYearRef? Year, ApiResponse<StudentTransportDto>? Error)> ResolveYearAsync(string schoolId, long? academicYearId, CancellationToken ct)
        {
            if (academicYearId != null)
            {
                var y = await _repo.GetYearAsync(schoolId, academicYearId.Value, ct);
                return y == null ? (null, Result<StudentTransportDto>(404, "Academic year not found.", null)) : (y, null);
            }
            var current = await _repo.GetCurrentYearAsync(schoolId, ct);
            return current == null ? (null, Result<StudentTransportDto>(409, "No current academic year is set. Set one first (School Setup > Academic Year).", null)) : (current, null);
        }

        private async Task<StudentTransportDto> BuildAsync(string schoolId, TransportStudentRef student, TransportYearRef year, int activeId, CancellationToken ct)
        {
            var row = await _repo.GetAssignmentAsync(schoolId, student.StudentId, year.AcademicYearId, ct);
            return new StudentTransportDto
            {
                StudentId = student.StudentId, StudentName = student.FullName, AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, IsCurrentYear = year.IsCurrent,
                Assignment = row == null || row.Row.StatusId != activeId ? null : new StudentTransportAssignmentDto { AreaId = row.Row.AreaId, AreaName = row.AreaName, AreaIsActive = row.AreaStatusId == activeId }
            };
        }

        // ---------------------------------------------------------------- School Admin / Principal
        public async Task<ApiResponse<StudentTransportDto>> GetAsync(string? schoolId, string studentId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentTransportDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<StudentTransportDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentTransportDto>(404, "Student not found.", null);
                var (year, error) = await ResolveYearAsync(schoolId, academicYearId, ct);
                if (error != null) return error;
                return Result(200, "Successfully fetched", await BuildAsync(schoolId, student, year!, activeId.Value, ct));
            }
            catch (Exception ex) { return Error<StudentTransportDto>(ex, "fetching a student's transport"); }
        }

        public async Task<ApiResponse<StudentTransportDto>> AssignAsync(string? schoolId, string studentId, StudentTransportRequest? request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentTransportDto>(400, TenantMessages.NoSchool, null);
            if (request?.AreaId == null) return Result<StudentTransportDto>(400, "Choose a transport area.", null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<StudentTransportDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentTransportDto>(404, "Student not found.", null);
                var (year, error) = await ResolveYearAsync(schoolId, request.AcademicYearId, ct);
                if (error != null) return error;
                var area = await _repo.GetAreaAsync(schoolId, request.AreaId.Value, ct);
                if (area == null) return Result<StudentTransportDto>(404, "Transport area not found.", null);        // another school's area looks exactly like a missing one
                if (area.StatusId != activeId) return Result<StudentTransportDto>(400, $"The area '{area.Name}' is inactive. Choose an active area.", null);

                var existing = await _repo.GetForUpdateAsync(schoolId, studentId, year!.AcademicYearId, ct);
                var created = existing == null;
                var now = Now;
                if (existing == null)
                    await _repo.AddAsync(new TbStudentTransport { SchoolId = schoolId, StudentId = studentId, AcademicYearId = year.AcademicYearId, AreaId = area.AreaId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now }, ct);
                else
                {
                    // reassigning (or assigning again after a removal) reuses THE row: there is never a second one for a student and year
                    existing.AreaId = area.AreaId;
                    existing.StatusId = activeId.Value;
                    existing.UpdatedAt = now;
                }
                try { await _repo.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ConstraintOf(ex) == UniqueIndex) { return Result<StudentTransportDto>(409, "This student's transport was just changed by someone else. Reload and try again.", null); }

                return Result(created ? 201 : 200, created ? "Transport assigned" : "Transport updated", await BuildAsync(schoolId, student, year, activeId.Value, ct));
            }
            catch (Exception ex) { return Error<StudentTransportDto>(ex, "assigning a student's transport"); }
        }

        public async Task<ApiResponse<StudentTransportDto>> RemoveAsync(string? schoolId, string studentId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentTransportDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<StudentTransportDto>("Active / Inactive status is missing in lut_status");
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentTransportDto>(404, "Student not found.", null);
                var (year, error) = await ResolveYearAsync(schoolId, academicYearId, ct);
                if (error != null) return error;

                var existing = await _repo.GetForUpdateAsync(schoolId, studentId, year!.AcademicYearId, ct);
                if (existing != null && existing.StatusId != inactiveId.Value)
                {
                    existing.StatusId = inactiveId.Value;
                    existing.UpdatedAt = Now;
                    await _repo.SaveChangesAsync(ct);
                }
                return Result(200, existing == null ? "No transport was assigned" : "Transport removed", await BuildAsync(schoolId, student, year, activeId.Value, ct));
            }
            catch (Exception ex) { return Error<StudentTransportDto>(ex, "removing a student's transport"); }
        }

        // ---------------------------------------------------------------- the student's own view
        public async Task<ApiResponse<MyBusDto>> GetMyBusAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MyBusDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var now = Now;
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<MyBusDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                // who: the student is the caller (the login id IS the student id); nothing in the request names a student
                var studentId = await _repo.GetStudentIdForLoginAsync(schoolId, userId, ct);
                if (studentId == null) return Result<MyBusDto>(404, "Your student record was not found.", null);

                var notAssigned = Result(200, "Not assigned", new MyBusDto { Assigned = false, ServerTime = now, Message = "You are not assigned to a school bus yet. Ask your school office." });
                var year = await _repo.GetCurrentYearAsync(schoolId, ct);
                if (year == null) return notAssigned;
                var row = await _repo.GetAssignmentAsync(schoolId, studentId, year.AcademicYearId, ct);
                if (row == null || row.Row.StatusId != activeId.Value) return notAssigned;

                var dto = new MyBusDto { Assigned = true, Area = new MyBusAreaDto { AreaName = row.AreaName }, ServerTime = now };
                if (row.AreaStatusId != activeId.Value)
                {
                    dto.Message = "Your bus area is not running right now. Ask your school office.";
                    return Result(200, "Successfully fetched", dto);
                }

                // every driver ON DUTY (an open session, Active driver) who covers the student's area: the same school, the same area
                var open = await _duty.ListOpenAsync(schoolId, row.Row.AreaId, null, activeId.Value, ct);
                var times = (await _duty.GetAreaTimesAsync(schoolId, row.Row.AreaId, open.Select(o => o.DriverId).ToList(), ct)).ToDictionary(t => t.DriverId);   // THIS area's link rows only
                var shown = new HashSet<string>();
                var buses = new List<(int Rank, MyBusItemDto Item)>();
                foreach (var r in open)
                {
                    var status = DutyRules.Status(r.ReceivedAt, now, _o);
                    if (status == DutyStatuses.Offline && (now - (r.ReceivedAt ?? r.SessionStartedAt)).TotalMinutes > _o.HideOfflineAfterMinutes) continue;   // the same hiding rule as the school's live board
                    var item = new MyBusItemDto { DriverName = r.DriverName, VehicleLabel = r.VehicleLabel, VehiclePlate = r.VehiclePlate, Status = status };
                    if (times.TryGetValue(r.DriverId, out var t))
                    {
                        item.MorningPickupTime = TransportRules.FormatTime(t.MorningPickupTime);
                        item.AfternoonDropTime = TransportRules.FormatTime(t.AfternoonDropTime);
                    }
                    shown.Add(r.DriverId);
                    if (status != DutyStatuses.Offline && r.ReceivedAt != null && r.Latitude != null && r.Longitude != null)
                    {
                        // OFFLINE never carries a position; STALE carries the last one and the app marks it clearly
                        item.Latitude = r.Latitude;
                        item.Longitude = r.Longitude;
                        item.AgeSeconds = DutyRules.AgeSeconds(r.ReceivedAt.Value, now);
                    }
                    buses.Add((status == DutyStatuses.Live ? 0 : status == DutyStatuses.Stale ? 1 : 2, item));
                }
                dto.Buses = buses.OrderBy(b => b.Rank).ThenBy(b => b.Item.DriverName).Select(b => b.Item).ToList();
                // everyone else with times on this area (not on duty, or an old offline session that is hidden above): the schedule, never a second entry for a driver already shown
                dto.Scheduled = (await _duty.ListScheduledAsync(schoolId, row.Row.AreaId, activeId.Value, ct)).Where(s => !shown.Contains(s.DriverId))
                    .Select(s => new MyBusScheduledDto { DriverName = s.DriverName, VehicleLabel = s.VehicleLabel, VehiclePlate = s.VehiclePlate,
                        MorningPickupTime = TransportRules.FormatTime(s.MorningPickupTime), AfternoonDropTime = TransportRules.FormatTime(s.AfternoonDropTime) }).ToList();
                if (dto.Buses.Count == 0) dto.Message = "No bus is on duty for your area right now.";
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<MyBusDto>(ex, "fetching a student's bus"); }
        }
    }
}
