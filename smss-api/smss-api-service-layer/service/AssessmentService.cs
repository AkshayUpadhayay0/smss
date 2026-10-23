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
    public class AssessmentService : IAssessmentService
    {
        private const decimal MaxAllowedMarks = 9999.99m;   // numeric(6,2)

        private readonly IAssessmentRepository _repo;
        private readonly ITimetableService _timetable;
        private readonly ILogger<AssessmentService> _logger;

        public AssessmentService(IAssessmentRepository repo, ITimetableService timetable, ILogger<AssessmentService> logger)
        {
            _repo = repo;
            _timetable = timetable;
            _logger = logger;
        }

        // ---------------------------------------------------------------- lookups
        public async Task<ApiResponse<List<AssessmentTypeDto>>> GetTypesAsync(CancellationToken ct)
        {
            try
            {
                var types = await _repo.GetTypesAsync(ct);
                return Result(200, "Successfully fetched", types.Select(t => new AssessmentTypeDto { AssessmentTypeId = t.AssessmentTypeId, TypeName = t.TypeName, TypeCode = t.TypeCode }).ToList());
            }
            catch (Exception ex) { return Error<List<AssessmentTypeDto>>(ex, "fetching assessment types"); }
        }

        public async Task<ApiResponse<List<AssessmentRosterStudentDto>>> GetRosterAsync(string? schoolId, AssessmentCaller caller, long academicYearId, long sectionId, long subjectId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<AssessmentRosterStudentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<AssessmentRosterStudentDto>>();
                string? ownerId = null;
                if (!caller.IsSchoolAdmin)
                {
                    var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                    ownerId = loginId;
                }
                var setup = await ResolveSetupAsync(schoolId, caller, ownerId, academicYearId, sectionId, null, subjectId, activeId.Value, ct);
                if (setup.Error != null) return Result<List<AssessmentRosterStudentDto>>(setup.Error.Value.Code, setup.Error.Value.Message, null);

                var roster = await _repo.GetRosterAsync(schoolId, academicYearId, sectionId, activeId.Value, ct);
                return Result(200, "Successfully fetched", roster.Select(r => new AssessmentRosterStudentDto { StudentId = r.StudentId, FullName = FullName(r.FirstName, r.MiddleName, r.LastName), RollNumber = r.RollNumber }).ToList());
            }
            catch (Exception ex) { return Error<List<AssessmentRosterStudentDto>>(ex, "fetching a test roster"); }
        }

        // ---------------------------------------------------------------- create
        public async Task<ApiResponse<AssessmentDetailDto>> CreateAsync(string? schoolId, AssessmentCaller caller, CreateAssessmentRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AssessmentDetailDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!string.IsNullOrWhiteSpace(req.SchoolId) && !string.Equals(req.SchoolId.Trim(), schoolId, StringComparison.OrdinalIgnoreCase))
                    return Result<AssessmentDetailDto>(403, "You can only record tests for your own school.", null);
                var bad = ValidateHeader(req.Title, req.TestDate!.Value, req.MaxMarks!.Value, out var title);
                if (bad != null) return Result<AssessmentDetailDto>(400, bad, null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<AssessmentDetailDto>();
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);

                // who owns it: a teacher always themselves; a School Admin names the teacher (or is one)
                string? ownerId;
                if (caller.IsSchoolAdmin) ownerId = string.IsNullOrWhiteSpace(req.EmployeeId) ? loginId : req.EmployeeId.Trim();
                else
                {
                    if (!string.IsNullOrWhiteSpace(req.EmployeeId) && !string.Equals(req.EmployeeId.Trim(), loginId, StringComparison.Ordinal))
                        return Result<AssessmentDetailDto>(403, "A teacher can only record tests as themselves.", null);
                    ownerId = loginId;
                }
                var owner = string.IsNullOrEmpty(ownerId) ? null : await _repo.GetEmployeeAsync(schoolId, ownerId, ct);
                if (owner == null)
                    return caller.IsSchoolAdmin ? Result<AssessmentDetailDto>(400, "Choose the teacher who owns this test.", null) : Result<AssessmentDetailDto>(403, "Your account is not linked to an employee record.", null);
                if (owner.StatusId != activeId || owner.EmployeeType != EmployeeTypes.Teaching || owner.EmploymentBlocksLogin)
                    return Result<AssessmentDetailDto>(400, "The owning teacher must be an active teaching employee.", null);

                var type = await _repo.GetTypeAsync(req.AssessmentTypeId!.Value, ct);
                if (type == null || !type.IsActive) return Result<AssessmentDetailDto>(400, "The selected assessment type was not found.", null);

                var setup = await ResolveSetupAsync(schoolId, caller, owner.EmployeeId, req.AcademicYearId!.Value, req.SectionId!.Value, req.ClassId, req.SubjectId!.Value, activeId.Value, ct);
                if (setup.Error != null) return Result<AssessmentDetailDto>(setup.Error.Value.Code, setup.Error.Value.Message, null);

                var roster = await _repo.GetRosterAsync(schoolId, setup.Year!.AcademicYearId, setup.Section!.SectionId, activeId.Value, ct);
                var markError = ValidateMarks(req.Marks, roster, req.MaxMarks.Value);
                if (markError != null) return Result<AssessmentDetailDto>(markError.Value.Code, markError.Value.Message, null);

                var now = DateTime.UtcNow;
                var entity = new TbClassAssessment
                {
                    SchoolId = schoolId, AcademicYearId = setup.Year.AcademicYearId, ClassId = setup.Section.ClassId, SectionId = setup.Section.SectionId, SubjectId = setup.Subject!.SubjectId,
                    AssessmentTypeId = type.AssessmentTypeId, EmployeeId = owner.EmployeeId, Title = title!, TestDate = req.TestDate.Value, MaxMarks = req.MaxMarks.Value,
                    StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now,
                };
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.AddAssessmentAsync(entity, ct);
                    await _repo.SaveChangesAsync(ct);   // the test first: its id is what the marks hang on
                    await _repo.AddMarksAsync(req.Marks!.Select(m => ToMark(entity.AssessmentId, m, now)), ct);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);          // header and every mark are one unit
                }
                return await DetailAsync(schoolId, entity.AssessmentId, 201, "Test recorded.", activeId.Value, ct);
            }
            catch (Exception ex) { return Error<AssessmentDetailDto>(ex, "recording a test"); }
        }

        // ---------------------------------------------------------------- update (title / date / max marks / the whole roster again)
        public async Task<ApiResponse<AssessmentDetailDto>> UpdateAsync(string? schoolId, AssessmentCaller caller, long id, UpdateAssessmentRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AssessmentDetailDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = ValidateHeader(req.Title, req.TestDate!.Value, req.MaxMarks!.Value, out var title);
                if (bad != null) return Result<AssessmentDetailDto>(400, bad, null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<AssessmentDetailDto>();

                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockAssessmentAsync(schoolId, id, ct);   // two saves of one test queue up
                    var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, activeId.Value, ct);
                    if (entity == null) return Result<AssessmentDetailDto>(failure!.Value.Code, failure.Value.Message, null);

                    var roster = await _repo.GetRosterAsync(schoolId, entity.AcademicYearId, entity.SectionId, activeId.Value, ct);
                    var markError = ValidateMarks(req.Marks, roster, req.MaxMarks.Value);
                    if (markError != null) return Result<AssessmentDetailDto>(markError.Value.Code, markError.Value.Message, null);

                    var now = DateTime.UtcNow;
                    var existing = (await _repo.GetMarksForUpdateAsync(id, ct)).ToDictionary(m => m.StudentId, StringComparer.Ordinal);
                    var submitted = req.Marks!.Select(m => m.StudentId!.Trim()).ToHashSet(StringComparer.Ordinal);
                    // a student who has since left keeps the result they had, but it must still fit the new maximum
                    var tooHigh = existing.Values.Where(m => !submitted.Contains(m.StudentId) && m.ObtainedMarks > req.MaxMarks.Value).Select(m => m.StudentId).ToList();
                    if (tooHigh.Count > 0) return Result<AssessmentDetailDto>(400, $"The new maximum ({req.MaxMarks.Value}) is below the stored mark of: {Names(tooHigh)}.", null);

                    var added = new List<TbStudentAssessmentMark>();
                    foreach (var input in req.Marks!)
                    {
                        var sid = input.StudentId!.Trim();
                        if (existing.TryGetValue(sid, out var row))
                        {
                            row.IsAbsent = input.IsAbsent; row.ObtainedMarks = input.IsAbsent ? null : input.ObtainedMarks; row.UpdatedAt = now;
                        }
                        else added.Add(ToMark(id, input, now));   // joined the section after the test was recorded
                    }
                    if (added.Count > 0) await _repo.AddMarksAsync(added, ct);
                    entity.Title = title!; entity.TestDate = req.TestDate.Value; entity.MaxMarks = req.MaxMarks.Value; entity.UpdatedAt = now;
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return await DetailAsync(schoolId, id, 200, "Test updated.", activeId.Value, ct);
            }
            catch (Exception ex) { return Error<AssessmentDetailDto>(ex, "updating a test"); }
        }

        public async Task<ApiResponse<AssessmentDto>> DeleteAsync(string? schoolId, AssessmentCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AssessmentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<AssessmentDto>();
                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, activeId.Value, ct);
                if (entity == null) return Result<AssessmentDto>(failure!.Value.Code, failure.Value.Message, null);
                entity.StatusId = inactiveId.Value; entity.UpdatedAt = DateTime.UtcNow;   // hidden, never removed; the marks stay too
                await _repo.SaveChangesAsync(ct);
                var row = await _repo.GetRowAsync(schoolId, id, ct);
                return row == null ? Result<AssessmentDto>(500, "Something went wrong. Please try again later.", null) : Result(200, "Test deleted.", ToDto(row, new AssessmentDto(), "Inactive"));
            }
            catch (Exception ex) { return Error<AssessmentDto>(ex, "deleting a test"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<AssessmentDetailDto>> GetAsync(string? schoolId, AssessmentCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AssessmentDetailDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<AssessmentDetailDto>();
                var row = await _repo.GetRowAsync(schoolId, id, ct);
                if (row == null || row.StatusId != activeId) return Result<AssessmentDetailDto>(404, "Test not found.", null);   // a deleted one is hidden from everyone
                if (!caller.IsSchoolAdmin)
                {
                    var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                    if (!caller.IsTeacher || !string.Equals(loginId, row.EmployeeId, StringComparison.Ordinal))
                        return Result<AssessmentDetailDto>(403, "Only the teacher who recorded this test, or a School Admin, can see its marks.", null);
                }
                return await DetailAsync(schoolId, id, 200, "Successfully fetched", activeId.Value, ct);
            }
            catch (Exception ex) { return Error<AssessmentDetailDto>(ex, "fetching a test"); }
        }

        public async Task<ApiResponse<List<AssessmentDto>>> GetMyAsync(string? schoolId, AssessmentCaller caller, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<AssessmentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<AssessmentDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<AssessmentDto>>();
                // the caller's own employee id is their login id; someone without one (the school-id login) owns nothing
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                if (string.IsNullOrEmpty(loginId)) return Result(200, "Successfully fetched", new List<AssessmentDto>());
                var rows = await _repo.ListAsync(schoolId, new AssessmentFilter(academicYearId, sectionId, subjectId, loginId, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", Newest(rows));
            }
            catch (Exception ex) { return Error<List<AssessmentDto>>(ex, "listing my tests"); }
        }

        public async Task<ApiResponse<List<AssessmentDto>>> ListAsync(string? schoolId, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<AssessmentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<AssessmentDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<AssessmentDto>>();
                var rows = await _repo.ListAsync(schoolId, new AssessmentFilter(academicYearId, sectionId, subjectId, null, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", Newest(rows));
            }
            catch (Exception ex) { return Error<List<AssessmentDto>>(ex, "listing tests"); }
        }

        public async Task<ApiResponse<List<AssessmentDto>>> GetSectionAsync(string? schoolId, long sectionId, long? academicYearId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<AssessmentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<AssessmentDto>>(400, rangeError, null);
                if (await _repo.GetSectionAsync(schoolId, sectionId, ct) == null) return Result<List<AssessmentDto>>(404, "Section not found.", null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<AssessmentDto>>();
                var rows = await _repo.ListAsync(schoolId, new AssessmentFilter(academicYearId, sectionId, null, null, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", Newest(rows));
            }
            catch (Exception ex) { return Error<List<AssessmentDto>>(ex, "listing a section's tests"); }
        }

        public async Task<ApiResponse<List<StudentAssessmentResultDto>>> GetStudentMyAsync(string? schoolId, AssessmentCaller caller, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<StudentAssessmentResultDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<StudentAssessmentResultDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<StudentAssessmentResultDto>>();
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                var year = await _repo.GetYearContainingAsync(schoolId, SchoolClock.Today(), ct);
                var sectionId = string.IsNullOrEmpty(loginId) || year == null ? null : await _repo.GetStudentSectionAsync(schoolId, loginId, year.AcademicYearId, activeId.Value, ct);
                if (sectionId == null) return Result(200, "Successfully fetched", new List<StudentAssessmentResultDto>());   // not enrolled in the current year: nothing to show
                var rows = await _repo.GetStudentResultsAsync(schoolId, loginId!, year!.AcademicYearId, sectionId.Value, activeId.Value, from, to, ct);
                return Result(200, "Successfully fetched", rows.Select(r => new StudentAssessmentResultDto
                {
                    AssessmentId = r.AssessmentId, Title = r.Title, AssessmentTypeName = r.TypeName, SubjectName = r.SubjectName, TestDate = r.TestDate, MaxMarks = r.MaxMarks,
                    ObtainedMarks = r.IsAbsent ? null : r.ObtainedMarks, IsAbsent = r.IsAbsent,
                }).ToList());
            }
            catch (Exception ex) { return Error<List<StudentAssessmentResultDto>>(ex, "listing a student's results"); }
        }

        // ---------------------------------------------------------------- shared rules
        private sealed record Setup(YearRef? Year, SectionRef? Section, HomeworkSubjectRow? Subject, (int Code, string Message)? Error);

        /// <summary>
        /// The year, section and subject of a test, and whether the owner may record it: the section must be active, the subject active AND mapped to the section's class
        /// (for a School Admin too: the same Class-Subject Mapping rule as Homework and the Timetable), and a Teacher must also teach that subject to that section.
        /// </summary>
        private async Task<Setup> ResolveSetupAsync(string schoolId, AssessmentCaller caller, string? ownerId, long academicYearId, long sectionId, long? classId, long subjectId, int activeId, CancellationToken ct)
        {
            Setup Fail(int code, string message) => new(null, null, null, (code, message));
            var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
            if (section == null) return Fail(400, "The selected section was not found for your school.");
            if (classId != null && classId != section.ClassId) return Fail(400, "The selected section does not belong to the selected class.");
            if (section.SectionStatusId != activeId || section.ClassStatusId != activeId) return Fail(400, "The selected section (or its class) is inactive.");
            var subject = await _repo.GetSubjectAsync(schoolId, subjectId, section.ClassId, activeId, ct);
            if (subject == null || subject.StatusId != activeId) return Fail(400, "The selected subject was not found for your school.");
            if (!subject.MappedToClass)
                return Fail(400, $"{subject.SubjectName} is not taught in {section.ClassName}. Add it to the class under Class-Subject Mapping first.");
            var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
            if (year == null) return Fail(400, "The selected academic year was not found for your school.");
            if (!caller.IsSchoolAdmin && (string.IsNullOrEmpty(ownerId) || !await _timetable.IsTeachingSectionSubjectAsync(schoolId, ownerId, section.SectionId, subject.SubjectId, year.AcademicYearId, ct)))
                return Fail(403, $"You don't currently teach {subject.SubjectName} to {section.ClassName} - {section.SectionName} in {year.YearName}, so you can't record a test for it. Ask a School Admin to add it to your timetable.");
            return new Setup(year, section, subject, null);
        }

        private static string? ValidateHeader(string? rawTitle, DateOnly testDate, decimal maxMarks, out string? title)
        {
            title = rawTitle?.Trim();
            if (string.IsNullOrEmpty(title)) return "Title is required.";
            if (title.Length > 200) return "The title can be at most 200 characters.";
            if (testDate > SchoolClock.Today()) return "A test can only be recorded on or after the day it was held; the test date can't be in the future.";
            if (maxMarks <= 0) return "Maximum marks must be greater than zero.";
            if (maxMarks > MaxAllowedMarks) return $"Maximum marks can be at most {MaxAllowedMarks}.";
            if (decimal.Round(maxMarks, 2) != maxMarks) return "Maximum marks can have at most two decimal places.";
            return null;
        }

        /// <summary>The whole-roster rule (the Attendance submit rule): every active student exactly once, nobody unknown, each a valid mark or an absence. Nothing is written on any failure.</summary>
        private static (int Code, string Message)? ValidateMarks(List<AssessmentMarkInput>? marks, List<RosterStudentRow> roster, decimal max)
        {
            if (roster.Count == 0) return (409, "This section has no active students, so there is nothing to record.");
            if (marks == null || marks.Count == 0) return (400, "The marks must cover every student of the section.");
            var ids = marks.Select(m => (m.StudentId ?? string.Empty).Trim()).ToList();
            var dupes = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) return (400, $"Each student can appear only once. Repeated: {Names(dupes)}.");
            var rosterIds = roster.Select(r => r.StudentId).ToHashSet(StringComparer.Ordinal);
            var strangers = ids.Where(i => !rosterIds.Contains(i)).ToList();
            if (strangers.Count > 0) return (400, $"These students are not on this section's active roster (unknown or since left): {Names(strangers)}.");
            var missing = roster.Select(r => r.StudentId).Where(i => !ids.Contains(i)).ToList();
            if (missing.Count > 0) return (400, $"A mark or an absence is missing for {missing.Count} student(s): {Names(missing)}. Every active student of the section must be included.");

            foreach (var m in marks)
            {
                var sid = m.StudentId!.Trim();
                if (m.IsAbsent)
                {
                    if (m.ObtainedMarks != null) return (400, $"{sid} is marked absent, so no marks can be given for them.");
                    continue;
                }
                if (m.ObtainedMarks == null) return (400, $"Marks are missing for {sid}: enter the marks, or mark the student absent.");
                if (m.ObtainedMarks < 0 || m.ObtainedMarks > max) return (400, $"The marks for {sid} must be between 0 and {max} (the maximum marks).");
                if (decimal.Round(m.ObtainedMarks.Value, 2) != m.ObtainedMarks) return (400, $"The marks for {sid} can have at most two decimal places.");
            }
            return null;
        }

        private static TbStudentAssessmentMark ToMark(long assessmentId, AssessmentMarkInput m, DateTime now) => new()
        {
            AssessmentId = assessmentId, StudentId = m.StudentId!.Trim(), IsAbsent = m.IsAbsent, ObtainedMarks = m.IsAbsent ? null : m.ObtainedMarks, CreatedAt = now, UpdatedAt = now,
        };

        /// <summary>The test to change: Active, in this school, and the caller is its owning teacher or a School Admin (a different teacher, even an eligible one, is refused).</summary>
        private async Task<(TbClassAssessment? Entity, (int Code, string Message)? Failure)> LoadForChangeAsync(string schoolId, AssessmentCaller caller, long id, int activeId, CancellationToken ct)
        {
            var entity = await _repo.GetActiveForUpdateAsync(schoolId, id, activeId, ct);
            if (entity == null) return (null, (404, "Test not found."));
            if (!caller.IsSchoolAdmin)
            {
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                if (!caller.IsTeacher || !string.Equals(loginId, entity.EmployeeId, StringComparison.Ordinal))
                    return (null, (403, "Only the teacher who recorded this test, or a School Admin, can change it."));
            }
            return (entity, null);
        }

        // ---------------------------------------------------------------- mapping
        private async Task<ApiResponse<AssessmentDetailDto>> DetailAsync(string schoolId, long id, int code, string message, int activeId, CancellationToken ct)
        {
            var row = await _repo.GetRowAsync(schoolId, id, ct);
            if (row == null) return Result<AssessmentDetailDto>(500, "Something went wrong. Please try again later.", null);
            var marks = await _repo.GetMarkRowsAsync(schoolId, id, row.AcademicYearId, ct);
            var roster = (await _repo.GetRosterAsync(schoolId, row.AcademicYearId, row.SectionId, activeId, ct)).Select(r => r.StudentId).ToHashSet(StringComparer.Ordinal);
            var dto = ToDto(row, new AssessmentDetailDto(), row.StatusId == activeId ? "Active" : "Inactive");
            dto.Marks = marks.Select(m => new AssessmentMarkDto
            {
                StudentId = m.StudentId, FullName = FullName(m.FirstName, m.MiddleName, m.LastName), RollNumber = m.RollNumber, ObtainedMarks = m.ObtainedMarks, IsAbsent = m.IsAbsent, OnRoster = roster.Contains(m.StudentId),
            }).ToList();
            return Result(code, message, dto);
        }

        private static List<AssessmentDto> Newest(List<AssessmentRow> rows) =>
            rows.OrderByDescending(r => r.TestDate).ThenByDescending(r => r.AssessmentId).Select(r => ToDto(r, new AssessmentDto(), "Active")).ToList();

        private static T ToDto<T>(AssessmentRow r, T dto, string statusName) where T : AssessmentDto
        {
            dto.AssessmentId = r.AssessmentId; dto.AcademicYearId = r.AcademicYearId; dto.AcademicYearName = r.AcademicYearName; dto.ClassId = r.ClassId; dto.ClassName = r.ClassName;
            dto.SectionId = r.SectionId; dto.SectionName = r.SectionName; dto.SubjectId = r.SubjectId; dto.SubjectName = r.SubjectName; dto.AssessmentTypeId = r.AssessmentTypeId; dto.AssessmentTypeName = r.TypeName;
            dto.EmployeeId = r.EmployeeId; dto.TeacherName = FullName(r.FirstName, r.MiddleName, r.LastName); dto.Title = r.Title; dto.TestDate = r.TestDate; dto.MaxMarks = r.MaxMarks;
            dto.StudentCount = r.StudentCount; dto.AbsentCount = r.AbsentCount; dto.StatusName = statusName;
            return dto;
        }

        private static string? RangeError(DateOnly? from, DateOnly? to) => from != null && to != null && to < from ? "The 'to' date can't be before the 'from' date." : null;

        /// <summary>Student ids for a message, capped so a large mistake can't produce a huge error.</summary>
        private static string Names(List<string> ids) => ids.Count <= 20 ? string.Join(", ", ids) : string.Join(", ", ids.Take(20)) + $" and {ids.Count - 20} more";

        private static string FullName(string first, string? middle, string? last) => string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: a general status is missing in lut_status");
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
