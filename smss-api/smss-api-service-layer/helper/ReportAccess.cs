using smss_api_db_layer.@interface;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>What one caller may do in one (class, section, subject) of one academic year.</summary>
    public sealed record ReportRights(bool IsAdmin, bool IsPrincipal, bool IsClassTeacher, bool Teaches, bool Mapped)
    {
        /// <summary>The union rule: School Admin, OR the section's class teacher, OR timetabled to teach this subject in this section. The Class-Subject Mapping is required in every case.</summary>
        public bool CanEnter => Mapped && (IsAdmin || IsClassTeacher || Teaches);
        /// <summary>Accept and reopen: School Admin or the section's class teacher.</summary>
        public bool CanReview => IsAdmin || IsClassTeacher;
        /// <summary>Reading: the above, the Principal (read-only), or a subject teacher of this section.</summary>
        public bool CanView => IsAdmin || IsPrincipal || IsClassTeacher || Teaches;
    }

    /// <summary>
    /// The one place that answers "may this caller enter, review or read here". Class teacher and timetable answers come from the same tables the Attendance, Timetable and
    /// Class-Teacher modules use (an Active assignment / an Active timetable entry for the year, and the person must still be a usable Teacher).
    /// </summary>
    public sealed class ReportAccess
    {
        private readonly IReportMarksRepository _repo;
        private readonly IClassTeacherRepository _teachers;
        public ReportAccess(IReportMarksRepository repo, IClassTeacherRepository teachers) { _repo = repo; _teachers = teachers; }

        /// <summary>The caller's employee id when they are a usable Teacher (not a School Admin), else null.</summary>
        public async Task<string?> EmployeeIdAsync(string schoolId, ReportCaller caller, int activeId, CancellationToken ct)
        {
            if (!caller.IsTeacher) return null;
            var loginId = await _repo.UserLoginIdAsync(schoolId, caller.UserId, ct);
            if (string.IsNullOrEmpty(loginId)) return null;
            return await _teachers.IsEligibleTeacherAsync(schoolId, loginId, activeId, RoleCodes.Teacher, ct) ? loginId : null;
        }

        public async Task<ReportRights> RightsAsync(string schoolId, ReportCaller caller, long academicYearId, long classId, long sectionId, long? subjectId, int activeId, CancellationToken ct)
        {
            var employeeId = caller.IsAdmin ? null : await EmployeeIdAsync(schoolId, caller, activeId, ct);
            var classTeacher = employeeId != null && (await _repo.ClassTeacherSectionIdsAsync(schoolId, employeeId, academicYearId, activeId, ct)).Contains(sectionId);
            var teaches = employeeId != null && subjectId != null && (await _repo.TeachingPairsAsync(schoolId, employeeId, academicYearId, activeId, ct)).Contains((sectionId, subjectId.Value));
            var mapped = subjectId == null || await _repo.IsMappedAsync(schoolId, classId, subjectId.Value, activeId, ct);
            return new ReportRights(caller.IsAdmin, caller.IsPrincipal, classTeacher, teaches, mapped);
        }
    }
}

namespace smss_api_service_layer.helper
{
    /// <summary>Roll numbers in the order teachers expect: 1, 2, 10 (numeric first), then text, and students without a roll number last.</summary>
    public sealed class RollComparer : IComparer<string?>
    {
        public static readonly RollComparer Instance = new();
        public int Compare(string? a, string? b)
        {
            var ea = string.IsNullOrWhiteSpace(a); var eb = string.IsNullOrWhiteSpace(b);
            if (ea || eb) return ea == eb ? 0 : ea ? 1 : -1;
            var na = int.TryParse(a, out var x); var nb = int.TryParse(b, out var y);
            if (na && nb) return x.CompareTo(y);
            if (na != nb) return na ? -1 : 1;
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }
    }
}
