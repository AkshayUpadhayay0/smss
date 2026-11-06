using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// The monthly attendance report's arithmetic, kept pure (no database, no clock) so it can be checked with any inputs. The service feeds it the rows of a few
    /// grouped queries; there is no per-student query.
    /// <para>Rules: Sundays (the school week is Monday to Saturday, as in the Timetable), days of a Holiday calendar event and days outside the academic year are
    /// non-working: greyed, excluded from every percentage and from "not marked". Days after today are never "not marked". A student's days before their enrolment
    /// date do not count for them. Percentage = present / (present + absent) over marked working days only.</para>
    /// </summary>
    public static class AttendanceReportBuilder
    {
        public const decimal ThresholdPercentage = 75m;
        public const string Present = "Present", Absent = "Absent", NotMarked = "NotMarked", NotEnrolled = "NotEnrolled", NonWorking = "NonWorking";
        public const string PresentCode = "PRESENT", AbsentCode = "ABSENT";

        public static MonthlyAttendanceReportDto Build(SectionRef section, long academicYearId, string academicYearName, DateOnly yearStart, DateOnly yearEnd,
            int year, int month, DateOnly today, IReadOnlyList<HolidayRow> holidays, IReadOnlyList<ReportRosterRow> roster, IReadOnlyList<MonthMarkRow> marks, int activeStatusId)
        {
            var first = new DateOnly(year, month, 1);
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var last = new DateOnly(year, month, daysInMonth);

            // marks: student -> (day -> status code), and how many marks each day has
            var byStudent = marks.GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.GroupBy(m => m.Date).ToDictionary(x => x.Key, x => x.First().StatusCode));
            var perDay = marks.GroupBy(m => m.Date).ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).Distinct().Count());

            var days = new List<AttendanceReportDayDto>(daysInMonth);
            for (var i = 0; i < daysInMonth; i++)
            {
                var d = first.AddDays(i);
                var c = AttendanceDayRules.Classify(d, yearStart, yearEnd, holidays);   // Sunday / Holiday / outside the year: shared with the report card's term attendance
                days.Add(new AttendanceReportDayDto
                {
                    Date = d, DayOfWeek = c.DayOfWeek, IsWorkingDay = c.IsWorkingDay, NonWorkingReason = c.NonWorkingReason, HolidayName = c.HolidayName,
                    IsFuture = d > today, MarkedCount = perDay.GetValueOrDefault(d)
                });
            }

            // who is listed: the section's Active students, plus anyone with a mark this month (a student who has since left). Nobody enrolled only after the month, unless they were marked.
            var students = new List<AttendanceReportStudentDto>();
            foreach (var r in roster)
            {
                var active = r.EnrollmentStatusId == activeStatusId && r.StudentStatusId == activeStatusId;
                var hasMarks = byStudent.ContainsKey(r.StudentId);
                if (!(active && r.EnrollmentDate <= last) && !hasMarks) continue;

                var dto = new AttendanceReportStudentDto
                {
                    StudentId = r.StudentId, FullName = FullName(r.FirstName, r.MiddleName, r.LastName), RollNumber = r.RollNumber, IsActive = active,
                    EnrolledFrom = r.EnrollmentDate > first ? r.EnrollmentDate : null
                };
                var own = byStudent.GetValueOrDefault(r.StudentId);
                foreach (var day in days)
                {
                    string cell;
                    var code = own != null && own.TryGetValue(day.Date, out var c) ? c : null;
                    if (day.Date < r.EnrollmentDate) cell = NotEnrolled;
                    else if (code != null) cell = code == PresentCode ? Present : code == AbsentCode ? Absent : NotMarked;
                    else cell = day.IsWorkingDay ? NotMarked : NonWorking;
                    dto.Marks.Add(cell);

                    if (!AttendanceDayRules.CountsForStudent(new DayClass(day.Date, day.DayOfWeek, day.IsWorkingDay, day.NonWorkingReason, day.HolidayName), r.EnrollmentDate)) continue;   // counts for nothing
                    if (cell == Present) dto.PresentDays++;
                    else if (cell == Absent) dto.AbsentDays++;
                    else if (!day.IsFuture) dto.NotMarkedDays++;
                }
                var marked = dto.PresentDays + dto.AbsentDays;
                dto.Percentage = marked == 0 ? null : Math.Round(dto.PresentDays * 100m / marked, 1, MidpointRounding.AwayFromZero);
                dto.BelowThreshold = dto.Percentage is { } p && p < ThresholdPercentage;
                students.Add(dto);
            }
            students = students
                .OrderBy(s => int.TryParse(s.RollNumber, out var n) ? n : int.MaxValue).ThenBy(s => s.RollNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.StudentId, StringComparer.Ordinal).ToList();

            var withPct = students.Where(s => s.Percentage != null).ToList();
            var elapsedWorking = days.Where(d => d.IsWorkingDay && !d.IsFuture).ToList();
            var missed = elapsedWorking.Where(d => d.MarkedCount == 0).Select(d => d.Date).ToList();

            return new MonthlyAttendanceReportDto
            {
                SectionId = section.SectionId, ClassId = section.ClassId, ClassName = section.ClassName, SectionName = section.SectionName,
                AcademicYearId = academicYearId, AcademicYearName = academicYearName, Year = year, Month = month,
                MonthName = first.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), Today = today, Days = days, Students = students,
                Summary = new AttendanceReportSummaryDto
                {
                    StudentCount = students.Count,
                    // the mean of the exact per-student percentages (not of the rounded ones), rounded once at the end
                    AveragePercentage = withPct.Count == 0 ? null : Math.Round(withPct.Average(s => s.PresentDays * 100m / (s.PresentDays + s.AbsentDays)), 1, MidpointRounding.AwayFromZero),
                    StudentsBelowThreshold = students.Count(s => s.BelowThreshold), ThresholdPercentage = ThresholdPercentage,
                    WorkingDays = elapsedWorking.Count, DaysNotMarked = missed.Count, NotMarkedDates = missed
                }
            };
        }

        private static string FullName(string first, string? middle, string last) =>
            string.Join(' ', new[] { first, middle, last }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
    }
}
