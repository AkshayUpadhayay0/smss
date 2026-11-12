using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.reportcard;
using Xunit;

namespace smss_api_tests
{
    /// <summary>The monthly attendance report PDF follows the same 7 pt floor as the report cards (day numbers, weekday letters, marks, page numbers and the footer note included).</summary>
    public class AttendanceReportFontFloorTests
    {
        private static MonthlyAttendanceReportDto Report(int daysInMonth, int students)
        {
            var days = Enumerable.Range(1, daysInMonth).Select(d =>
            {
                var date = new DateOnly(2026, 10, d);
                var dow = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;
                return new AttendanceReportDayDto { Date = date, DayOfWeek = dow, IsWorkingDay = dow != 7, IsFuture = d > 25 };
            }).ToList();
            var rows = Enumerable.Range(1, students).Select(i => new AttendanceReportStudentDto
            {
                StudentId = $"S{i:D3}", FullName = $"Student Number {i} With A Fairly Long Name", RollNumber = i.ToString(), IsActive = i % 7 != 0,
                Marks = days.Select(d => !d.IsWorkingDay ? AttendanceReportBuilder.NonWorking : d.IsFuture ? AttendanceReportBuilder.NotMarked : (d.Date.Day + i) % 5 == 0 ? AttendanceReportBuilder.Absent : AttendanceReportBuilder.Present).ToList(),
                PresentDays = 15, AbsentDays = 4, Percentage = 78.9m, BelowThreshold = i % 4 == 0,
            }).ToList();
            return new MonthlyAttendanceReportDto
            {
                ClassName = "Class 5", SectionName = "A", AcademicYearName = "2026-27", Year = 2026, Month = 10, MonthName = "October 2026", Today = new DateOnly(2026, 10, 25),
                Days = days, Students = rows,
                Summary = new AttendanceReportSummaryDto { StudentCount = students, AveragePercentage = 80.5m, StudentsBelowThreshold = 3, ThresholdPercentage = 75, WorkingDays = 20 },
            };
        }

        [Theory]
        [InlineData(28, 5)] [InlineData(30, 40)] [InlineData(31, 60)] [InlineData(31, 120)]
        public void No_text_in_the_monthly_attendance_report_is_smaller_than_7_pt(int daysInMonth, int students)
        {
            var sizes = PdfTextSizes.Sizes(AttendanceReportPdf.Render(Report(daysInMonth, students), "Sample Public School"));
            Assert.NotEmpty(sizes);
            Assert.True(sizes.Keys.Min() >= RcKit.MinFontPt, $"prints text at {sizes.Keys.Min()} pt: the floor is {RcKit.MinFontPt} pt.");
        }
    }
}
