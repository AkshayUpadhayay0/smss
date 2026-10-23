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
    public class AttendanceService : IAttendanceService
    {
        private const string OneMarkPerDay = "uq_tb_att_student_date";
        private const int MaxRangeDays = 366;
        private const string PresentCode = "PRESENT";

        private readonly IAttendanceRepository _repo;
        private readonly IClassTeacherService _classTeachers;
        private readonly ILogger<AttendanceService> _logger;

        public AttendanceService(IAttendanceRepository repo, IClassTeacherService classTeachers, ILogger<AttendanceService> logger)
        {
            _repo = repo;
            _classTeachers = classTeachers;
            _logger = logger;
        }

        // ---------------------------------------------------------------- submit (the class teacher, today)
        public async Task<ApiResponse<SubmitAttendanceResultDto>> SubmitAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, SubmitAttendanceRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SubmitAttendanceResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var date = req.Date!.Value;
                var today = SchoolClock.Today();
                // never trust a client date here: today's marks only. Anything else is a correction.
                if (date != today)
                    return Result<SubmitAttendanceResultDto>(400, $"Attendance can only be submitted for today ({today:yyyy-MM-dd}). Past days are changed by a School Admin through a correction.", null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<SubmitAttendanceResultDto>();

                var section = await _repo.GetSectionAsync(schoolId, req.SectionId!.Value, ct);
                if (section == null) return Result<SubmitAttendanceResultDto>(400, "The selected section was not found for your school.", null);
                if (section.SectionStatusId != activeId || section.ClassStatusId != activeId) return Result<SubmitAttendanceResultDto>(400, "The selected section (or its class) is inactive.", null);

                var year = await _repo.GetYearContainingAsync(schoolId, date, ct);
                if (year == null) return Result<SubmitAttendanceResultDto>(409, "No academic year covers today's date, so attendance can't be recorded.", null);

                if (!await MayMarkAsync(schoolId, callerUserId, callerIsSchoolAdmin, section.SectionId, year.AcademicYearId, ct))
                    return Result<SubmitAttendanceResultDto>(403, "Only this section's class teacher (or a School Admin) can submit its attendance.", null);

                var statuses = await _repo.GetAttendanceStatusesAsync(ct);
                var records = req.Records!;
                var dupes = records.GroupBy(r => r.StudentId!.Trim()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dupes.Count > 0) return Result<SubmitAttendanceResultDto>(400, $"Each student can appear only once. Repeated: {Names(dupes)}.", null);
                var badStatus = records.Where(r => statuses.All(s => s.Id != r.AttendanceStatusId)).Select(r => r.StudentId!.Trim()).ToList();
                if (badStatus.Count > 0) return Result<SubmitAttendanceResultDto>(400, $"An unknown attendance status was given for: {Names(badStatus)}.", null);

                var roster = await _repo.GetRosterAsync(schoolId, year.AcademicYearId, section.SectionId, activeId.Value, ct);
                if (roster.Count == 0) return Result<SubmitAttendanceResultDto>(409, "This section has no active students, so there is nothing to mark.", null);
                var rosterIds = roster.Select(r => r.StudentId).ToHashSet(StringComparer.Ordinal);
                var givenIds = records.Select(r => r.StudentId!.Trim()).ToList();

                // the submission must cover EVERY active student: an incomplete one is a mistake to surface, not to paper over with a default
                var missing = roster.Select(r => r.StudentId).Where(id => !givenIds.Contains(id)).ToList();
                if (missing.Count > 0) return Result<SubmitAttendanceResultDto>(400, $"Attendance is missing for {missing.Count} student(s): {Names(missing)}. Every active student in the section must be marked.", null);
                var strangers = givenIds.Where(id => !rosterIds.Contains(id)).ToList();
                if (strangers.Count > 0) return Result<SubmitAttendanceResultDto>(400, $"These students are not on this section's active roster: {Names(strangers)}.", null);

                var statusById = statuses.ToDictionary(s => s.Id);
                int created = 0, updated = 0;
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockSectionDayAsync(schoolId, section.SectionId, date, ct);   // two submissions for one section-day queue up
                    var existing = (await _repo.GetExistingForUpdateAsync(schoolId, date, rosterIds, ct)).ToDictionary(a => a.StudentId, StringComparer.Ordinal);

                    var elsewhere = existing.Values.Where(a => a.SectionId != section.SectionId).Select(a => a.StudentId).ToList();
                    if (elsewhere.Count > 0)
                        return Result<SubmitAttendanceResultDto>(409, $"Today's attendance for {Names(elsewhere)} was already recorded in another section.", null);
                    // an admin correction on today's marks must not be silently overwritten by a resubmission
                    if (existing.Values.Any(a => a.CorrectedAt != null))
                        return Result<SubmitAttendanceResultDto>(409, "A School Admin has corrected today's attendance for this section, so it can't be resubmitted. Ask the School Admin to make further changes.", null);

                    var now = DateTime.UtcNow;
                    var inserts = new List<TbStudentAttendance>();
                    foreach (var r in records)
                    {
                        var id = r.StudentId!.Trim();
                        if (existing.TryGetValue(id, out var row))
                        {
                            row.AttendanceStatusId = r.AttendanceStatusId!.Value;   // same-day edit = overwrite
                            row.MarkedBy = callerUserId;
                            row.MarkedAt = now;
                            row.UpdatedAt = now;
                            updated++;
                        }
                        else
                        {
                            inserts.Add(new TbStudentAttendance
                            {
                                SchoolId = schoolId, AcademicYearId = year.AcademicYearId, SectionId = section.SectionId, ClassId = section.ClassId, StudentId = id,
                                AttendanceDate = date, AttendanceStatusId = r.AttendanceStatusId!.Value, MarkedBy = callerUserId, MarkedAt = now, CreatedAt = now, UpdatedAt = now
                            });
                            created++;
                        }
                    }
                    await _repo.AddRangeAsync(inserts, ct);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }

                int Count(string code) => records.Count(r => statusById[r.AttendanceStatusId!.Value].Code == code);
                return Result(200, created > 0 && updated == 0 ? "Attendance saved." : "Attendance updated.", new SubmitAttendanceResultDto
                {
                    SectionId = section.SectionId, AcademicYearId = year.AcademicYearId, Date = date, Total = records.Count,
                    Present = Count(PresentCode), Absent = Count("ABSENT"), Created = created, Updated = updated
                });
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == OneMarkPerDay)
            {
                // the database's one-mark-per-student-per-day rule caught a race the lock did not
                return Result<SubmitAttendanceResultDto>(409, "Attendance for one of these students was just recorded by someone else. Please reload and try again.", null);
            }
            catch (Exception ex) { return Error<SubmitAttendanceResultDto>(ex, "submitting attendance"); }
        }

        // ---------------------------------------------------------------- correct (School Admin, any past day)
        public async Task<ApiResponse<AttendanceCorrectionDto>> CorrectAsync(string? schoolId, long adminUserId, CorrectAttendanceRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AttendanceCorrectionDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var reason = req.Reason?.Trim();
                if (string.IsNullOrEmpty(reason)) return Result<AttendanceCorrectionDto>(400, "A reason is required to correct attendance.", null);
                var date = req.Date!.Value;
                if (date > SchoolClock.Today()) return Result<AttendanceCorrectionDto>(400, "Attendance can't be corrected for a future date.", null);

                var statuses = await _repo.GetAttendanceStatusesAsync(ct);
                var newStatus = statuses.FirstOrDefault(s => s.Id == req.AttendanceStatusId);
                if (newStatus == null) return Result<AttendanceCorrectionDto>(400, "The selected attendance status was not found.", null);

                var studentId = req.StudentId!.Trim();
                if (await _repo.GetStudentAsync(schoolId, studentId, ct) == null) return Result<AttendanceCorrectionDto>(404, "Student not found.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                var mark = await _repo.GetMarkForUpdateAsync(schoolId, studentId, date, ct);

                // Which section-day is this? An existing mark knows. A day with NO mark belongs to the student's enrollment that year.
                YearRef? year = null;
                SectionRef? section = null;
                long sectionId;
                if (mark != null) sectionId = mark.SectionId;
                else
                {
                    year = await _repo.GetYearContainingAsync(schoolId, date, ct);
                    if (year == null) return Result<AttendanceCorrectionDto>(400, "No academic year covers that date, so attendance can't be added for it.", null);
                    var enrollment = await _repo.GetStudentEnrollmentAsync(schoolId, studentId, year.AcademicYearId, ct);
                    if (enrollment == null) return Result<AttendanceCorrectionDto>(400, $"This student was not enrolled in {year.YearName}, so attendance can't be added for that date.", null);
                    if (date < enrollment.EnrollmentDate)
                        return Result<AttendanceCorrectionDto>(400, $"This student was enrolled from {enrollment.EnrollmentDate:yyyy-MM-dd}, so attendance can't be added for an earlier date.", null);
                    section = await _repo.GetSectionAsync(schoolId, enrollment.SectionId, ct);
                    if (section == null) return Result<AttendanceCorrectionDto>(409, "This student's enrollment points to a section that no longer exists. Please contact support.", null);
                    sectionId = section.SectionId;
                }

                await _repo.LockSectionDayAsync(schoolId, sectionId, date, ct);   // never in the middle of a teacher's submission of that section-day
                mark = await _repo.GetMarkForUpdateAsync(schoolId, studentId, date, ct);   // (re-read under the lock: a teacher may have just marked it)

                var now = DateTime.UtcNow;
                string? previous = null;
                var created = mark == null;
                if (mark == null)
                {
                    // ADD: a correction with nothing to overwrite. It records exactly what a correction records.
                    if (year == null || section == null) return Result<AttendanceCorrectionDto>(409, "Attendance for this student was just removed. Please try again.", null);
                    mark = new TbStudentAttendance
                    {
                        SchoolId = schoolId, AcademicYearId = year.AcademicYearId, SectionId = section.SectionId, ClassId = section.ClassId, StudentId = studentId, AttendanceDate = date,
                        AttendanceStatusId = newStatus.Id, MarkedBy = adminUserId, MarkedAt = now, CreatedAt = now, UpdatedAt = now,
                        CorrectedBy = adminUserId, CorrectedAt = now, CorrectionReason = reason
                    };
                    await _repo.AddRangeAsync(new[] { mark }, ct);
                }
                else
                {
                    if (mark.AttendanceStatusId == newStatus.Id) return Result<AttendanceCorrectionDto>(400, $"This student is already marked {newStatus.Name} on that date.", null);
                    previous = statuses.FirstOrDefault(s => s.Id == mark.AttendanceStatusId)?.Name;
                    mark.AttendanceStatusId = newStatus.Id;
                    mark.CorrectedBy = adminUserId;
                    mark.CorrectedAt = now;
                    mark.CorrectionReason = reason;
                    mark.UpdatedAt = now;
                }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var (_, adminName) = await _repo.GetUserAsync(schoolId, adminUserId, ct);
                return Result(200, created ? "Attendance added." : "Attendance corrected.", new AttendanceCorrectionDto
                {
                    StudentId = studentId, Date = date, SectionId = mark.SectionId, StatusCode = newStatus.Code, StatusName = newStatus.Name, PreviousStatusName = previous, Created = created,
                    CorrectedByName = adminName, CorrectedAt = now, CorrectionReason = reason
                });
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == OneMarkPerDay)
            {
                return Result<AttendanceCorrectionDto>(409, "Attendance for this student was just recorded by someone else. Please try again.", null);
            }
            catch (Exception ex) { return Error<AttendanceCorrectionDto>(ex, "correcting attendance"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<SectionAttendanceDto>> GetSectionAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, DateOnly? date, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SectionAttendanceDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var day = date ?? SchoolClock.Today();
                if (day > SchoolClock.Today()) return Result<SectionAttendanceDto>(400, "There is no attendance for a future date.", null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<SectionAttendanceDto>();

                var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
                if (section == null) return Result<SectionAttendanceDto>(404, "Section not found.", null);
                var year = await _repo.GetYearContainingAsync(schoolId, day, ct);
                if (year == null) return Result<SectionAttendanceDto>(400, "No academic year covers that date.", null);

                var mayMark = await MayMarkAsync(schoolId, callerUserId, callerIsSchoolAdmin, sectionId, year.AcademicYearId, ct);
                if (!mayMark) return Result<SectionAttendanceDto>(403, "Only this section's class teacher (or a School Admin) can view its attendance.", null);

                var roster = await _repo.GetRosterAsync(schoolId, year.AcademicYearId, sectionId, activeId.Value, ct);
                var marks = (await _repo.GetMarksAsync(schoolId, sectionId, day, ct)).ToDictionary(m => m.Mark.StudentId, StringComparer.Ordinal);

                SectionAttendanceStudentDto Row(string id, string name, string? roll, bool onRoster)
                {
                    marks.TryGetValue(id, out var m);
                    return new SectionAttendanceStudentDto
                    {
                        StudentId = id, FullName = name, RollNumber = roll, OnRoster = onRoster,
                        AttendanceStatusId = m?.Mark.AttendanceStatusId, StatusCode = m?.StatusCode, StatusName = m?.StatusName,
                        MarkedByName = m?.MarkedByName, MarkedAt = m?.Mark.MarkedAt,
                        IsCorrected = m?.Mark.CorrectedAt != null, CorrectedAt = m?.Mark.CorrectedAt, CorrectedByName = m?.CorrectedByName, CorrectionReason = m?.Mark.CorrectionReason
                    };
                }
                var students = roster.Select(r => Row(r.StudentId, FullName(r.FirstName, r.MiddleName, r.LastName), r.RollNumber, true)).ToList();
                // a student who has a mark that day but has since left the roster still shows, so history is never hidden
                foreach (var id in marks.Keys.Where(k => students.All(s => s.StudentId != k)))
                {
                    var st = await _repo.GetStudentAsync(schoolId, id, ct);
                    students.Add(Row(id, st == null ? id : FullName(st.FirstName, st.MiddleName, st.LastName), null, false));
                }

                return Result(200, "Successfully fetched", new SectionAttendanceDto
                {
                    SectionId = section.SectionId, ClassId = section.ClassId, ClassName = section.ClassName, SectionName = section.SectionName,
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, Date = day, IsToday = day == SchoolClock.Today(),
                    RosterCount = roster.Count, MarkedCount = marks.Count, IsMarked = marks.Count > 0, CanSubmit = day == SchoolClock.Today() && roster.Count > 0,
                    Statuses = (await _repo.GetAttendanceStatusesAsync(ct)).Select(s => new AttendanceStatusDto { AttendanceStatusId = s.Id, StatusCode = s.Code, StatusName = s.Name }).ToList(),
                    Students = students
                });
            }
            catch (Exception ex) { return Error<SectionAttendanceDto>(ex, "fetching a section's attendance"); }
        }

        public async Task<ApiResponse<StudentAttendanceHistoryDto>> GetStudentAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, string studentId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentAttendanceHistoryDto>(400, TenantMessages.NoSchool, null);
            if (to < from) return Result<StudentAttendanceHistoryDto>(400, "'to' cannot be before 'from'.", null);
            if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays) return Result<StudentAttendanceHistoryDto>(400, $"The range can be at most {MaxRangeDays} days.", null);
            try
            {
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);   // 404 for another school's student too
                if (student == null) return Result<StudentAttendanceHistoryDto>(404, "Student not found.", null);

                if (!callerIsSchoolAdmin)
                {
                    // a teacher sees the students of the section they are class teacher of (this year)
                    var year = await _repo.GetYearContainingAsync(schoolId, SchoolClock.Today(), ct);
                    var sectionId = year == null ? null : await _repo.GetStudentSectionAsync(schoolId, studentId, year.AcademicYearId, ct);
                    if (year == null || sectionId == null || !await MayMarkAsync(schoolId, callerUserId, false, sectionId.Value, year.AcademicYearId, ct))
                        return Result<StudentAttendanceHistoryDto>(403, "You can only view attendance of students in your own section.", null);
                }

                var days = (await _repo.GetStudentDaysAsync(schoolId, studentId, from, to, ct)).Select(d => new StudentAttendanceDayDto
                {
                    Date = d.Date, StatusCode = d.StatusCode, StatusName = d.StatusName, SectionId = d.SectionId, ClassName = d.ClassName, SectionName = d.SectionName,
                    IsCorrected = d.CorrectedAt != null, CorrectionReason = d.CorrectionReason
                }).ToList();
                var byStatus = days.GroupBy(d => d.StatusCode).ToDictionary(g => g.Key, g => g.Count());
                return Result(200, "Successfully fetched", new StudentAttendanceHistoryDto
                {
                    StudentId = studentId, FullName = FullName(student.FirstName, student.MiddleName, student.LastName), From = from, To = to, Days = days,
                    Summary = new AttendanceSummaryDto
                    {
                        MarkedDays = days.Count, ByStatus = byStatus,
                        PresentPercentage = days.Count == 0 ? null : Math.Round(byStatus.GetValueOrDefault(PresentCode) * 100m / days.Count, 1)
                    }
                });
            }
            catch (Exception ex) { return Error<StudentAttendanceHistoryDto>(ex, "fetching a student's attendance"); }
        }

        public async Task<ApiResponse<MissingAttendanceDto>> GetMissingAsync(string? schoolId, DateOnly? date, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MissingAttendanceDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var day = date ?? SchoolClock.Today();
                if (day > SchoolClock.Today()) return Result<MissingAttendanceDto>(400, "There is no attendance for a future date.", null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<MissingAttendanceDto>();

                var year = academicYearId != null ? await _repo.GetYearAsync(schoolId, academicYearId.Value, ct) : await _repo.GetYearContainingAsync(schoolId, day, ct);
                if (year == null) return Result<MissingAttendanceDto>(400, academicYearId != null ? "The selected academic year was not found for your school." : "No academic year covers that date.", null);

                var rows = await _repo.GetUnmarkedSectionsAsync(schoolId, year.AcademicYearId, day, activeId.Value, ct);
                return Result(200, "Successfully fetched", new MissingAttendanceDto
                {
                    Date = day, AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName,
                    Sections = rows.Select(r => new UnmarkedSectionDto
                    {
                        ClassId = r.ClassId, ClassName = r.ClassName, SectionId = r.SectionId, SectionName = r.SectionName, RosterCount = r.RosterCount,
                        ClassTeacherId = r.ClassTeacherId, ClassTeacherName = r.ClassTeacherName
                    }).ToList()
                });
            }
            catch (Exception ex) { return Error<MissingAttendanceDto>(ex, "listing sections without attendance"); }
        }

        public async Task<ApiResponse<MySectionsDto>> GetMySectionsAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MySectionsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var today = SchoolClock.Today();
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<MySectionsDto>();
                var year = await _repo.GetYearContainingAsync(schoolId, today, ct);
                if (year == null) return Result<MySectionsDto>(409, "No academic year covers today's date.", null);

                string? employeeId = null;
                if (!callerIsSchoolAdmin)
                {
                    var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                    if (loginId == null) return Result<MySectionsDto>(403, "Your account could not be found.", null);
                    employeeId = loginId;
                }
                var rows = await _repo.GetMarkableSectionsAsync(schoolId, year.AcademicYearId, today, employeeId, activeId.Value, ct);
                return Result(200, "Successfully fetched", new MySectionsDto
                {
                    Today = today, AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName,
                    Sections = rows.Select(r => new MarkableSectionDto { ClassId = r.ClassId, ClassName = r.ClassName, SectionId = r.SectionId, SectionName = r.SectionName, RosterCount = r.RosterCount, MarkedToday = r.MarkedToday > 0 }).ToList()
                });
            }
            catch (Exception ex) { return Error<MySectionsDto>(ex, "listing the sections a user can mark"); }
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>A School Admin, or the section's Active class teacher for that year (asked of the Class-Teacher module, which also checks they are still an eligible teacher).</summary>
        private async Task<bool> MayMarkAsync(string schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long academicYearId, CancellationToken ct)
        {
            if (callerIsSchoolAdmin) return true;
            var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
            if (string.IsNullOrEmpty(loginId)) return false;
            var answer = await _classTeachers.IsClassTeacherAsync(schoolId, loginId, sectionId, academicYearId, ct);
            return answer.Status && answer.Data?.IsClassTeacher == true;
        }

        /// <summary>Student ids for a message, capped so a large mistake can't produce a huge error.</summary>
        private static string Names(List<string> ids) => ids.Count <= 20 ? string.Join(", ", ids) : string.Join(", ", ids.Take(20)) + $" and {ids.Count - 20} more";

        private static string FullName(string first, string? middle, string? last) =>
            string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active status is missing in lut_status");
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
