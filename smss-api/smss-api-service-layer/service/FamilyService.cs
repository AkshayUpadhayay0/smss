using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentAccessService : IStudentAccessService
    {
        private const string NotFound = "Student not found.";
        private readonly IParentAccountRepository _accounts;
        private readonly IStudentRepository _students;
        private readonly IGuardianRepository _guardians;

        public StudentAccessService(IParentAccountRepository accounts, IStudentRepository students, IGuardianRepository guardians)
        {
            _accounts = accounts; _students = students; _guardians = guardians;
        }

        public async Task<AccessDecision> ResolveAsync(AccessCaller caller, string? requestedStudentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(caller.SchoolId)) return new(null, 400, TenantMessages.NoSchool);
            var requested = requestedStudentId?.Trim();

            if (caller.IsStudent && !caller.IsGuardian)
            {
                // a Student login: its own record, nothing else. Another id is a 404 (never a 403: that would confirm the id exists)
                var own = await _students.GetUserLoginIdAsync(caller.SchoolId, caller.UserId, ct);
                if (string.IsNullOrEmpty(own) || (!string.IsNullOrEmpty(requested) && !string.Equals(requested, own, StringComparison.Ordinal))) return new(null, 404, NotFound);
                if (await _students.GetStudentAsync(caller.SchoolId, own, ct) == null) return new(null, 404, NotFound);
                return new(new StudentAccess(own, caller.SchoolId, false, null), 200, "");
            }

            if (caller.IsGuardian && !caller.IsStudent)
            {
                // the guardian is the one behind the TOKEN: looked up by the login's user id, and it must be Active, not merged, and of this school
                var info = await _accounts.GetGuardianByUserIdAsync(caller.UserId, ct);
                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (info == null || activeId == null || info.StatusId != activeId || info.MergedIntoGuardianId != null
                    || !string.Equals(info.SchoolId, caller.SchoolId, StringComparison.OrdinalIgnoreCase)
                    || (caller.GuardianIdClaim != null && caller.GuardianIdClaim != info.GuardianId)) return new(null, 404, NotFound);
                if (string.IsNullOrEmpty(requested)) return new(null, 404, NotFound);
                // the student must be this school's AND linked to this guardian AND portal access must be on for the link
                if (!await _accounts.IsChildLinkedAsync(caller.SchoolId, info.GuardianId, requested, ct)) return new(null, 404, NotFound);
                return new(new StudentAccess(requested, caller.SchoolId, true, info.GuardianId), 200, "");
            }

            return new(null, 403, "Only a student or a parent login can use this.");
        }
    }

    public class FamilyService : IFamilyService
    {
        private readonly IStudentAccessService _access;
        private readonly IParentAccountRepository _accounts;
        private readonly IStudentRepository _students;
        private readonly IGuardianRepository _guardians;
        private readonly IStudentService _studentService;
        private readonly IAttendanceService _attendance;
        private readonly IHomeworkService _homework;
        private readonly ITimetableService _timetable;
        private readonly IReportResultService _reports;
        private readonly IReportScheduleService _schedule;
        private readonly IStudentMyFeesService _fees;
        private readonly IFeePaymentService _payments;
        private readonly IStudentLeaveService _leave;
        private readonly IStudentPhotoService _photos;
        private readonly INoticeService _notices;
        private readonly ISchoolEventService _events;
        private readonly ILogger<FamilyService> _logger;

        public FamilyService(IStudentAccessService access, IParentAccountRepository accounts, IStudentRepository students, IGuardianRepository guardians, IStudentService studentService, IAttendanceService attendance,
            IHomeworkService homework, ITimetableService timetable, IReportResultService reports, IReportScheduleService schedule, IStudentMyFeesService fees, IFeePaymentService payments, IStudentLeaveService leave,
            IStudentPhotoService photos, INoticeService notices, ISchoolEventService events, ILogger<FamilyService> logger, IEarlierYearDuesRepository? earlier = null)
        {
            _earlier = earlier;
            _access = access; _accounts = accounts; _students = students; _guardians = guardians; _studentService = studentService; _attendance = attendance; _homework = homework; _timetable = timetable;
            _reports = reports; _schedule = schedule; _fees = fees; _payments = payments; _leave = leave; _photos = photos; _notices = notices; _events = events; _logger = logger;
        }

        private static ApiResponse<T> Refused<T>(AccessDecision d) => new() { Status = false, StatusCode = d.StatusCode, Message = d.Message, Data = default };
        private static ApiResponse<T> Ok<T>(T data) => new() { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = data };
        /// <summary>The student photo path points at the staff / student endpoint, which a parent is refused: a parent reads the same private file through the family endpoint (and the helper).</summary>
        private static string? FamilyPhoto(string? apiPath) => apiPath?.Replace("/api/Student/", "/api/Family/children/");
        private static string FullName(string first, string? middle, string? last) => string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        // ------------------------------------------------------------------------------------------------ the children list
        public async Task<ApiResponse<FamilyChildrenDto>> ListChildrenAsync(AccessCaller caller, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(caller.SchoolId)) return Refused<FamilyChildrenDto>(new(null, 400, TenantMessages.NoSchool));
            if (!caller.IsGuardian || caller.IsStudent) return Refused<FamilyChildrenDto>(new(null, 403, "Only a parent login has a family screen."));   // a student login never opens it
            try
            {
                var info = await _accounts.GetGuardianByUserIdAsync(caller.UserId, ct);
                var activeId = await _guardians.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (info == null || activeId == null || info.StatusId != activeId || info.MergedIntoGuardianId != null || !string.Equals(info.SchoolId, caller.SchoolId, StringComparison.OrdinalIgnoreCase)
                    || (caller.GuardianIdClaim != null && caller.GuardianIdClaim != info.GuardianId)) return Refused<FamilyChildrenDto>(new(null, 404, "Student not found."));

                var statuses = await _students.GetGeneralStatusNamesAsync(ct);
                var today = SchoolClock.Today();
                var result = new FamilyChildrenDto();
                var starts = new List<(string StudentId, DateOnly Start)>();
                foreach (var c in await _accounts.ListLinkedChildrenAsync(caller.SchoolId, info.GuardianId, ct))
                {
                    var enrollments = await _students.GetEnrollmentsAsync(caller.SchoolId, c.StudentId, ct);
                    // the child's OWN enrolment: the current-year one, else an Active one, else the latest (a passed-out or not-yet-promoted child stays visible, read-only)
                    var chosen = enrollments.OrderByDescending(e => e.AcademicYearIsCurrent).ThenByDescending(e => e.StatusId == activeId).ThenByDescending(e => e.AcademicYearStart).ThenByDescending(e => e.EnrollmentId).FirstOrDefault();
                    var outcome = await _accounts.GetLatestOutcomeAsync(caller.SchoolId, c.StudentId, ct);
                    var studentActive = c.StatusId == activeId;
                    string? chip = outcome is "PASSED_OUT" or "LEFT" or "TRANSFERRED_OUT" ? outcome : !studentActive ? "LEFT" : chosen != null && !chosen.AcademicYearIsCurrent ? "PENDING_PROMOTION" : null;

                    var card = new FamilyChildCardDto
                    {
                        StudentId = c.StudentId, FullName = FullName(c.FirstName, c.MiddleName, c.LastName), AdmissionNumber = c.AdmissionNumber, StatusName = statuses.GetValueOrDefault(c.StatusId, "Unknown"),
                        ProfilePhotoUrl = FamilyPhoto(StudentPhotos.ApiPath(c.StudentId, c.ProfilePhotoStored)), AcademicYearName = chosen?.AcademicYearName, ClassName = chosen?.ClassName, SectionName = chosen?.SectionName,
                        RollNumber = chosen?.RollNumber, ChipState = chip, ReadOnly = chip != null,
                    };
                    if (chip == null)
                    {
                        var att = await _attendance.GetForStudentAsync(caller.SchoolId, c.StudentId, today, today, ct);
                        card.AttendanceToday = att.Status ? att.Data?.Days.FirstOrDefault(d => d.Date == today)?.StatusName : null;
                    }
                    if (chosen != null)
                    {
                        var dues = await _students.GetFeeDueRowsAsync(caller.SchoolId, chosen.AcademicYearId, new[] { c.StudentId }, ct);
                        card.FeeDueNow = dues.Where(d => d.DueDate <= today && d.AmountDue > d.AmountPaid).Sum(d => d.AmountDue - d.AmountPaid);
                        starts.Add((c.StudentId, chosen.AcademicYearStart));
                    }
                    result.Children.Add(card);
                }
                await AddEarlierYearsAsync(caller.SchoolId, result, starts, today, ct);
                result.FeeTile = new FamilyFeeTileDto
                {
                    TotalDueNow = result.Children.Sum(c => c.FeeDueNow),
                    TotalEarlierYears = result.Children.Sum(c => c.FeeEarlierYears),
                    AllPaid = result.Children.All(c => c.FeeDueNow == 0 && c.FeeEarlierYears == 0),
                    Children = result.Children.Where(c => c.FeeDueNow > 0 || c.FeeEarlierYears > 0).Select(c => new FamilyFeeChildDto { StudentId = c.StudentId, FullName = c.FullName, DueNow = c.FeeDueNow, EarlierYears = c.FeeEarlierYears }).ToList(),
                };
                return Ok(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Error while listing a parent's children"); return Refused<FamilyChildrenDto>(new(null, 500, "Something went wrong. Please try again later.")); }
        }

        private readonly IEarlierYearDuesRepository? _earlier;

        /// <summary>One grouped read for all the parent's children: what each still owes from years before the year shown for them. Only the children linked to this guardian are asked about (the list above came from the guardian's own links). A failure never fails the page.</summary>
        private async Task AddEarlierYearsAsync(string schoolId, FamilyChildrenDto result, List<(string StudentId, DateOnly Start)> starts, DateOnly today, CancellationToken ct)
        {
            if (_earlier == null || starts.Count == 0) return;
            try
            {
                var rows = await _earlier.GetOutstandingAsync(schoolId, starts.Select(s => s.StudentId).ToList(), starts.Select(s => s.Start).ToList(), today, ct);
                foreach (var g in rows.GroupBy(r => r.StudentId))
                    result.Children.First(c => c.StudentId == g.Key).FeeEarlierYears = g.Sum(r => r.Outstanding);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Earlier-year dues of a parent's children could not be read"); }
        }

        // ------------------------------------------------------------------------------------------------ one child, behind the helper
        private async Task<ApiResponse<T>> ForChildAsync<T>(AccessCaller caller, string studentId, Func<StudentAccess, Task<ApiResponse<T>>> read, CancellationToken ct)
        {
            var d = await _access.ResolveAsync(caller, studentId, ct);
            return d.Access == null ? Refused<T>(d) : await read(d.Access);
        }

        public Task<ApiResponse<FamilyChildProfileDto>> ProfileAsync(AccessCaller caller, string studentId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, async a =>
            {
                var r = await _studentService.GetProfileForStudentAsync(a.SchoolId, a.StudentId, ct);
                if (!r.Status || r.Data == null) return new ApiResponse<FamilyChildProfileDto> { Status = false, StatusCode = r.StatusCode, Message = r.Message };
                var p = r.Data;   // the student-safe profile, MINUS the guardians list (another guardian's contact details) and the student's own mobile / email
                return Ok(new FamilyChildProfileDto
                {
                    StudentId = p.StudentId, FullName = p.FullName, AdmissionNumber = p.AdmissionNumber, StatusName = p.StatusName, ProfilePhotoUrl = FamilyPhoto(p.ProfilePhotoUrl), AcademicYearName = p.AcademicYearName,
                    ClassName = p.ClassName, SectionName = p.SectionName, RollNumber = p.RollNumber, GenderName = p.GenderName, DateOfBirth = p.DateOfBirth, BloodGroupName = p.BloodGroupName, Address = p.Address,
                });
            }, ct);

        public Task<ApiResponse<StudentOwnAttendanceDto>> AttendanceAsync(AccessCaller caller, string studentId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => from == null || to == null
                ? Task.FromResult(new ApiResponse<StudentOwnAttendanceDto> { Status = false, StatusCode = 400, Message = "from and to are required." })
                : _attendance.GetForStudentAsync(a.SchoolId, a.StudentId, from.Value, to.Value, ct), ct);

        public Task<ApiResponse<List<HomeworkDto>>> HomeworkAsync(AccessCaller caller, string studentId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _homework.GetForStudentAsync(a.SchoolId, a.StudentId, from, to, ct), ct);

        public Task<ApiResponse<TimetableGridDto>> TimetableAsync(AccessCaller caller, string studentId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _timetable.GetStudentGridAsync(a.SchoolId, a.StudentId, ct), ct);

        public Task<ApiResponse<List<FamilyExamScheduleDto>>> ExamScheduleAsync(AccessCaller caller, string studentId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, async a =>
            {
                var enrollments = await _students.GetEnrollmentsAsync(a.SchoolId, a.StudentId, ct);
                var current = enrollments.OrderByDescending(e => e.AcademicYearIsCurrent).ThenByDescending(e => e.AcademicYearStart).ThenByDescending(e => e.EnrollmentId).FirstOrDefault();
                if (current == null) return Ok(new List<FamilyExamScheduleDto>());
                var all = await _schedule.GetAsync(a.SchoolId, current.AcademicYearId, ct);
                if (!all.Status || all.Data == null) return new ApiResponse<List<FamilyExamScheduleDto>> { Status = false, StatusCode = all.StatusCode, Message = all.Message };
                var list = new List<FamilyExamScheduleDto>();
                foreach (var scheme in all.Data)
                {
                    var cls = scheme.Classes.FirstOrDefault(c => string.Equals(c.ClassName, current.ClassName, StringComparison.OrdinalIgnoreCase));
                    if (cls == null) continue;   // the child's class is not part of this scheme: nothing of it is shown
                    list.Add(new FamilyExamScheduleDto
                    {
                        SchemeName = scheme.SchemeName, AcademicYearName = scheme.AcademicYearName,
                        Exams = scheme.Exams.Select(e => new FamilyExamDto { TermName = e.TermName, ExamName = e.ExamName, ExamDate = e.ClassDates.Where(d => d.ClassId == cls.ClassId).Select(d => (DateOnly?)d.ExamDate).FirstOrDefault() ?? e.ExamDate }).ToList(),
                    });
                }
                return Ok(list);
            }, ct);

        public Task<ApiResponse<List<MyReportCardDto>>> ReportCardsAsync(AccessCaller caller, string studentId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _reports.CardsForStudentAsync(a.SchoolId, a.StudentId, ct), ct);

        public Task<ApiResponse<MyReportCardDetailDto>> ReportCardAsync(AccessCaller caller, string studentId, long publicationId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _reports.CardForStudentAsync(a.SchoolId, a.StudentId, publicationId, ct), ct);

        public async Task<(AccessDecision Decision, ReportFileResult? File)> ReportCardPdfAsync(AccessCaller caller, string studentId, long publicationId, CancellationToken ct)
        {
            var d = await _access.ResolveAsync(caller, studentId, ct);
            return d.Access == null ? (d, null) : (d, await _reports.CardPdfForStudentAsync(d.Access.SchoolId, d.Access.StudentId, publicationId, ct));
        }

        public Task<ApiResponse<StudentMyFeesDto>> FeesAsync(AccessCaller caller, string studentId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _fees.GetFeesForStudentAsync(a.SchoolId, a.StudentId, ct), ct);

        public Task<ApiResponse<FeeReceiptDto>> ReceiptAsync(AccessCaller caller, string studentId, long paymentId, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _payments.GetReceiptForStudentAsync(a.SchoolId, a.StudentId, paymentId, ct), ct);

        public Task<ApiResponse<List<StudentLeaveDto>>> LeaveAsync(AccessCaller caller, string studentId, string? status, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            ForChildAsync(caller, studentId, a => _leave.GetForStudentAsync(a.SchoolId, a.StudentId, status, from, to, ct), ct);

        public async Task<(AccessDecision Decision, StudentPhotoResult? Photo)> PhotoAsync(AccessCaller caller, string studentId, CancellationToken ct)
        {
            var d = await _access.ResolveAsync(caller, studentId, ct);
            if (d.Access == null) return (d, null);
            // the helper has cleared the child: the photo service only has to read the file (viewer Staff = no further narrowing)
            return (d, await _photos.GetAsync(d.Access.SchoolId, caller.UserId, PhotoViewer.Staff, d.Access.StudentId, ct));
        }

        private static FamilyNoticeDto ToNotice(NoticeDto n) => new() { NoticeId = n.NoticeId, Title = n.Title, Body = n.Body, PublishedAt = n.PublishedAt, IsRead = n.IsRead, PostedBy = n.CreatedByName };
        private static bool ParentOnly(AccessCaller c) => c.IsGuardian && !c.IsStudent;

        public async Task<ApiResponse<List<FamilyNoticeDto>>> NoticesAsync(AccessCaller caller, bool unreadOnly, CancellationToken ct)
        {
            if (!ParentOnly(caller)) return Refused<List<FamilyNoticeDto>>(new(null, 403, "Only a parent login uses the family notices."));
            // school-wide, not per child: the Guardian tier of the existing notice service, for the guardian's own login (no child id exists here to be probed)
            var r = await _notices.GetMyAsync(caller.SchoolId, new NoticeCaller(caller.UserId, false, false), unreadOnly, null, null, ct);
            return new ApiResponse<List<FamilyNoticeDto>> { Status = r.Status, StatusCode = r.StatusCode, Message = r.Message, Data = r.Data?.Select(ToNotice).ToList() };
        }

        public async Task<ApiResponse<UnreadCountDto>> UnreadNoticesAsync(AccessCaller caller, CancellationToken ct) =>
            !ParentOnly(caller) ? Refused<UnreadCountDto>(new(null, 403, "Only a parent login uses the family notices."))
                                : await _notices.GetUnreadCountAsync(caller.SchoolId, new NoticeCaller(caller.UserId, false, false), ct);

        public async Task<ApiResponse<FamilyNoticeDto>> NoticeAsync(AccessCaller caller, long noticeId, CancellationToken ct)
        {
            if (!ParentOnly(caller)) return Refused<FamilyNoticeDto>(new(null, 403, "Only a parent login uses the family notices."));
            var r = await _notices.GetAsync(caller.SchoolId, new NoticeCaller(caller.UserId, false, false), noticeId, ct);   // the notice service checks the Guardian tier and marks it read
            return new ApiResponse<FamilyNoticeDto> { Status = r.Status, StatusCode = r.StatusCode, Message = r.Message, Data = r.Data == null ? null : ToNotice(r.Data) };
        }

        public async Task<ApiResponse<List<FamilyEventDto>>> CalendarAsync(AccessCaller caller, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (!ParentOnly(caller)) return Refused<List<FamilyEventDto>>(new(null, 403, "Only a parent login uses the family calendar."));
            var r = await _events.GetRangeAsync(caller.SchoolId, null, from, to, ct);   // the school is the token's; no event id or school id comes from the request
            return new ApiResponse<List<FamilyEventDto>>
            {
                Status = r.Status, StatusCode = r.StatusCode, Message = r.Message,
                Data = r.Data?.Select(e => new FamilyEventDto { EventId = e.EventId, Title = e.EventTitle, StartDate = e.StartDate, EndDate = e.EndDate, IsAllDay = e.IsAllDay, StartTime = e.StartTime, EndTime = e.EndTime, CategoryName = e.CategoryName, ColorHex = e.ColorHex, Description = e.Description }).ToList(),
            };
        }
    }
}
