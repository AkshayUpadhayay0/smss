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
    /// S6: timetable versions. Every write to a section's timetable goes through here. A section has many timetables per year and exactly ONE ACTIVE; only the active one is read by
    /// dashboards, student and parent views, mobile, attendance and homework. Every write runs under the per-school academic-setup lock in one transaction; a closed year refuses all of them.
    /// Drafts never block anything: a teacher's double-booking is checked among ACTIVE timetables only (at activation, and when an active timetable is edited), and the database
    /// itself keeps two active timetables from booking one teacher twice (unique index on the mirrored tt_is_active).
    /// </summary>
    public class TimetableVersionService : ITimetableVersionService
    {
        private const string TeacherIndex = "uq_tb_tte_teacher_active";
        private const string SlotIndex = "uq_tb_tte_slot_per_timetable";
        private const string NameIndex = "uq_tb_tt_name";
        private const string OneActiveIndex = "uq_tb_tt_one_active";
        public const string DefaultName = "Regular timetable";

        private readonly ITimetableRepository _repo;
        private readonly ILogger<TimetableVersionService> _logger;

        public TimetableVersionService(ITimetableRepository repo, ILogger<TimetableVersionService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ================================================================== list / create / rename
        public async Task<ApiResponse<List<TimetableSummaryDto>>> ListAsync(string? schoolId, long? academicYearId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<TimetableSummaryDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<List<TimetableSummaryDto>>();
                var (year, error) = await YearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<List<TimetableSummaryDto>>(error!.Value.Code, error.Value.Message, null);
                if (await _repo.GetSectionAsync(schoolId, sectionId, ct) == null) return Result<List<TimetableSummaryDto>>(404, "Section not found.", null);
                return Result(200, "Successfully fetched", (await SummariesAsync(schoolId, year.AcademicYearId, sectionId, s.Value.Active, ct)));
            }
            catch (Exception ex) { return Error<List<TimetableSummaryDto>>(ex, "listing timetables"); }
        }

        public async Task<ApiResponse<TimetableSummaryDto>> CreateAsync(string? schoolId, long userId, CreateTimetableRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableSummaryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableSummaryDto>();
                var name = (req.TimetableName ?? "").Trim();
                if (name.Length == 0) return Result<TimetableSummaryDto>(400, "Name is required.", null);
                var (year, error) = await YearAsync(schoolId, req.AcademicYearId, ct);
                if (year == null) return Result<TimetableSummaryDto>(error!.Value.Code, error.Value.Message, null);
                if (await _repo.IsYearClosedAsync(schoolId, year.AcademicYearId, ct)) return Result<TimetableSummaryDto>(409, ClosedYear.Message, null);
                var section = await _repo.GetSectionAsync(schoolId, req.SectionId!.Value, ct);
                if (section == null) return Result<TimetableSummaryDto>(404, "Section not found.", null);
                if (section.SectionStatusId != s.Value.Active || section.ClassStatusId != s.Value.Active) return Result<TimetableSummaryDto>(400, "The selected section (or its class) is inactive.", null);
                if ((await _repo.GetOfferedSectionsOfClassAsync(schoolId, year.AcademicYearId, section.ClassId, ct)).All(o => o.SectionId != section.SectionId))
                    return Result<TimetableSummaryDto>(409, $"{section.ClassName} - {section.SectionName} is not offered in {year.YearName}. Offer it first (Academic Setup).", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                if (await _repo.IsYearClosedAsync(schoolId, year.AcademicYearId, ct)) return Result<TimetableSummaryDto>(409, ClosedYear.Message, null);
                if (await _repo.TimetableNameTakenAsync(schoolId, year.AcademicYearId, section.SectionId, name, null, ct))
                    return Result<TimetableSummaryDto>(409, $"This section already has a timetable named \"{name}\".", null);

                List<TbTimetableEntry>? source = null;
                if (req.CloneFromTimetableId != null)
                {
                    var from = await _repo.GetTimetableAsync(schoolId, req.CloneFromTimetableId.Value, ct);
                    if (from == null || from.SectionId != section.SectionId || from.AcademicYearId != year.AcademicYearId)
                        return Result<TimetableSummaryDto>(400, "The timetable to copy was not found for this section and year.", null);
                    source = (await _repo.GetTimetableEntriesForUpdateAsync(schoolId, from.TimetableId, ct)).Where(e => e.StatusId == s.Value.Active).ToList();
                }

                var created = await NewTimetableAsync(schoolId, year.AcademicYearId, section, name, userId, ct);
                await _repo.AddTimetableAsync(created, ct);
                await _repo.SaveChangesAsync(ct);
                var now = DateTime.UtcNow;
                foreach (var e in source ?? new())
                    await _repo.AddAsync(CopyEntry(e, created, e.EmployeeId, e.NeedsTeacher, userId, s.Value.Active, now), ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, created, s.Value.Active, 201, "Timetable created.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: NameIndex }) { return Result<TimetableSummaryDto>(409, "This section already has a timetable with that name.", null); }
            catch (Exception ex) { return Error<TimetableSummaryDto>(ex, "creating a timetable"); }
        }

        public async Task<ApiResponse<TimetableSummaryDto>> RenameAsync(string? schoolId, long userId, long timetableId, RenameTimetableRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableSummaryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableSummaryDto>();
                var name = (req.TimetableName ?? "").Trim();
                if (name.Length == 0) return Result<TimetableSummaryDto>(400, "Name is required.", null);
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var tt = await _repo.GetTimetableForUpdateAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<TimetableSummaryDto>(404, "Timetable not found.", null);
                if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return Result<TimetableSummaryDto>(409, ClosedYear.Message, null);
                if (await _repo.TimetableNameTakenAsync(schoolId, tt.AcademicYearId, tt.SectionId, name, tt.TimetableId, ct)) return Result<TimetableSummaryDto>(409, $"This section already has a timetable named \"{name}\".", null);
                tt.TimetableName = name; tt.UpdatedAt = DateTime.UtcNow; tt.UpdatedBy = UserOrNull(userId);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, tt, s.Value.Active, 200, "Timetable renamed.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: NameIndex }) { return Result<TimetableSummaryDto>(409, "This section already has a timetable with that name.", null); }
            catch (Exception ex) { return Error<TimetableSummaryDto>(ex, "renaming a timetable"); }
        }

        // ================================================================== slots (the grid editor)
        public async Task<ApiResponse<TimetableEntryDto>> AssignAsync(string? schoolId, long userId, long timetableId, AssignSlotRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableEntryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableEntryDto>();
                var day = req.DayOfWeek!.Value;
                if (day < 1 || day > 6) return Result<TimetableEntryDto>(400, "Day of week must be 1 (Monday) to 6 (Saturday). Sunday is not a school day.", null);
                var employeeId = string.IsNullOrWhiteSpace(req.EmployeeId) ? null : req.EmployeeId.Trim();
                var room = string.IsNullOrWhiteSpace(req.RoomNo) ? null : req.RoomNo.Trim();

                var (tt0, err0) = await EditableAsync(schoolId, timetableId, ct);
                if (tt0 == null) return Result<TimetableEntryDto>(err0!.Value.Code, err0.Value.Message, null);
                var section = (await _repo.GetSectionAsync(schoolId, tt0.SectionId, ct))!;
                if (section.SectionStatusId != s.Value.Active || section.ClassStatusId != s.Value.Active) return Result<TimetableEntryDto>(400, "The selected section (or its class) is inactive.", null);

                var ctxData = await ContextAsync(schoolId, tt0, new[] { req.PeriodId!.Value }, employeeId == null ? Array.Empty<string>() : new[] { employeeId }, s.Value.Active, ct);
                var shape = CheckSlot(ctxData, section, day, req.PeriodId!.Value, req.SubjectId!.Value, employeeId, s.Value.Active, null).FirstOrDefault(p => p.Severity == "ERROR" && p.Code != "TEACHER_CLASH");   // a clash is handled below: only an ACTIVE timetable is held to it
                if (shape != null) return Result<TimetableEntryDto>(400, shape.Message, null);

                long entryId;
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockSchoolAsync(schoolId, ct);
                    var tt = await _repo.GetTimetableForUpdateAsync(schoolId, timetableId, ct);
                    if (tt == null || tt.State == TimetableStates.Archived) return Result<TimetableEntryDto>(409, ArchivedMessage, null);
                    if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return Result<TimetableEntryDto>(409, ClosedYear.Message, null);

                    // only an ACTIVE timetable is held to the teacher check while it is edited; a draft never blocks
                    if (tt.IsActive && employeeId != null)
                    {
                        var clash = await _repo.FindTeacherConflictAsync(schoolId, tt.AcademicYearId, employeeId, (short)day, req.PeriodId.Value, tt.TimetableId, s.Value.Active, ct);
                        if (clash != null) return Result<TimetableEntryDto>(409, ClashMessage(TeacherName(ctxData.Teachers[employeeId]), clash.ClassName, clash.SectionName, clash.SubjectName, day, clash.PeriodNumber, clash.TimetableName), null);
                    }

                    var current = await _repo.GetActiveSlotForUpdateAsync(schoolId, tt.TimetableId, (short)day, req.PeriodId.Value, s.Value.Active, ct);
                    if (current != null && current.SubjectId == req.SubjectId && current.EmployeeId == employeeId && string.Equals(current.RoomNo ?? "", room ?? "", StringComparison.Ordinal))
                        return await RowAsync(schoolId, current.TimetableEntryId, s.Value.Active, 200, "This slot already has exactly these details.", ct);

                    var now = DateTime.UtcNow;
                    if (current != null)
                    {
                        // history, not deletion: the old entry stays, marked Inactive. Saved first, so the one-slot / one-booking-per-teacher indexes are never violated.
                        current.StatusId = s.Value.Inactive;
                        current.UpdatedAt = now;
                        await _repo.SaveChangesAsync(ct);
                    }
                    var next = new TbTimetableEntry
                    {
                        SchoolId = schoolId, AcademicYearId = tt.AcademicYearId, ClassId = tt.ClassId, SectionId = tt.SectionId, TimetableId = tt.TimetableId, TtIsActive = tt.IsActive,
                        DayOfWeek = (short)day, PeriodId = req.PeriodId.Value, SubjectId = req.SubjectId.Value, EmployeeId = employeeId, NeedsTeacher = employeeId == null, RoomNo = room,
                        StatusId = s.Value.Active, AssignedBy = userId, CreatedAt = now, UpdatedAt = now
                    };
                    await _repo.AddAsync(next, ct);
                    tt.UpdatedAt = now; tt.UpdatedBy = UserOrNull(userId);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    entryId = next.TimetableEntryId;
                }
                return await RowAsync(schoolId, entryId, s.Value.Active, 200, "Timetable slot saved.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: TeacherIndex or SlotIndex })
            {
                return Result<TimetableEntryDto>(409, "This slot or this teacher's period was just changed by someone else. Please refresh and try again.", null);
            }
            catch (Exception ex) { return Error<TimetableEntryDto>(ex, "saving a timetable slot"); }
        }

        public async Task<ApiResponse<TimetableEntryDto>> UnassignAsync(string? schoolId, long userId, long timetableId, UnassignSlotRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableEntryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableEntryDto>();
                var day = req.DayOfWeek!.Value;
                if (day < 1 || day > 6) return Result<TimetableEntryDto>(400, "Day of week must be 1 (Monday) to 6 (Saturday). Sunday is not a school day.", null);
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var tt = await _repo.GetTimetableForUpdateAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<TimetableEntryDto>(404, "Timetable not found.", null);
                if (tt.State == TimetableStates.Archived) return Result<TimetableEntryDto>(409, ArchivedMessage, null);
                if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return Result<TimetableEntryDto>(409, ClosedYear.Message, null);
                var current = await _repo.GetActiveSlotForUpdateAsync(schoolId, tt.TimetableId, (short)day, req.PeriodId!.Value, s.Value.Active, ct);
                if (current == null) return Result<TimetableEntryDto>(404, "There is nothing scheduled in this slot.", null);
                current.StatusId = s.Value.Inactive;
                current.UpdatedAt = DateTime.UtcNow;
                tt.UpdatedAt = DateTime.UtcNow; tt.UpdatedBy = UserOrNull(userId);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await RowAsync(schoolId, current.TimetableEntryId, s.Value.Active, 200, "Timetable slot cleared.", ct);
            }
            catch (Exception ex) { return Error<TimetableEntryDto>(ex, "clearing a timetable slot"); }
        }

        // ================================================================== validate / activate / archive / restore / delete
        public async Task<ApiResponse<TimetableValidationDto>> ValidateAsync(string? schoolId, long timetableId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableValidationDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableValidationDto>();
                var tt = await _repo.GetTimetableAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<TimetableValidationDto>(404, "Timetable not found.", null);
                return Result(200, "Successfully checked. Nothing was saved.", await CheckAllAsync(schoolId, tt, s.Value.Active, ct));
            }
            catch (Exception ex) { return Error<TimetableValidationDto>(ex, "checking a timetable"); }
        }

        public async Task<ApiResponse<TimetableActivationDto>> ActivateAsync(string? schoolId, long userId, long timetableId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableActivationDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableActivationDto>();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var tt = await _repo.GetTimetableForUpdateAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<TimetableActivationDto>(404, "Timetable not found.", null);
                if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return Result<TimetableActivationDto>(409, ClosedYear.Message, null);
                var summary0 = await CheckAllAsync(schoolId, tt, s.Value.Active, ct);
                if (tt.IsActive) return await ActivationAnswerAsync(schoolId, tt, null, summary0.SlotsWithoutTeacher, s.Value.Active, 200, "This timetable is already the active one.", ct);

                if (summary0.Errors.Count > 0)
                    return new ApiResponse<TimetableActivationDto> { Status = false, StatusCode = 409, Data = null, Message = $"This timetable can't be activated yet: {Describe(summary0.Errors)}" };

                var now = DateTime.UtcNow;
                var previous = await _repo.GetActiveTimetableForUpdateAsync(schoolId, tt.AcademicYearId, tt.SectionId, ct);
                if (previous != null)
                {
                    // the old active one becomes history in the same transaction (the database flips every one of its entries too)
                    previous.State = TimetableStates.Archived; previous.IsActive = false; previous.DeactivatedAt = now; previous.UpdatedAt = now; previous.UpdatedBy = UserOrNull(userId);
                    await _repo.SaveChangesAsync(ct);
                }
                tt.State = TimetableStates.Active; tt.IsActive = true; tt.EverActive = true; tt.FirstActivatedAt ??= now; tt.ActivatedAt = now; tt.ActivatedBy = userId; tt.DeactivatedAt = null;
                tt.UpdatedAt = now; tt.UpdatedBy = UserOrNull(userId);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await ActivationAnswerAsync(schoolId, tt, previous?.TimetableName, summary0.SlotsWithoutTeacher, s.Value.Active, 200,
                    summary0.SlotsWithoutTeacher > 0 ? $"Timetable activated. {summary0.SlotsWithoutTeacher} slot(s) still need a teacher." : "Timetable activated.", ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: TeacherIndex or OneActiveIndex })
            {
                // the database's rules caught a race the lock did not (should not happen)
                return Result<TimetableActivationDto>(409, "A teacher was booked elsewhere at the same moment, so nothing was activated. Please check the timetable again.", null);
            }
            catch (Exception ex) { return Error<TimetableActivationDto>(ex, "activating a timetable"); }
        }

        public async Task<ApiResponse<TimetableSummaryDto>> ArchiveAsync(string? schoolId, long userId, long timetableId, CancellationToken ct) =>
            await MoveAsync(schoolId, userId, timetableId, archive: true, ct);

        public async Task<ApiResponse<TimetableSummaryDto>> RestoreAsync(string? schoolId, long userId, long timetableId, CancellationToken ct) =>
            await MoveAsync(schoolId, userId, timetableId, archive: false, ct);

        private async Task<ApiResponse<TimetableSummaryDto>> MoveAsync(string? schoolId, long userId, long timetableId, bool archive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableSummaryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<TimetableSummaryDto>();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var tt = await _repo.GetTimetableForUpdateAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<TimetableSummaryDto>(404, "Timetable not found.", null);
                if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return Result<TimetableSummaryDto>(409, ClosedYear.Message, null);
                if (archive)
                {
                    if (tt.IsActive) return Result<TimetableSummaryDto>(409, "This is the active timetable. Activate another one first; this one is archived automatically.", null);
                    if (tt.State == TimetableStates.Archived) return await OneAsync(schoolId, tt, s.Value.Active, 200, "Already archived.", ct);
                    tt.State = TimetableStates.Archived;
                }
                else
                {
                    if (tt.State != TimetableStates.Archived) return await OneAsync(schoolId, tt, s.Value.Active, 200, "Not archived.", ct);
                    if (tt.EverActive) return Result<TimetableSummaryDto>(409, "This timetable was active once. Activate it again, or copy it into a new draft.", null);
                    tt.State = TimetableStates.Draft;
                }
                tt.UpdatedAt = DateTime.UtcNow; tt.UpdatedBy = UserOrNull(userId);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await OneAsync(schoolId, tt, s.Value.Active, 200, archive ? "Timetable archived." : "Timetable restored as a draft.", ct);
            }
            catch (Exception ex) { return Error<TimetableSummaryDto>(ex, "archiving a timetable"); }
        }

        public async Task<ApiResponse<object>> DeleteAsync(string? schoolId, long timetableId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<object>(400, TenantMessages.NoSchool, null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                var tt = await _repo.GetTimetableForUpdateAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<object>(404, "Timetable not found.", null);
                if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return Result<object>(409, ClosedYear.Message, null);
                if (tt.IsActive || tt.EverActive) return Result<object>(409, "A timetable that was ever active is kept as history and can't be deleted. Archive it instead.", null);
                _repo.RemoveEntries(await _repo.GetTimetableEntriesForUpdateAsync(schoolId, tt.TimetableId, ct));
                await _repo.SaveChangesAsync(ct);
                _repo.RemoveTimetable(tt);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result<object>(200, "Draft deleted.", new { });
            }
            catch (Exception ex) { return Error<object>(ex, "deleting a timetable"); }
        }

        // ================================================================== apply to other sections
        public async Task<ApiResponse<ApplyResultDto>> PreviewApplyAsync(string? schoolId, long timetableId, ApplyToSectionsRequest req, CancellationToken ct) =>
            await ApplyCoreAsync(schoolId, 0, timetableId, req, preview: true, ct);

        public async Task<ApiResponse<ApplyResultDto>> ApplyAsync(string? schoolId, long userId, long timetableId, ApplyToSectionsRequest req, CancellationToken ct) =>
            await ApplyCoreAsync(schoolId, userId, timetableId, req, preview: false, ct);

        private async Task<ApiResponse<ApplyResultDto>> ApplyCoreAsync(string? schoolId, long userId, long timetableId, ApplyToSectionsRequest req, bool preview, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ApplyResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var s = await StatusesAsync(ct);
                if (s == null) return ConfigError<ApplyResultDto>();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);   // the plan and the writes see the same bookings
                var source = await _repo.GetTimetableAsync(schoolId, timetableId, ct);
                if (source == null) return Result<ApplyResultDto>(404, "Timetable not found.", null);
                if (!preview && await _repo.IsYearClosedAsync(schoolId, source.AcademicYearId, ct)) return Result<ApplyResultDto>(409, ClosedYear.Message, null);
                var name = string.IsNullOrWhiteSpace(req.TimetableName) ? source.TimetableName : req.TimetableName.Trim();
                var sourceRows = (await _repo.GetTimetableRowsAsync(schoolId, source.TimetableId, s.Value.Active, ct));
                var offered = (await _repo.GetOfferedSectionsOfClassAsync(schoolId, source.AcademicYearId, source.ClassId, ct)).ToDictionary(o => o.SectionId);
                var subjectNames = await _repo.GetSubjectNamesAsync(schoolId, ct);

                var result = new ApplyResultDto { SourceTimetableId = source.TimetableId, Preview = preview };
                var usedInRequest = new HashSet<(string Employee, int Day, long Period)>();
                var now = DateTime.UtcNow;
                foreach (var sectionId in req.SectionIds!.Distinct())
                {
                    var item = new ApplySectionResultDto { SectionId = sectionId, SectionName = offered.TryGetValue(sectionId, out var o) ? o.SectionName : "", TimetableName = name };
                    result.Sections.Add(item);
                    if (sectionId == source.SectionId) { item.SkipReason = "This is the section the timetable belongs to."; continue; }
                    if (!offered.ContainsKey(sectionId)) { item.SkipReason = "This section is not an offered section of the same class in this year."; continue; }
                    if (await _repo.TimetableNameTakenAsync(schoolId, source.AcademicYearId, sectionId, name, null, ct)) { item.SkipReason = $"It already has a timetable named \"{name}\". Nothing is overwritten."; continue; }

                    var target = (await _repo.GetSectionAsync(schoolId, sectionId, ct))!;
                    var effective = await _repo.GetEffectiveSubjectIdsAsync(schoolId, source.AcademicYearId, sectionId, ct);
                    var plan = new List<(TtEntryRow Src, string? Teacher, bool Needs)>();
                    var busy = await _repo.GetActiveBookingsAsync(schoolId, source.AcademicYearId, sourceRows.Where(r => r.Entry.EmployeeId != null).Select(r => r.Entry.EmployeeId!).Distinct().ToList(), new long[] { -1 }, s.Value.Active, ct);
                    foreach (var row in sourceRows.OrderBy(r => r.Entry.DayOfWeek).ThenBy(r => r.PeriodNumber))
                    {
                        var e = row.Entry;
                        if (!effective.Contains(e.SubjectId))
                        {
                            item.SlotsSkipped++;
                            item.Skipped.Add(new ApplySlotNoteDto { DayOfWeek = e.DayOfWeek, PeriodNumber = row.PeriodNumber, SubjectName = row.SubjectName, Reason = $"{row.SubjectName} is not taught in {target.ClassName} - {target.SectionName}." });
                            continue;
                        }
                        string? teacher = e.EmployeeId; var needs = e.EmployeeId == null; string? why = needs ? "The source slot has no teacher." : null;
                        if (teacher != null)
                        {
                            var b = busy.FirstOrDefault(x => x.EmployeeId == teacher && x.DayOfWeek == e.DayOfWeek && x.PeriodId == e.PeriodId);
                            if (b != null) { why = $"The teacher is busy with {b.ClassName} - {b.SectionName} then."; teacher = null; needs = true; }
                            else if (usedInRequest.Contains((teacher, e.DayOfWeek, e.PeriodId))) { why = "The teacher is already given to another new timetable in this slot."; teacher = null; needs = true; }
                        }
                        plan.Add((row, teacher, needs));
                        if (needs) item.NeedsTeacher.Add(new ApplySlotNoteDto { DayOfWeek = e.DayOfWeek, PeriodNumber = row.PeriodNumber, SubjectName = row.SubjectName, Reason = why ?? "No teacher." });
                    }
                    item.SlotsCopied = plan.Count; item.SlotsNeedingTeacher = plan.Count(p => p.Needs); item.Created = !preview;
                    foreach (var p in plan.Where(p => p.Teacher != null)) usedInRequest.Add((p.Teacher!, p.Src.Entry.DayOfWeek, p.Src.Entry.PeriodId));
                    if (preview) { item.Created = true; continue; }

                    var created = await NewTimetableAsync(schoolId, source.AcademicYearId, target, name, userId, ct);
                    await _repo.AddTimetableAsync(created, ct);
                    await _repo.SaveChangesAsync(ct);
                    item.TimetableId = created.TimetableId;
                    foreach (var p in plan)
                        await _repo.AddAsync(new TbTimetableEntry
                        {
                            SchoolId = schoolId, AcademicYearId = created.AcademicYearId, ClassId = created.ClassId, SectionId = created.SectionId, TimetableId = created.TimetableId, TtIsActive = false,
                            DayOfWeek = p.Src.Entry.DayOfWeek, PeriodId = p.Src.Entry.PeriodId, SubjectId = p.Src.Entry.SubjectId, EmployeeId = p.Teacher, NeedsTeacher = p.Needs,
                            RoomNo = null, StatusId = s.Value.Active, AssignedBy = userId, CreatedAt = now, UpdatedAt = now
                        }, ct);
                    await _repo.SaveChangesAsync(ct);
                }
                if (!preview) await tx.CommitAsync(ct);
                return Result(200, preview ? "Preview only. Nothing was created." : "Timetables created as drafts.", result);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: NameIndex }) { return Result<ApplyResultDto>(409, "A section already has a timetable with that name. Nothing was created.", null); }
            catch (Exception ex) { return Error<ApplyResultDto>(ex, "applying a timetable to other sections"); }
        }

        // ================================================================== the old single-timetable endpoints: they act on the ACTIVE timetable
        /// <summary>The section's ACTIVE timetable, created (as "Regular timetable") when the section has none yet. Used by the original assign / unassign endpoints.</summary>
        public async Task<(TbTimetable? Timetable, string? Error)> ActiveOrNewAsync(string schoolId, long userId, long academicYearId, long sectionId, CancellationToken ct)
        {
            var existing = await _repo.GetActiveTimetableAsync(schoolId, academicYearId, sectionId, ct);
            if (existing != null) return (existing, null);
            var s = await StatusesAsync(ct);
            if (s == null) return (null, "The system is not configured correctly. Please contact support.");
            await using var tx = await _repo.BeginTransactionAsync(ct);
            await _repo.LockSchoolAsync(schoolId, ct);
            existing = await _repo.GetActiveTimetableAsync(schoolId, academicYearId, sectionId, ct);
            if (existing != null) return (existing, null);
            var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
            if (section == null) return (null, "The selected section was not found for your school.");
            var name = DefaultName;
            for (var n = 2; await _repo.TimetableNameTakenAsync(schoolId, academicYearId, sectionId, name, null, ct); n++) name = $"{DefaultName} {n}";
            var created = await NewTimetableAsync(schoolId, academicYearId, section, name, userId, ct);
            var now = DateTime.UtcNow;
            created.State = TimetableStates.Active; created.IsActive = true; created.EverActive = true; created.FirstActivatedAt = now; created.ActivatedAt = now; created.ActivatedBy = userId;
            await _repo.AddTimetableAsync(created, ct);
            await _repo.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (created, null);
        }

        // ================================================================== helpers
        /// <summary>The original endpoints that carry no user pass 0: a user id of 0 is never written (the column is a foreign key).</summary>
        private static long? UserOrNull(long userId) => userId > 0 ? userId : null;

        private const string ArchivedMessage = "This timetable is archived. Restore it (or copy it into a new draft) before editing.";

        private sealed class Ctx
        {
            public Dictionary<long, TbPeriod> Slots = new();
            public Dictionary<string, TtTeacherRow> Teachers = new();
            public HashSet<long> Effective = new();
            public Dictionary<long, string> SubjectNames = new();
            public List<(long? ClassId, short? Day, long ScheduleId)> Follows = new();
            public long? DefaultSchedule;
            public Dictionary<long, string> ScheduleNames = new();
            public List<TeacherBookingRow> Bookings = new();
        }

        /// <summary>The timetables whose bookings do not count against this one: itself, and the section's ACTIVE one that activating this one would archive.</summary>
        private async Task<long[]> ReplacedByAsync(string schoolId, TbTimetable tt, CancellationToken ct)
        {
            var active = await _repo.GetActiveTimetableAsync(schoolId, tt.AcademicYearId, tt.SectionId, ct);
            return active == null || active.TimetableId == tt.TimetableId ? new[] { tt.TimetableId } : new[] { tt.TimetableId, active.TimetableId };
        }

        private async Task<Ctx> ContextAsync(string schoolId, TbTimetable tt, IReadOnlyCollection<long> periodIds, IReadOnlyCollection<string> employeeIds, int activeId, CancellationToken ct) => new()
        {
            Slots = await _repo.GetSlotsByIdsAsync(schoolId, periodIds, ct),
            Teachers = await _repo.GetTeachersByIdsAsync(schoolId, employeeIds, ct),
            Effective = await _repo.GetEffectiveSubjectIdsAsync(schoolId, tt.AcademicYearId, tt.SectionId, ct),
            SubjectNames = await _repo.GetSubjectNamesAsync(schoolId, ct),
            Follows = await _repo.GetYearScheduleRowsAsync(schoolId, tt.AcademicYearId, ct),
            DefaultSchedule = await _repo.GetDefaultScheduleIdAsync(schoolId, ct),
            ScheduleNames = await _repo.GetScheduleNamesAsync(schoolId, ct),
            Bookings = employeeIds.Count == 0 ? new() : await _repo.GetActiveBookingsAsync(schoolId, tt.AcademicYearId, employeeIds, await ReplacedByAsync(schoolId, tt, ct), activeId, ct)
        };

        /// <summary>Everything wrong (ERROR) or worth knowing (WARNING) about ONE slot. The clash is only reported when the bookings were loaded.</summary>
        private List<TimetableProblemDto> CheckSlot(Ctx c, SectionRef section, int day, long periodId, long subjectId, string? employeeId, int activeId, long? entryId)
        {
            var list = new List<TimetableProblemDto>();
            TimetableProblemDto P(string sev, string code, string msg) => new() { Severity = sev, Code = code, Message = msg, DayOfWeek = day, PeriodId = periodId };
            var dayName = ScheduleResolver.DayNames[day - 1];
            if (!c.Slots.TryGetValue(periodId, out var slot)) { list.Add(P("ERROR", "PERIOD_NOT_FOUND", "The selected period was not found for your school.")); return list; }
            var label = slot.SlotType == SlotTypes.Break ? $"the break \"{slot.SlotLabel}\"" : $"period {slot.PeriodNumber}";
            if (slot.SlotType != SlotTypes.Period) list.Add(P("ERROR", "BREAK", $"{(slot.SlotLabel ?? "This slot")} is a break, so no lesson can be placed in it."));
            else if (slot.StatusId != activeId) list.Add(P("ERROR", "PERIOD_INACTIVE", $"Period {slot.PeriodNumber} is inactive."));
            else
            {
                var follows = ScheduleResolver.Resolve(c.Follows, section.ClassId, day, c.DefaultSchedule);
                if (follows != null && follows != slot.ScheduleId)
                    list.Add(P("ERROR", "WRONG_SCHEDULE", $"Period {slot.PeriodNumber} belongs to the schedule \"{c.ScheduleNames.GetValueOrDefault(slot.ScheduleId)}\", but {section.ClassName} follows \"{c.ScheduleNames.GetValueOrDefault(follows.Value)}\" on {dayName}. Choose a period of \"{c.ScheduleNames.GetValueOrDefault(follows.Value)}\"."));
            }
            var subjectName = c.SubjectNames.GetValueOrDefault(subjectId, "This subject");
            if (!c.SubjectNames.ContainsKey(subjectId)) list.Add(P("ERROR", "SUBJECT_NOT_FOUND", "The selected subject was not found for your school."));
            else if (!c.Effective.Contains(subjectId)) list.Add(P("ERROR", "SUBJECT_NOT_TAUGHT", $"{subjectName} is not taught in {section.ClassName} - {section.SectionName} in this academic year. Add it under Academic Setup (subjects) first."));

            if (employeeId == null) { list.Add(P("WARNING", "NEEDS_TEACHER", $"{subjectName} on {dayName}, {label} has no teacher yet.")); return list; }
            if (!c.Teachers.TryGetValue(employeeId, out var teacher)) { list.Add(P("ERROR", "TEACHER_NOT_FOUND", "The selected teacher was not found for your school.")); return list; }
            if (teacher.StatusId != activeId || teacher.EmployeeType != EmployeeTypes.Teaching || teacher.EmploymentBlocksLogin)
                list.Add(P("ERROR", "TEACHER_NOT_ELIGIBLE", $"{TeacherName(teacher)} can't be given a timetable slot. They need an active record, employee type Teaching, and an employment status that still works at the school."));
            var clash = c.Bookings.FirstOrDefault(b => b.EmployeeId == employeeId && b.DayOfWeek == day && b.PeriodId == periodId);
            if (clash != null) list.Add(P("ERROR", "TEACHER_CLASH", ClashMessage(TeacherName(teacher), clash.ClassName, clash.SectionName, clash.SubjectName, day, clash.PeriodNumber, clash.TimetableName)));
            return list;
        }

        private async Task<TimetableValidationDto> CheckAllAsync(string schoolId, TbTimetable tt, int activeId, CancellationToken ct)
        {
            var rows = await _repo.GetTimetableRowsAsync(schoolId, tt.TimetableId, activeId, ct);
            var section = (await _repo.GetSectionAsync(schoolId, tt.SectionId, ct))!;
            var c = await ContextAsync(schoolId, tt, rows.Select(r => r.Entry.PeriodId).Distinct().ToList(), rows.Where(r => r.Entry.EmployeeId != null).Select(r => r.Entry.EmployeeId!).Distinct().ToList(), activeId, ct);
            var result = new TimetableValidationDto { TimetableId = tt.TimetableId, TimetableName = tt.TimetableName, SlotCount = rows.Count };
            if (section.SectionStatusId != activeId || section.ClassStatusId != activeId)
                result.Errors.Add(new TimetableProblemDto { Code = "SECTION_INACTIVE", Message = "The section (or its class) is inactive." });
            foreach (var r in rows.OrderBy(r => r.Entry.DayOfWeek).ThenBy(r => r.PeriodNumber))
                foreach (var p in CheckSlot(c, section, r.Entry.DayOfWeek, r.Entry.PeriodId, r.Entry.SubjectId, r.Entry.EmployeeId, activeId, r.Entry.TimetableEntryId))
                    (p.Severity == "ERROR" ? result.Errors : result.Warnings).Add(p);
            result.SlotsWithoutTeacher = rows.Count(r => r.Entry.EmployeeId == null);
            result.CanActivate = result.Errors.Count == 0;
            return result;
        }

        private static string Describe(List<TimetableProblemDto> errors)
        {
            var first = errors.Take(3).Select(e => e.Message);
            return $"{errors.Count} problem{(errors.Count == 1 ? "" : "s")}. {string.Join(" ", first)}{(errors.Count > 3 ? $" (and {errors.Count - 3} more: use Check to see them all)" : "")}";
        }

        private static string ClashMessage(string teacher, string className, string sectionName, string subject, int day, int periodNumber, string? timetable) =>
            $"{teacher} is already teaching {className} - {sectionName} ({subject}) on {ScheduleResolver.DayNames[day - 1]}, period {periodNumber}{(timetable == null ? "" : $" in the active timetable \"{timetable}\"")}.";

        private async Task<TbTimetable> NewTimetableAsync(string schoolId, long academicYearId, SectionRef section, string name, long userId, CancellationToken ct)
        {
            var follows = ScheduleResolver.Resolve(await _repo.GetYearScheduleRowsAsync(schoolId, academicYearId, ct), section.ClassId, 1, await _repo.GetDefaultScheduleIdAsync(schoolId, ct))
                          ?? throw new InvalidOperationException("The school has no bell schedule yet.");
            var now = DateTime.UtcNow;
            return new TbTimetable
            {
                SchoolId = schoolId, AcademicYearId = academicYearId, ClassId = section.ClassId, SectionId = section.SectionId, TimetableName = name, ScheduleId = follows,
                State = TimetableStates.Draft, IsActive = false, CreatedAt = now, UpdatedAt = now, CreatedBy = userId, UpdatedBy = userId
            };
        }

        private static TbTimetableEntry CopyEntry(TbTimetableEntry e, TbTimetable to, string? teacher, bool needs, long userId, int activeId, DateTime now) => new()
        {
            SchoolId = to.SchoolId, AcademicYearId = to.AcademicYearId, ClassId = to.ClassId, SectionId = to.SectionId, TimetableId = to.TimetableId, TtIsActive = to.IsActive,
            DayOfWeek = e.DayOfWeek, PeriodId = e.PeriodId, SubjectId = e.SubjectId, EmployeeId = teacher, NeedsTeacher = needs, RoomNo = e.RoomNo, StatusId = activeId, AssignedBy = userId, CreatedAt = now, UpdatedAt = now
        };

        private async Task<(TbTimetable? Timetable, (int Code, string Message)? Error)> EditableAsync(string schoolId, long timetableId, CancellationToken ct)
        {
            var tt = await _repo.GetTimetableAsync(schoolId, timetableId, ct);
            if (tt == null) return (null, (404, "Timetable not found."));
            if (await _repo.IsYearClosedAsync(schoolId, tt.AcademicYearId, ct)) return (null, (409, ClosedYear.Message));
            if (tt.State == TimetableStates.Archived) return (null, (409, ArchivedMessage));
            return (tt, null);
        }

        private async Task<(YearRef? Year, (int Code, string Message)? Error)> YearAsync(string schoolId, long? academicYearId, CancellationToken ct)
        {
            if (academicYearId != null)
            {
                var y = await _repo.GetYearAsync(schoolId, academicYearId.Value, ct);
                return y == null ? (null, (400, "The selected academic year was not found for your school.")) : (y, null);
            }
            var current = await _repo.GetYearContainingAsync(schoolId, SchoolClock.Today(), ct);
            if (current == null) return (null, (409, "No academic year covers today's date, so choose an academic year."));
            _logger.LogInformation("Timetable call without an academic year: defaulted to {YearId} of school {SchoolId}", current.AcademicYearId, schoolId);
            return (current, null);
        }

        private async Task<List<TimetableSummaryDto>> SummariesAsync(string schoolId, long academicYearId, long sectionId, int activeId, CancellationToken ct) =>
            (await _repo.ListTimetablesAsync(schoolId, academicYearId, sectionId, activeId, ct)).Select(ToDto).ToList();

        private async Task<ApiResponse<TimetableSummaryDto>> OneAsync(string schoolId, TbTimetable tt, int activeId, int code, string message, CancellationToken ct)
        {
            var one = (await SummariesAsync(schoolId, tt.AcademicYearId, tt.SectionId, activeId, ct)).FirstOrDefault(x => x.TimetableId == tt.TimetableId);
            return one == null ? Result<TimetableSummaryDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, one);
        }

        private async Task<ApiResponse<TimetableActivationDto>> ActivationAnswerAsync(string schoolId, TbTimetable tt, string? previous, int withoutTeacher, int activeId, int code, string message, CancellationToken ct)
        {
            var one = (await SummariesAsync(schoolId, tt.AcademicYearId, tt.SectionId, activeId, ct)).FirstOrDefault(x => x.TimetableId == tt.TimetableId);
            return one == null ? Result<TimetableActivationDto>(500, "Something went wrong. Please try again later.", null)
                : Result(code, message, new TimetableActivationDto { Timetable = one, PreviousTimetableName = previous, SlotsWithoutTeacher = withoutTeacher });
        }

        private static TimetableSummaryDto ToDto(TimetableListRow r) => new()
        {
            TimetableId = r.Timetable.TimetableId, AcademicYearId = r.Timetable.AcademicYearId, ClassId = r.Timetable.ClassId, SectionId = r.Timetable.SectionId, TimetableName = r.Timetable.TimetableName,
            ScheduleId = r.Timetable.ScheduleId, ScheduleName = r.ScheduleName, State = r.Timetable.State, IsActive = r.Timetable.IsActive, EverActive = r.Timetable.EverActive,
            SlotCount = r.Slots, NeedsTeacherCount = r.NeedsTeacher, CreatedAt = r.Timetable.CreatedAt, UpdatedAt = r.Timetable.UpdatedAt, ActivatedAt = r.Timetable.ActivatedAt,
            ActivatedByName = r.ActivatedByName, CreatedByName = r.CreatedByName, CanDelete = !r.Timetable.IsActive && !r.Timetable.EverActive
        };

        private async Task<ApiResponse<TimetableEntryDto>> RowAsync(string schoolId, long entryId, int activeId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetEntryRowAsync(schoolId, entryId, ct);
            return row == null ? Result<TimetableEntryDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToEntryDto(row));
        }

        public static TimetableEntryDto ToEntryDto(TtEntryRow r) => new()
        {
            TimetableEntryId = r.Entry.TimetableEntryId, AcademicYearId = r.Entry.AcademicYearId, ClassId = r.Entry.ClassId, ClassName = r.ClassName, SectionId = r.Entry.SectionId, SectionName = r.SectionName,
            DayOfWeek = r.Entry.DayOfWeek, PeriodId = r.Entry.PeriodId, PeriodNumber = r.PeriodNumber, SubjectId = r.Entry.SubjectId, SubjectName = r.SubjectName, SubjectCode = r.SubjectCode,
            EmployeeId = r.Entry.EmployeeId ?? "", EmployeeName = r.EmployeeName, RoomNo = r.Entry.RoomNo, NeedsTeacher = r.Entry.NeedsTeacher, TimetableId = r.Entry.TimetableId
        };

        private static string TeacherName(TtTeacherRow t) => string.Join(' ', new[] { t.FirstName, t.MiddleName, t.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

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
