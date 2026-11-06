using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>One student's attendance over a term: marked working days only (present + absent), plus the working days nobody marked for them.</summary>
    public sealed record TermAttendanceRow(string StudentId, int Present, int Absent, int NotMarked);

    public sealed class TermAttendanceResult
    {
        /// <summary>Working days of the term so far (not a Sunday, not a Holiday, inside the academic year, not in the future).</summary>
        public int WorkingDays { get; init; }
        /// <summary>Working days so far on which nobody in the section was marked at all.</summary>
        public List<DateOnly> UnmarkedDates { get; init; } = new();
        public Dictionary<string, TermAttendanceRow> ByStudent { get; init; } = new();
    }

    /// <summary>
    /// AUTO attendance of a term, from the Attendance module's own day rules (AttendanceDayRules). Pure: the caller loads the roster, the marks and the holidays. Shared by the class teacher's
    /// attendance preview and the report card results, so the two can never disagree.
    /// </summary>
    public static class TermAttendanceRules
    {
        public static TermAttendanceResult Compute(DateOnly from, DateOnly to, DateOnly today, DateOnly yearStart, DateOnly yearEnd, IReadOnlyList<HolidayRow> holidays,
            IReadOnlyList<ReportRosterRow> roster, IReadOnlyList<MonthMarkRow> marks, int activeStatusId)
        {
            var byStudent = marks.Where(m => m.Date >= from && m.Date <= to).GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.GroupBy(m => m.Date).ToDictionary(x => x.Key, x => x.First().StatusCode));
            var markedPerDay = marks.Where(m => m.Date >= from && m.Date <= to).GroupBy(m => m.Date).ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).Distinct().Count());
            var days = new List<DayClass>();
            for (var d = from; d <= to; d = d.AddDays(1)) days.Add(AttendanceDayRules.Classify(d, yearStart, yearEnd, holidays));
            var elapsed = days.Where(d => d.IsWorkingDay && d.Date <= today).ToList();
            var result = new TermAttendanceResult { WorkingDays = elapsed.Count, UnmarkedDates = elapsed.Where(d => markedPerDay.GetValueOrDefault(d.Date) == 0).Select(d => d.Date).ToList() };
            foreach (var r in roster)
            {
                var active = r.EnrollmentStatusId == activeStatusId && r.StudentStatusId == activeStatusId;
                var own = byStudent.GetValueOrDefault(r.StudentId);
                if (!(active && r.EnrollmentDate <= to) && own == null) continue;
                int present = 0, absent = 0, notMarked = 0;
                foreach (var day in days)
                {
                    if (!AttendanceDayRules.CountsForStudent(day, r.EnrollmentDate)) continue;
                    var code = own != null && own.TryGetValue(day.Date, out var c) ? c : null;
                    if (code == AttendanceReportBuilder.PresentCode) present++;
                    else if (code == AttendanceReportBuilder.AbsentCode) absent++;
                    else if (day.Date <= today) notMarked++;
                }
                result.ByStudent[r.StudentId] = new TermAttendanceRow(r.StudentId, present, absent, notMarked);
            }
            return result;
        }
    }
}
