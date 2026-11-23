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
    /// <summary>
    /// S4: bell schedules (named sets of periods and breaks) and which schedule each class follows on each day of an academic year.
    /// Every write runs under the per-school academic-setup lock in one transaction. The database also refuses overlapping Active slots of one schedule
    /// (EXCLUDE), a second default schedule (partial unique), and a timetable entry on a break (foreign key on the slot type).
    /// </summary>
    public class PeriodScheduleService : IPeriodScheduleService
    {
        private const string NumberConstraint = "uq_tb_periods_schedule_number";

        private readonly IPeriodRepository _repo;
        private readonly ILogger<PeriodScheduleService> _logger;

        public PeriodScheduleService(IPeriodRepository repo, ILogger<PeriodScheduleService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ================================================================== schedules
        public async Task<ApiResponse<List<ScheduleDto>>> ListAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<ScheduleDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<List<ScheduleDto>>();
                return Result(200, "Successfully fetched", await BuildAllAsync(schoolId, s.Value.Active, ct));
            }
            catch (Exception ex) { return Error<List<ScheduleDto>>(ex, "listing bell schedules"); }
        }

        public async Task<ApiResponse<ScheduleDto>> CreateAsync(string? schoolId, long userId, CreateScheduleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ScheduleDto>();
                var name = (req.ScheduleName ?? "").Trim();
                if (name.Length == 0) return Result<ScheduleDto>(400, "Schedule name is required.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var firstEver = (await _repo.ListSchedulesAsync(schoolId, ct)).Count == 0;   // the first schedule a school ever has is its default
                if (await _repo.ScheduleNameTakenAsync(schoolId, name, null, ct)) return Result<ScheduleDto>(409, $"A schedule named \"{name}\" already exists.", null);

                List<TbPeriod>? source = null;
                if (req.CopyFromScheduleId != null)
                {
                    if (await _repo.GetScheduleForUpdateAsync(schoolId, req.CopyFromScheduleId.Value, ct) == null) return Result<ScheduleDto>(404, "The schedule to copy was not found.", null);
                    source = await _repo.ScheduleSlotsForUpdateAsync(schoolId, req.CopyFromScheduleId.Value, ct);
                }

                var now = DateTime.UtcNow;
                var created = new TbPeriodSchedule { SchoolId = schoolId, ScheduleName = name, IsDefault = firstEver, StatusId = s.Value.Active, CreatedAt = now, UpdatedAt = now, CreatedBy = userId, UpdatedBy = userId };
                await _repo.AddScheduleAsync(created, ct);
                await _repo.SaveChangesAsync(ct);
                foreach (var slot in source ?? new())
                    await _repo.AddAsync(new TbPeriod
                    {
                        SchoolId = schoolId, ScheduleId = created.ScheduleId, SlotType = slot.SlotType, PeriodNumber = slot.PeriodNumber, SlotLabel = slot.SlotLabel, SlotOrder = slot.SlotOrder,
                        StartTime = slot.StartTime, EndTime = slot.EndTime, StatusId = slot.StatusId, CreatedAt = now, UpdatedAt = now
                    }, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, created.ScheduleId, s.Value.Active, 201, "Schedule created.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Result<ScheduleDto>(409, "A schedule with that name already exists.", null); }
            catch (Exception ex) { return Error<ScheduleDto>(ex, "creating a bell schedule"); }
        }

        public async Task<ApiResponse<ScheduleDto>> RenameAsync(string? schoolId, long userId, long scheduleId, RenameScheduleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ScheduleDto>();
                var name = (req.ScheduleName ?? "").Trim();
                if (name.Length == 0) return Result<ScheduleDto>(400, "Schedule name is required.", null);
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var sched = await _repo.GetScheduleForUpdateAsync(schoolId, scheduleId, ct);
                if (sched == null) return Result<ScheduleDto>(404, "Schedule not found.", null);
                if (await _repo.ScheduleNameTakenAsync(schoolId, name, scheduleId, ct)) return Result<ScheduleDto>(409, $"A schedule named \"{name}\" already exists.", null);
                sched.ScheduleName = name; sched.UpdatedAt = DateTime.UtcNow; sched.UpdatedBy = userId;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, scheduleId, s.Value.Active, 200, "Schedule renamed.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Result<ScheduleDto>(409, "A schedule with that name already exists.", null); }
            catch (Exception ex) { return Error<ScheduleDto>(ex, "renaming a bell schedule"); }
        }

        public async Task<ApiResponse<ScheduleDto>> SetDefaultAsync(string? schoolId, long userId, long scheduleId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ScheduleDto>();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var sched = await _repo.GetScheduleForUpdateAsync(schoolId, scheduleId, ct);
                if (sched == null) return Result<ScheduleDto>(404, "Schedule not found.", null);
                if (!sched.IsDefault)
                {
                    // clear the old default first (the partial unique index allows only one), then set the new one
                    var old = (await _repo.ListSchedulesAsync(schoolId, ct)).FirstOrDefault(d => d.IsDefault);
                    if (old != null)
                    {
                        var tracked = await _repo.GetScheduleForUpdateAsync(schoolId, old.ScheduleId, ct);
                        tracked!.IsDefault = false; tracked.UpdatedAt = DateTime.UtcNow; tracked.UpdatedBy = userId;
                        await _repo.SaveChangesAsync(ct);
                    }
                    sched.IsDefault = true; sched.UpdatedAt = DateTime.UtcNow; sched.UpdatedBy = userId;
                    await _repo.SaveChangesAsync(ct);
                    // classes that follow the default now follow the new one: refuse if that would strand a timetable entry
                    var conflicts = await StrandedAsync(schoolId, s.Value.Active, ct);
                    if (conflicts.Count > 0) return Result<ScheduleDto>(409, $"Nothing was changed. Making \"{sched.ScheduleName}\" the default would leave timetable slots on periods of another schedule: {string.Join("; ", conflicts)}.", null);
                }
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, scheduleId, s.Value.Active, 200, "Default schedule changed.", ct);
            }
            catch (Exception ex) { return Error<ScheduleDto>(ex, "changing the default bell schedule"); }
        }

        public async Task<ApiResponse<object>> DeleteAsync(string? schoolId, long scheduleId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<object>(400, TenantMessages.NoSchool, null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var sched = await _repo.GetScheduleForUpdateAsync(schoolId, scheduleId, ct);
                if (sched == null) return Result<object>(404, "Schedule not found.", null);
                var usage = (await _repo.ScheduleUsageAsync(schoolId, ct)).First(u => u.ScheduleId == scheduleId);
                var why = DeleteBlock(sched, usage);
                if (why != null) return Result<object>(409, why, null);
                _repo.RemoveSlots(await _repo.ScheduleSlotsForUpdateAsync(schoolId, scheduleId, ct));
                await _repo.SaveChangesAsync(ct);
                _repo.RemoveSchedule(sched);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result<object>(200, "Schedule deleted.", new { });
            }
            catch (Exception ex) { return Error<object>(ex, "deleting a bell schedule"); }
        }

        // ================================================================== slots
        public async Task<ApiResponse<ScheduleDto>> AddSlotAsync(string? schoolId, long scheduleId, ScheduleSlotRequest req, CancellationToken ct) =>
            await SaveSlotAsync(schoolId, scheduleId, null, req, ct);

        public async Task<ApiResponse<ScheduleDto>> UpdateSlotAsync(string? schoolId, long scheduleId, long periodId, ScheduleSlotRequest req, CancellationToken ct) =>
            await SaveSlotAsync(schoolId, scheduleId, periodId, req, ct);

        private async Task<ApiResponse<ScheduleDto>> SaveSlotAsync(string? schoolId, long scheduleId, long? periodId, ScheduleSlotRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ScheduleDto>();
                var type = (req.SlotType ?? "").Trim().ToUpperInvariant();
                if (type != SlotTypes.Period && type != SlotTypes.Break) return Result<ScheduleDto>(400, "Slot type must be PERIOD or BREAK.", null);
                if (req.EndTime <= req.StartTime) return Result<ScheduleDto>(400, "The end time must be after the start time.", null);
                var label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim();
                short? number = null;
                if (type == SlotTypes.Period)
                {
                    if (req.PeriodNumber == null) return Result<ScheduleDto>(400, "A period needs a number (1 to 30).", null);
                    number = (short)req.PeriodNumber.Value;
                }
                else if (label == null) return Result<ScheduleDto>(400, "A break needs a label, for example \"Lunch\".", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var sched = await _repo.GetScheduleForUpdateAsync(schoolId, scheduleId, ct);
                if (sched == null) return Result<ScheduleDto>(404, "Schedule not found.", null);
                var slots = await _repo.ScheduleSlotsForUpdateAsync(schoolId, scheduleId, ct);
                TbPeriod? slot = null;
                if (periodId != null)
                {
                    slot = slots.FirstOrDefault(x => x.PeriodId == periodId.Value);
                    if (slot == null) return Result<ScheduleDto>(404, "Slot not found.", null);
                    if (slot.SlotType == SlotTypes.Period && type == SlotTypes.Break && await _repo.EntryCountAnyStatusAsync(schoolId, slot.PeriodId, ct) > 0)
                        return Result<ScheduleDto>(409, $"Period {slot.PeriodNumber} has timetable entries, so it can't become a break. Clear those first.", null);
                }
                if (number != null && slots.Any(x => x.PeriodNumber == number && x.PeriodId != periodId))
                    return Result<ScheduleDto>(409, $"Period {number} already exists in this schedule (it may be inactive: activate it instead).", null);
                var active = slot == null || slot.StatusId == s.Value.Active;
                if (active)
                {
                    var clash = slots.FirstOrDefault(x => x.StatusId == s.Value.Active && x.PeriodId != periodId && x.StartTime < req.EndTime && x.EndTime > req.StartTime);
                    if (clash != null) return Result<ScheduleDto>(409, $"These times overlap {Name(clash)} ({clash.StartTime:HH\\:mm} to {clash.EndTime:HH\\:mm}).", null);
                }

                var now = DateTime.UtcNow;
                if (slot == null)
                {
                    slot = new TbPeriod { SchoolId = schoolId, ScheduleId = scheduleId, StatusId = s.Value.Active, CreatedAt = now, SlotOrder = (short)(2000 + (slots.Count + 1)) };
                    await _repo.AddAsync(slot, ct);
                }
                slot.SlotType = type; slot.PeriodNumber = number; slot.SlotLabel = label; slot.StartTime = req.StartTime!.Value; slot.EndTime = req.EndTime!.Value; slot.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                await _repo.ResequenceAsync(schoolId, scheduleId, ct);   // slots stay in clock-time order
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, scheduleId, s.Value.Active, periodId == null ? 201 : 200, periodId == null ? "Slot added." : "Slot updated.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: NumberConstraint }) { return Result<ScheduleDto>(409, "That period number was just taken by someone else.", null); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation }) { return Result<ScheduleDto>(409, "These times overlap another slot of this schedule.", null); }
            catch (Exception ex) { return Error<ScheduleDto>(ex, "saving a schedule slot"); }
        }

        public async Task<ApiResponse<ScheduleDto>> ToggleSlotAsync(string? schoolId, long scheduleId, long periodId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ScheduleDto>();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var slots = await _repo.ScheduleSlotsForUpdateAsync(schoolId, scheduleId, ct);
                var slot = slots.FirstOrDefault(x => x.PeriodId == periodId);
                if (slot == null) return Result<ScheduleDto>(404, "Slot not found.", null);
                if (slot.StatusId == s.Value.Active)
                {
                    var used = await _repo.ActiveEntryCountAsync(schoolId, periodId, s.Value.Active, ct);
                    if (used > 0) return Result<ScheduleDto>(409, $"{Name(slot)} is used by {used} timetable {(used == 1 ? "entry" : "entries")}. Clear those first.", null);
                    slot.StatusId = s.Value.Inactive;
                }
                else
                {
                    var clash = slots.FirstOrDefault(x => x.StatusId == s.Value.Active && x.PeriodId != periodId && x.StartTime < slot.EndTime && x.EndTime > slot.StartTime);
                    if (clash != null) return Result<ScheduleDto>(409, $"Its times overlap {Name(clash)}, so it can't be activated.", null);
                    slot.StatusId = s.Value.Active;
                }
                slot.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, scheduleId, s.Value.Active, 200, slot.StatusId == s.Value.Active ? "Slot activated." : "Slot deactivated.", ct);
            }
            catch (Exception ex) { return Error<ScheduleDto>(ex, "toggling a schedule slot"); }
        }

        public async Task<ApiResponse<ScheduleDto>> DeleteSlotAsync(string? schoolId, long scheduleId, long periodId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ScheduleDto>();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var slot = (await _repo.ScheduleSlotsForUpdateAsync(schoolId, scheduleId, ct)).FirstOrDefault(x => x.PeriodId == periodId);
                if (slot == null) return Result<ScheduleDto>(404, "Slot not found.", null);
                var used = await _repo.EntryCountAnyStatusAsync(schoolId, periodId, ct);
                if (used > 0) return Result<ScheduleDto>(409, $"{Name(slot)} has {used} timetable {(used == 1 ? "entry" : "entries")} (past ones too), so it can't be deleted. Deactivate it instead.", null);
                _repo.RemoveSlots(new[] { slot });
                await _repo.SaveChangesAsync(ct);
                await _repo.ResequenceAsync(schoolId, scheduleId, ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, scheduleId, s.Value.Active, 200, "Slot deleted.", ct);
            }
            catch (Exception ex) { return Error<ScheduleDto>(ex, "deleting a schedule slot"); }
        }

        // ================================================================== year assignments
        public async Task<ApiResponse<YearSchedulesDto>> GetYearAsync(string? schoolId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearSchedulesDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<YearSchedulesDto>();
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<YearSchedulesDto>(404, "Academic year not found", null);
                return Result(200, "Successfully fetched", await BuildYearAsync(schoolId, year, s.Value.Active, ct));
            }
            catch (Exception ex) { return Error<YearSchedulesDto>(ex, "fetching the year's schedules"); }
        }

        public async Task<ApiResponse<YearSchedulesDto>> SaveYearAsync(string? schoolId, long userId, long academicYearId, SaveYearSchedulesRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearSchedulesDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<YearSchedulesDto>();
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<YearSchedulesDto>(404, "Academic year not found", null);
                if (year.ClosedAt != null) return Result<YearSchedulesDto>(409, ClosedYear.Message, null);

                var rows = req.Rows ?? new();
                var schedules = (await _repo.ListSchedulesAsync(schoolId, ct)).ToDictionary(d => d.ScheduleId);
                var classes = await _repo.OfferedClassesAsync(schoolId, academicYearId, s.Value.Active, ct);
                var offered = classes.Select(c => c.ClassId).ToHashSet();
                foreach (var r in rows)
                {
                    if (!schedules.TryGetValue(r.ScheduleId, out var sch)) return Result<YearSchedulesDto>(400, "A schedule in the request was not found for your school.", null);
                    if (sch.StatusId != s.Value.Active) return Result<YearSchedulesDto>(400, $"The schedule \"{sch.ScheduleName}\" is inactive.", null);
                    if (r.ClassId != null && !offered.Contains(r.ClassId.Value)) return Result<YearSchedulesDto>(400, "A class in the request is not offered in this academic year.", null);
                }
                if (rows.GroupBy(r => (r.ClassId, r.DayOfWeek)).Any(g => g.Count() > 1))
                    return Result<YearSchedulesDto>(400, "The same class and day is listed twice. Each class and day follows one schedule.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null || year.ClosedAt != null) return Result<YearSchedulesDto>(409, ClosedYear.Message, null);
                var def = await _repo.GetDefaultScheduleAsync(schoolId, ct);

                // refuse a change that would leave a timetable slot on a period of a schedule its class no longer follows that day
                var resolveRows = rows.Select(r => (r.ClassId, (short?)(r.DayOfWeek == null ? null : (short)r.DayOfWeek.Value), r.ScheduleId)).ToList();
                var stranded = new List<string>();
                var classNames = classes.ToDictionary(c => c.ClassId, c => c.ClassName);
                foreach (var use in (await _repo.EntryScheduleUseAsync(schoolId, academicYearId, s.Value.Active, ct)).OrderBy(u => u.ClassId).ThenBy(u => u.DayOfWeek))
                {
                    var follows = ScheduleResolver.Resolve(resolveRows, use.ClassId, use.DayOfWeek, def?.ScheduleId);
                    if (follows != use.ScheduleId)
                        stranded.Add($"{use.Entries} on {classNames.GetValueOrDefault(use.ClassId, "a class")} {ScheduleResolver.DayNames[use.DayOfWeek - 1]} (periods of \"{schedules.GetValueOrDefault(use.ScheduleId)?.ScheduleName}\", not \"{schedules.GetValueOrDefault(follows ?? 0)?.ScheduleName ?? "any schedule"}\")");
                }
                if (stranded.Count > 0)
                    return Result<YearSchedulesDto>(409, $"Nothing was saved. These timetable slots would be left on periods of a schedule the class no longer follows: {string.Join("; ", stranded)}. Clear or move them first.", null);

                var now = DateTime.UtcNow;
                var existing = await _repo.AssignmentsForUpdateAsync(schoolId, academicYearId, ct);
                _repo.RemoveAssignments(existing);
                await _repo.SaveChangesAsync(ct);   // a plain replace: assignments are configuration rows, not records of events
                await _repo.AddAssignmentsAsync(rows.Select(r => new TbAcademicYearSchedule
                {
                    SchoolId = schoolId, AcademicYearId = academicYearId, ClassId = r.ClassId, DayOfWeek = r.DayOfWeek == null ? null : (short)r.DayOfWeek.Value, ScheduleId = r.ScheduleId,
                    CreatedAt = now, UpdatedAt = now, CreatedBy = userId, UpdatedBy = userId
                }), ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Schedules saved", await BuildYearAsync(schoolId, year, s.Value.Active, ct));
            }
            catch (Exception ex) { return Error<YearSchedulesDto>(ex, "saving the year's schedules"); }
        }

        /// <summary>Timetable entries (Active) that sit on a period of a schedule their class does NOT follow on that day, for the year(s) with entries.</summary>
        private async Task<List<string>> StrandedAsync(string schoolId, int activeId, CancellationToken ct)
        {
            var def = await _repo.GetDefaultScheduleAsync(schoolId, ct);
            var schedules = (await _repo.ListSchedulesAsync(schoolId, ct)).ToDictionary(d => d.ScheduleId);
            var lines = new List<string>();
            foreach (var yearId in await YearsWithAssignmentsOrEntriesAsync(schoolId, ct))
            {
                var assignments = (await _repo.AssignmentsForUpdateAsync(schoolId, yearId, ct)).Select(a => (a.ClassId, a.DayOfWeek, a.ScheduleId)).ToList();
                foreach (var use in await _repo.EntryScheduleUseAsync(schoolId, yearId, activeId, ct))
                    if (ScheduleResolver.Resolve(assignments, use.ClassId, use.DayOfWeek, def?.ScheduleId) != use.ScheduleId)
                        lines.Add($"{use.Entries} slot(s) on {ScheduleResolver.DayNames[use.DayOfWeek - 1]} of class {use.ClassId} use \"{schedules.GetValueOrDefault(use.ScheduleId)?.ScheduleName}\"");
            }
            return lines;
        }

        private async Task<List<long>> YearsWithAssignmentsOrEntriesAsync(string schoolId, CancellationToken ct) =>
            (await _repo.EntryYearIdsAsync(schoolId, ct));

        // ================================================================== building the answers
        private async Task<List<ScheduleDto>> BuildAllAsync(string schoolId, int activeId, CancellationToken ct)
        {
            var schedules = await _repo.ListSchedulesAsync(schoolId, ct);
            var slots = (await _repo.AllSlotsAsync(schoolId, ct)).ToLookup(p => p.ScheduleId);
            var usage = (await _repo.ScheduleUsageAsync(schoolId, ct)).ToDictionary(u => u.ScheduleId);
            var perSlotEntries = await _repo.ActiveEntryCountsAsync(schoolId, activeId, ct);
            return schedules.Select(d =>
            {
                var u = usage.GetValueOrDefault(d.ScheduleId) ?? new ScheduleUsageRow(d.ScheduleId, 0, 0, 0);
                return new ScheduleDto
                {
                    ScheduleId = d.ScheduleId, ScheduleName = d.ScheduleName, IsDefault = d.IsDefault, Assignments = u.Assignments, TimetableEntries = u.Entries,
                    DeleteBlockedReason = DeleteBlock(d, u),
                    Slots = slots[d.ScheduleId].OrderBy(p => p.SlotOrder).Select(p => new ScheduleSlotDto
                    {
                        PeriodId = p.PeriodId, SlotType = p.SlotType, PeriodNumber = p.PeriodNumber, Label = p.SlotLabel, StartTime = p.StartTime, EndTime = p.EndTime,
                        SlotOrder = p.SlotOrder, IsActive = p.StatusId == activeId, ActiveEntryCount = perSlotEntries.GetValueOrDefault(p.PeriodId)
                    }).ToList()
                };
            }).ToList();
        }

        private async Task<ApiResponse<ScheduleDto>> OneAsync(string schoolId, long scheduleId, int activeId, int code, string message, CancellationToken ct)
        {
            var one = (await BuildAllAsync(schoolId, activeId, ct)).FirstOrDefault(d => d.ScheduleId == scheduleId);
            return one == null ? Result<ScheduleDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, one);
        }

        private async Task<YearSchedulesDto> BuildYearAsync(string schoolId, TbAcademicYears year, int activeId, CancellationToken ct)
        {
            var schedules = await _repo.ListSchedulesAsync(schoolId, ct);
            var classes = await _repo.OfferedClassesAsync(schoolId, year.AcademicYearId, activeId, ct);
            var rows = await _repo.AssignmentsForUpdateAsync(schoolId, year.AcademicYearId, ct);
            var byId = schedules.ToDictionary(d => d.ScheduleId);
            var classNames = classes.ToDictionary(c => c.ClassId, c => c.ClassName);
            return new YearSchedulesDto
            {
                AcademicYearId = year.AcademicYearId, YearName = year.YearName, IsClosed = year.ClosedAt != null, DefaultScheduleId = schedules.FirstOrDefault(d => d.IsDefault)?.ScheduleId,
                Schedules = schedules.Where(d => d.StatusId == activeId).Select(d => new ScheduleChoiceDto { ScheduleId = d.ScheduleId, ScheduleName = d.ScheduleName, IsDefault = d.IsDefault }).ToList(),
                Classes = classes.Select(c => new ScheduleClassDto { ClassId = c.ClassId, ClassName = c.ClassName }).ToList(),
                Rows = rows.OrderBy(r => r.ClassId == null ? 0 : 1).ThenBy(r => classNames.GetValueOrDefault(r.ClassId ?? 0)).ThenBy(r => r.DayOfWeek ?? 0)
                    .Select(r => new ScheduleAssignmentDto
                    {
                        ClassId = r.ClassId, ClassName = r.ClassId == null ? null : classNames.GetValueOrDefault(r.ClassId.Value), DayOfWeek = r.DayOfWeek,
                        ScheduleId = r.ScheduleId, ScheduleName = byId.GetValueOrDefault(r.ScheduleId)?.ScheduleName ?? ""
                    }).ToList()
            };
        }

        private static string? DeleteBlock(TbPeriodSchedule d, ScheduleUsageRow u)
        {
            if (d.IsDefault) return "This is the default schedule. Make another schedule the default first.";
            if (u.Assignments > 0) return $"It is assigned to {u.Assignments} class/day setting{(u.Assignments == 1 ? "" : "s")} of an academic year.";
            if (u.Entries > 0) return $"{u.Entries} timetable entr{(u.Entries == 1 ? "y sits" : "ies sit")} on its periods.";
            return null;
        }

        private static string Name(TbPeriod p) => p.SlotType == SlotTypes.Break ? $"the break \"{p.SlotLabel}\"" : $"period {p.PeriodNumber}";

        private async Task<(int Active, int Inactive)?> StatusesAsync(CancellationToken ct)
        {
            var a = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            var i = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
            return a == null || i == null ? null : (a.Value, i.Value);
        }

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
