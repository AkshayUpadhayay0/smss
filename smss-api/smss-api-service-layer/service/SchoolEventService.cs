using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class SchoolEventService : ISchoolEventService
    {
        public const int MaxRangeDays = 400;      // a month grid is 42 days; a year view fits too
        public const int MaxTake = 50;

        private readonly ISchoolEventRepository _repo;
        private readonly ILogger<SchoolEventService> _logger;

        public SchoolEventService(ISchoolEventRepository repo, ILogger<SchoolEventService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- write
        public async Task<ApiResponse<SchoolEventDto>> CreateAsync(string? schoolId, long createdByUserId, SchoolEventRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchoolEventDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<SchoolEventDto>();
                var checkedReq = await ValidateAsync(schoolId, req, ct);
                if (checkedReq.Error != null) return Result<SchoolEventDto>(checkedReq.Error.Value.Code, checkedReq.Error.Value.Message, null);
                var v = checkedReq.Value!;

                var now = DateTime.UtcNow;
                var ev = new TbSchoolEvent { SchoolId = schoolId, CreatedBy = createdByUserId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                Apply(ev, v);
                await _repo.AddAsync(ev, ct);
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, ev.EventId, 201, "Event created.", ct);
            }
            catch (Exception ex) { return Error<SchoolEventDto>(ex, "creating an event"); }
        }

        public async Task<ApiResponse<SchoolEventDto>> UpdateAsync(string? schoolId, long eventId, SchoolEventRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchoolEventDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<SchoolEventDto>();
                // 404 for another school's event and for one that was deleted
                var ev = await _repo.GetActiveForUpdateAsync(schoolId, eventId, activeId.Value, ct);
                if (ev == null) return Result<SchoolEventDto>(404, "Event not found.", null);

                var checkedReq = await ValidateAsync(schoolId, req, ct);
                if (checkedReq.Error != null) return Result<SchoolEventDto>(checkedReq.Error.Value.Code, checkedReq.Error.Value.Message, null);
                Apply(ev, checkedReq.Value!);
                ev.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, eventId, 200, "Event updated.", ct);
            }
            catch (Exception ex) { return Error<SchoolEventDto>(ex, "updating an event"); }
        }

        public async Task<ApiResponse<SchoolEventDto>> DeleteAsync(string? schoolId, long eventId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchoolEventDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<SchoolEventDto>();
                var ev = await _repo.GetActiveForUpdateAsync(schoolId, eventId, activeId.Value, ct);
                if (ev == null) return Result<SchoolEventDto>(404, "Event not found.", null);

                ev.StatusId = inactiveId.Value;
                ev.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, eventId, 200, "Event deleted.", ct);
            }
            catch (Exception ex) { return Error<SchoolEventDto>(ex, "deleting an event"); }
        }

        // ---------------------------------------------------------------- read
        public async Task<ApiResponse<List<SchoolEventDto>>> GetRangeAsync(string? schoolId, long? academicYearId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<SchoolEventDto>>(400, TenantMessages.NoSchool, null);
            if (from == null || to == null) return Result<List<SchoolEventDto>>(400, "from and to are required.", null);
            if (to < from) return Result<List<SchoolEventDto>>(400, "'to' cannot be before 'from'.", null);
            if (to.Value.DayNumber - from.Value.DayNumber + 1 > MaxRangeDays) return Result<List<SchoolEventDto>>(400, $"The range can be at most {MaxRangeDays} days.", null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<List<SchoolEventDto>>();
                if (academicYearId != null && await _repo.GetYearAsync(schoolId, academicYearId.Value, ct) == null)
                    return Result<List<SchoolEventDto>>(400, "The selected academic year was not found for your school.", null);
                var rows = await _repo.GetOverlappingAsync(schoolId, academicYearId, from.Value, to.Value, activeId.Value, ct);
                return Result(200, "Successfully fetched", rows.Select(r => ToDto(r, null)).ToList());
            }
            catch (Exception ex) { return Error<List<SchoolEventDto>>(ex, "listing events"); }
        }

        public async Task<ApiResponse<List<SchoolEventDto>>> GetUpcomingAsync(string? schoolId, int take, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<SchoolEventDto>>(400, TenantMessages.NoSchool, null);
            if (take < 1 || take > MaxTake) return Result<List<SchoolEventDto>>(400, $"take must be between 1 and {MaxTake}.", null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<List<SchoolEventDto>>();
                var today = SchoolClock.Today();
                var rows = await _repo.GetUpcomingAsync(schoolId, today, take, activeId.Value, ct);
                return Result(200, "Successfully fetched", rows.Select(r => ToDto(r, today)).ToList());
            }
            catch (Exception ex) { return Error<List<SchoolEventDto>>(ex, "listing upcoming events"); }
        }

        public async Task<ApiResponse<List<EventCategoryDto>>> GetCategoriesAsync(CancellationToken ct)
        {
            try
            {
                var rows = await _repo.GetActiveCategoriesAsync(ct);
                return Result(200, "Successfully fetched", rows.Select(c => new EventCategoryDto { EventCategoryId = c.Id, CategoryName = c.Name, CategoryCode = c.Code, ColorHex = c.ColorHex }).ToList());
            }
            catch (Exception ex) { return Error<List<EventCategoryDto>>(ex, "listing event categories"); }
        }

        // ---------------------------------------------------------------- helpers
        private record Checked(long AcademicYearId, string Title, DateOnly Start, DateOnly End, bool AllDay, TimeOnly? StartTime, TimeOnly? EndTime, long CategoryId, string? Description);
        private record CheckResult(Checked? Value, (int Code, string Message)? Error);

        /// <summary>All the rules in one place, shared by create and update.</summary>
        private async Task<CheckResult> ValidateAsync(string schoolId, SchoolEventRequest r, CancellationToken ct)
        {
            CheckResult Bad(int code, string message) => new(null, (code, message));

            var title = r.EventTitle?.Trim();
            if (string.IsNullOrEmpty(title)) return Bad(400, "Event title is required.");
            var start = r.StartDate!.Value;
            var end = r.EndDate ?? start;                           // end defaults to the start date
            if (end < start) return Bad(400, "The end date cannot be before the start date.");

            TimeOnly? st = null, et = null;
            if (r.IsAllDay)
            {
                if (r.StartTime != null || r.EndTime != null) return Bad(400, "An all-day event can't have a start or end time. Untick all-day to set times.");
            }
            else
            {
                if (r.StartTime == null || r.EndTime == null) return Bad(400, "Both a start time and an end time are required for an event that is not all-day.");
                st = r.StartTime; et = r.EndTime;
                if (end == start && et <= st) return Bad(400, "The end time must be after the start time on a single-day event.");
            }

            var category = await _repo.GetActiveCategoryAsync(r.EventCategoryId!.Value, ct);
            if (category == null) return Bad(400, "The selected category was not found.");

            // the academic year: given, or the one that contains the start date
            var year = r.AcademicYearId != null ? await _repo.GetYearAsync(schoolId, r.AcademicYearId.Value, ct) : await _repo.GetYearContainingAsync(schoolId, start, ct);
            if (year == null)
                return Bad(400, r.AcademicYearId != null ? "The selected academic year was not found for your school." : "No academic year covers the start date. Add the academic year first.");
            if (start < year.StartDate || end > year.EndDate)
                return Bad(400, $"The event must fall inside the academic year {year.YearName} ({year.StartDate:yyyy-MM-dd} to {year.EndDate:yyyy-MM-dd}).");

            var description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
            return new CheckResult(new Checked(year.AcademicYearId, title, start, end, r.IsAllDay, st, et, category.Id, description), null);
        }

        private static void Apply(TbSchoolEvent e, Checked v)
        {
            e.AcademicYearId = v.AcademicYearId;
            e.EventTitle = v.Title;
            e.StartDate = v.Start;
            e.EndDate = v.End;
            e.IsAllDay = v.AllDay;
            e.StartTime = v.StartTime;
            e.EndTime = v.EndTime;
            e.EventCategoryId = v.CategoryId;
            e.Description = v.Description;
        }

        private async Task<ApiResponse<SchoolEventDto>> RowAsync(string schoolId, long eventId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetRowAsync(schoolId, eventId, ct);
            return row == null ? Result<SchoolEventDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row, null));
        }

        private static SchoolEventDto ToDto(EventRow r, DateOnly? today) => new()
        {
            EventId = r.Event.EventId, AcademicYearId = r.Event.AcademicYearId, EventTitle = r.Event.EventTitle, StartDate = r.Event.StartDate, EndDate = r.Event.EndDate,
            IsAllDay = r.Event.IsAllDay, StartTime = r.Event.StartTime, EndTime = r.Event.EndTime, EventCategoryId = r.Event.EventCategoryId, CategoryName = r.CategoryName,
            CategoryCode = r.CategoryCode, ColorHex = r.ColorHex, Description = r.Event.Description, CreatedByName = r.CreatedByName,
            IsOngoing = today != null && r.Event.StartDate < today
        };

        private Task<int?> ActiveIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
