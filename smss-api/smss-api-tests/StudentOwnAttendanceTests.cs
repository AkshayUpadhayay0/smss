using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    // GET api/Attendance/student/my. No database: the repository is an in-memory fake keyed by (school, student), so "another school's data" is
    // proven by the fake only ever answering for the school the token names. Nothing here touches a real account.
    public class StudentOwnAttendanceTests
    {
        private const string School = "SCH1", OtherSchool = "SCH2", MyStudent = "ST001", OtherStudent = "ST002";
        private const long MyUserId = 11;

        /// <summary>Answers only the repository calls the endpoint makes; any other call fails the test loudly.</summary>
        public class FakeRepo : DispatchProxy
        {
            public List<(string School, string Student, StudentDayRow Row)> Days = new();
            public Dictionary<(string School, long User), string> Logins = new();
            public List<(string School, string Student)> Students = new();

            protected override object? Invoke(MethodInfo? m, object?[]? a)
            {
                switch (m!.Name)
                {
                    case "GetUserAsync":
                        return Task.FromResult<(string?, string?)>(Logins.TryGetValue(((string)a![0]!, (long)a[1]!), out var login) ? (login, "n") : (null, null));
                    case "GetStudentAsync":
                        return Task.FromResult(Students.Any(s => s.School == (string)a![0]! && s.Student == (string)a[1]!) ? new StudentRef((string)a![1]!, "Asha", null, "Rao") : null);
                    case "GetStudentDaysAsync":
                        var (sc, st, f, t) = ((string)a![0]!, (string)a[1]!, (DateOnly)a[2]!, (DateOnly)a[3]!);
                        return Task.FromResult(Days.Where(d => d.School == sc && d.Student == st && d.Row.Date >= f && d.Row.Date <= t).Select(d => d.Row).ToList());
                    default: throw new NotSupportedException(m.Name);
                }
            }
        }

        private static AttendanceController Controller(params string[] roles) => ControllerFor(School, roles);

        private static AttendanceController ControllerFor(string? school, params string[] roles)
        {
            var repo = DispatchProxy.Create<IAttendanceRepository, FakeRepo>();
            var fake = (FakeRepo)(object)repo;
            fake.Students.Add((School, MyStudent)); fake.Students.Add((School, OtherStudent)); fake.Students.Add((OtherSchool, MyStudent));
            fake.Logins[(School, MyUserId)] = MyStudent;
            // my days include a CORRECTED one with a reason; another student's day and another school's day must never come back
            fake.Days.Add((School, MyStudent, new StudentDayRow(new DateOnly(2026, 10, 5), 7, "Class 5", "A", "PRESENT", "Present", null, null)));
            fake.Days.Add((School, MyStudent, new StudentDayRow(new DateOnly(2026, 10, 6), 7, "Class 5", "A", "ABSENT", "Absent", DateTime.UtcNow, "Admin fixed it")));
            fake.Days.Add((School, OtherStudent, new StudentDayRow(new DateOnly(2026, 10, 5), 7, "Class 5", "A", "ABSENT", "Absent", null, null)));
            fake.Days.Add((OtherSchool, MyStudent, new StudentDayRow(new DateOnly(2026, 10, 7), 99, "Class 9", "Z", "ABSENT", "Absent", null, null)));

            var service = new AttendanceService(repo, null!, NullLogger<AttendanceService>.Instance);
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, MyUserId.ToString()) };
            if (school != null) claims.Add(new Claim("school_id", school));
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new AttendanceController(service)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } }
            };
        }

        private static int Code(IActionResult r) => ((ObjectResult)r).StatusCode!.Value;
        private static readonly DateOnly Oct1 = new(2026, 10, 1), Oct31 = new(2026, 10, 31);

        [Fact]
        public async Task Student_gets_own_days_only_and_no_audit_fields()
        {
            var r = await Controller("Student").GetStudentMy(Oct1, Oct31, default);
            Assert.Equal(200, Code(r));
            var body = Assert.IsType<ApiResponse<StudentOwnAttendanceDto>>(((ObjectResult)r).Value);
            Assert.Equal(new[] { new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6) }, body.Data!.Days.Select(d => d.Date).ToArray());   // not 10-07 (other school), not the other student
            Assert.Equal(2, body.Data.Summary.MarkedDays);
            Assert.Equal(50.0m, body.Data.Summary.PresentPercentage);
            // the student-safe day type has exactly date + status: nothing about corrections, the section or who marked it
            Assert.Equal(new[] { "Date", "StatusCode", "StatusName" }, typeof(StudentOwnAttendanceDayDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
        }

        [Theory]
        [InlineData("Teacher")]
        [InlineData("School Admin")]
        [InlineData("Principal")]
        public async Task Staff_roles_get_403(string role) =>
            Assert.Equal(403, Code(await Controller(role).GetStudentMy(Oct1, Oct31, default)));

        [Fact]
        public async Task A_token_for_another_school_never_reaches_this_schools_student()
        {
            // same user id, but the token says SCH2: the fake has no login for (SCH2, 11), so it is "not linked", never SCH1's data
            Assert.Equal(403, Code(await ControllerFor(OtherSchool, "Student").GetStudentMy(Oct1, Oct31, default)));
        }

        [Fact]
        public async Task Token_without_a_school_is_400() =>
            Assert.Equal(400, Code(await ControllerFor(null, "Student").GetStudentMy(Oct1, Oct1.AddDays(1), default)));

        [Fact]
        public async Task Missing_from_or_to_is_400()
        {
            Assert.Equal(400, Code(await Controller("Student").GetStudentMy(null, Oct31, default)));
            Assert.Equal(400, Code(await Controller("Student").GetStudentMy(Oct1, null, default)));
        }

        [Fact]
        public async Task Reversed_range_is_400() =>
            Assert.Equal(400, Code(await Controller("Student").GetStudentMy(Oct31, Oct1, default)));

        [Fact]
        public async Task Range_cap_is_92_days_inclusive()
        {
            var from = new DateOnly(2026, 1, 1);
            Assert.Equal(200, Code(await Controller("Student").GetStudentMy(from, from.AddDays(91), default)));   // 92 days
            Assert.Equal(400, Code(await Controller("Student").GetStudentMy(from, from.AddDays(92), default)));   // 93 days
        }
    }
}
