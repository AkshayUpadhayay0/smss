using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>The class teacher's read-only view of their class's fee status. The fee state comes from the same grouped query and the same FeeSchedule.Overall rule as the student list: no fee arithmetic of its own, and no per-student query.</summary>
    public class FeeSectionStatusService : IFeeSectionStatusService
    {
        private readonly IStudentRepository _students;
        private readonly IAttendanceRepository _lookups;
        private readonly IClassTeacherService _classTeachers;
        private readonly ILogger<FeeSectionStatusService> _logger;

        public FeeSectionStatusService(IStudentRepository students, IAttendanceRepository lookups, IClassTeacherService classTeachers, ILogger<FeeSectionStatusService> logger)
        {
            _students = students;
            _lookups = lookups;
            _classTeachers = classTeachers;
            _logger = logger;
        }

        public async Task<ApiResponse<SectionFeeStatusDto>> GetAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                // 404 for a section / year of another school (or none), whoever asks: nothing can be probed
                var section = await _lookups.GetSectionAsync(schoolId, sectionId, ct);
                if (section == null) return Result(404, "Section not found.", null);
                var yearId = academicYearId ?? await _lookups.GetCurrentYearIdAsync(schoolId, ct);
                if (yearId == null) return Result(400, "No current academic year is set for this school.", null);
                var year = await _lookups.GetYearAsync(schoolId, yearId.Value, ct);
                if (year == null) return Result(404, "The selected academic year was not found for your school.", null);

                if (!callerIsSchoolAdmin)
                {
                    var loginId = await _students.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                    var answer = string.IsNullOrEmpty(loginId) ? null : await _classTeachers.IsClassTeacherAsync(schoolId, loginId, sectionId, year.AcademicYearId, ct);
                    if (answer == null || !answer.Status || answer.Data?.IsClassTeacher != true)
                        return Result(403, "You can only see the fee status of the section you are the class teacher of.", null);
                }

                // fixed number of reads: statuses, the section's students, and ONE grouped fee query for all of them
                var statuses = await _students.GetGeneralStatusNamesAsync(ct);
                var rows = (await _students.GetEnrolledStudentsAsync(schoolId, year.AcademicYearId, sectionId, ct))
                    .Where(r => statuses.GetValueOrDefault(r.StatusId) == StatusNames.Active).ToList();
                var fees = await _students.GetFeeAggregatesAsync(schoolId, year.AcademicYearId, rows.Select(r => r.StudentId).ToList(), SchoolClock.Today(), ct);

                var list = rows.Select(r =>
                {
                    var has = fees.TryGetValue(r.StudentId, out var f);
                    var due = has ? f!.TotalDue : 0m;
                    var paid = has ? f!.TotalPaid : 0m;
                    return new SectionFeeStudentDto
                    {
                        RollNumber = r.RollNumber, FullName = string.Join(' ', new[] { r.FirstName, r.MiddleName, r.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        Status = FeeSchedule.Overall(has, due, paid, has ? f!.OverdueCount : 0), TotalDue = due, TotalPaid = paid, Balance = Math.Max(due - paid, 0)
                    };
                })
                .OrderBy(s => int.TryParse(s.RollNumber, out var n) ? n : int.MaxValue).ThenBy(s => s.RollNumber, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ToList();

                return Result(200, "Successfully fetched", new SectionFeeStatusDto
                {
                    SectionId = section.SectionId, ClassName = section.ClassName, SectionName = section.SectionName, AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName,
                    Students = list,
                    Summary = new SectionFeeSummaryDto
                    {
                        Students = list.Count,
                        FullyPaid = list.Count(s => s.Status == FeeSchedule.StatusPaid), PartiallyPaid = list.Count(s => s.Status == FeeSchedule.StatusPartiallyPaid),
                        Unpaid = list.Count(s => s.Status == FeeSchedule.StatusPending), Overdue = list.Count(s => s.Status == FeeSchedule.StatusOverdue),
                        NotSet = list.Count(s => s.Status == FeeSchedule.StatusNotSet)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building a section's fee status");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<SectionFeeStatusDto> Result(int code, string message, SectionFeeStatusDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
