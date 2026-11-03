using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class MyStudentsService : IMyStudentsService
    {
        private readonly IStudentRepository _repo;
        private readonly IStudentService _students;
        private readonly ITimetableService _timetable;
        private readonly IClassTeacherService _classTeachers;
        private readonly ILogger<MyStudentsService> _logger;

        public MyStudentsService(IStudentRepository repo, IStudentService students, ITimetableService timetable, IClassTeacherService classTeachers, ILogger<MyStudentsService> logger)
        {
            _repo = repo;
            _students = students;
            _timetable = timetable;
            _classTeachers = classTeachers;
            _logger = logger;
        }

        public async Task<ApiResponse<MyClassDto>> GetMyClassAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MyClassDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // The caller's own timetable (for the current academic year) says which sections they teach in, and carries the year itself.
                var grid = await _timetable.GetMyGridAsync(schoolId, callerUserId, null, ct);
                if (!grid.Status || grid.Data == null)
                    return grid.StatusCode == 404
                        ? Result<MyClassDto>(403, "Your account is not linked to an employee record.", null)   // the timetable's 404 is "not an employee"
                        : Result<MyClassDto>(grid.StatusCode, grid.Message, null);
                var year = grid.Data;

                var loginId = await _repo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                if (string.IsNullOrEmpty(loginId)) return Result<MyClassDto>(403, "Your account could not be found.", null);

                // Must-have scope: sections where the caller is the Active class teacher (the Class-Teacher module answers, so it applies its own rules).
                var teachers = await _classTeachers.ListAsync(schoolId, year.AcademicYearId, ct);
                if (!teachers.Status || teachers.Data == null) return Result<MyClassDto>(teachers.StatusCode, teachers.Message, null);

                var sections = new Dictionary<long, MyClassSectionDto>();
                foreach (var s in teachers.Data.Sections.Where(s => s.EmployeeId == loginId))
                    sections[s.SectionId] = new MyClassSectionDto { ClassId = s.ClassId, ClassName = s.ClassName, SectionId = s.SectionId, SectionName = s.SectionName, IsClassTeacher = true };

                // Nice-to-have scope: sections they are timetabled to teach a subject in (the Timetable module answers).
                foreach (var e in year.Rows.SelectMany(r => r.Cells).Where(c => c.Entry != null).Select(c => c.Entry!))
                {
                    if (!sections.TryGetValue(e.SectionId, out var sec))
                        sections[e.SectionId] = sec = new MyClassSectionDto { ClassId = e.ClassId, ClassName = e.ClassName, SectionId = e.SectionId, SectionName = e.SectionName };
                    if (!sec.Subjects.Contains(e.SubjectName, StringComparer.OrdinalIgnoreCase)) sec.Subjects.Add(e.SubjectName);
                }

                var statuses = await _repo.GetGeneralStatusNamesAsync(ct);
                var genders = await _repo.GetGenderNamesAsync(ct);
                foreach (var sec in sections.Values)
                {
                    sec.Subjects.Sort(StringComparer.OrdinalIgnoreCase);
                    var rows = await _repo.GetEnrolledStudentsAsync(schoolId, year.AcademicYearId, sec.SectionId, ct);
                    sec.Students = rows
                        .OrderBy(r => r.RollNumber, RollOrder.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
                        .Select(r => new MyClassStudentDto
                        {
                            StudentId = r.StudentId, AdmissionNumber = r.AdmissionNumber, FullName = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName), RollNumber = r.RollNumber,
                            GenderName = r.GenderId.HasValue ? genders.GetValueOrDefault(r.GenderId.Value) : null, StatusName = statuses.GetValueOrDefault(r.StatusId, "Unknown"), ProfilePhotoUrl = r.ProfilePhotoUrl
                        }).ToList();
                }

                return Result(200, "Successfully fetched", new MyClassDto
                {
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.AcademicYearName,
                    // class-teacher sections first, then by class (created in class order) and section name
                    Sections = sections.Values.OrderByDescending(s => s.IsClassTeacher).ThenBy(s => s.ClassId).ThenBy(s => s.SectionName, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }
            catch (Exception ex) { return Error<MyClassDto>(ex, "listing a teacher's students"); }
        }

        public async Task<ApiResponse<StudentProfileDto>> GetMyClassStudentAsync(string? schoolId, long callerUserId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentProfileDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var mine = await GetMyClassAsync(schoolId, callerUserId, ct);
                if (!mine.Status || mine.Data == null) return Result<StudentProfileDto>(mine.StatusCode, mine.Message, null);
                // 404 (not 403) for a student outside the caller's sections, same as for another school's student: nothing can be probed
                var id = (studentId ?? string.Empty).Trim();
                if (!mine.Data.Sections.Any(s => s.Students.Any(x => string.Equals(x.StudentId, id, StringComparison.Ordinal))))
                    return Result<StudentProfileDto>(404, "Student not found in your classes.", null);

                var profile = await _students.GetStudentProfileAsync(schoolId, id, limited: false, ct);
                if (!profile.Status || profile.Data == null) return profile;
                // A teacher does not need these: dropped here, on the server, never sent.
                profile.Data.AadharNumberMasked = null;
                profile.Data.LoginId = null;
                return profile;
            }
            catch (Exception ex) { return Error<StudentProfileDto>(ex, "fetching a student of a teacher's class"); }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }

        // Numeric roll numbers sort as numbers (2 before 10), others as text, blanks last (the same order as the School Admin's student list)
        private sealed class RollOrder : IComparer<string?>
        {
            public static readonly RollOrder Instance = new();

            public int Compare(string? a, string? b)
            {
                var aBlank = string.IsNullOrWhiteSpace(a);
                var bBlank = string.IsNullOrWhiteSpace(b);
                if (aBlank || bBlank) return aBlank == bBlank ? 0 : aBlank ? 1 : -1;
                var aNum = long.TryParse(a, out var an);
                var bNum = long.TryParse(b, out var bn);
                if (aNum && bNum) return an.CompareTo(bn);
                if (aNum != bNum) return aNum ? -1 : 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
