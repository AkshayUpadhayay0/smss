using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Who may read a student's fee data through /api/StudentFee. fee-items: School Admin, Accountant and Principal (CanViewFees) any student of their school; a Student only their own
    /// (another student = 404, not 403); every other role 403; another school's student 404; anonymous 401. fee-dues and the full fees read stay CanViewFees only.
    /// The first half needs no database; the [DbFact] half runs on a scratch PostgreSQL (see TaxTestKit).
    /// </summary>
    public class StudentFeeAccessTests
    {
        private const string School = "SCH1";
        private static readonly string[] Viewers = { "School Admin", "Accountant", "Principal" };
        private static readonly string[] Refused = { "Teacher", "Guardian", "Admission Staff", "Super Admin", "Driver" };

        private static (StudentFeeController c, IStudentFeeBillingService billing) Controller(params string[] roles)
        {
            var billing = Fake.Of<IStudentFeeBillingService>(new()
            {
                ["GetItemsAsync"] = Fake.Ok(new StudentFeeItemsDto()),
                ["GetOwnItemsAsync"] = Fake.Ok(new StudentFeeItemsDto()),
                ["GetDuesAsync"] = Fake.Ok(new StudentFeeDuesDto()),
            });
            var service = Fake.Of<IStudentFeeService>(new() { ["GetFeesAsync"] = Fake.Ok(new StudentFeesDto()) });
            return (Fake.WithUser(new StudentFeeController(service, billing), Fake.User(School, 7, roles)), billing);
        }

        private static List<string> Calls(IStudentFeeBillingService b) => Fake.ProxyOf(b).Calls;

        [Fact]
        public async Task FeeItems_SchoolAdminAccountantPrincipal_ReadAnyStudentOfTheirSchool()
        {
            foreach (var role in Viewers)
            {
                var (c, b) = Controller(role);
                Assert.Equal(200, Fake.Code(await c.GetFeeItems("GHS001ST001", default)));
                Assert.Equal(new[] { "GetItemsAsync" }, Calls(b));
            }
        }

        [Fact]
        public async Task FeeItems_AStudentGoesThroughTheOwnPath_NeverTheUnrestrictedRead()
        {
            var (c, b) = Controller("Student");
            Assert.Equal(200, Fake.Code(await c.GetFeeItems("GHS001ST001", default)));
            Assert.Equal(new[] { "GetOwnItemsAsync" }, Calls(b));
        }

        [Fact]
        public async Task FeeItems_TeacherAndEveryOtherRoleGet403_AndNothingIsRead()
        {
            foreach (var role in Refused)
            {
                var (c, b) = Controller(role);
                Assert.True(403 == Fake.Code(await c.GetFeeItems("GHS001ST001", default)), $"{role} must be refused");
                Assert.Empty(Calls(b));
            }
            var (none, b2) = Controller();
            Assert.Equal(403, Fake.Code(await none.GetFeeItems("GHS001ST001", default)));
            Assert.Empty(Calls(b2));
        }

        [Fact]
        public async Task FeeItems_AStudentWhoIsAlsoStaffKeepsTheStaffRead_AStudentTokenWithoutAUserIdIs401()
        {
            var (c, b) = Controller("Student", "Accountant");
            Assert.Equal(200, Fake.Code(await c.GetFeeItems("GHS001ST002", default)));
            Assert.Equal(new[] { "GetItemsAsync" }, Calls(b));

            var noId = Fake.WithUser(new StudentFeeController(Fake.Of<IStudentFeeService>(), Fake.Of<IStudentFeeBillingService>()), new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("school_id", School), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Student") }, "test")));
            Assert.IsType<UnauthorizedResult>(await noId.GetFeeItems("GHS001ST001", default));
        }

        [Fact]
        public async Task OtherStudentFeeReads_StayCanViewFeesOnly_ATeacherOrStudentIsRefused()
        {
            foreach (var role in Viewers)
            {
                var (c, _) = Controller(role);
                Assert.Equal(200, Fake.Code(await c.GetFeeDues("GHS001ST001", default)));
                Assert.Equal(200, Fake.Code(await c.GetFees("GHS001ST001", default)));
            }
            foreach (var role in Refused.Append("Student"))
            {
                var (c, b) = Controller(role);
                Assert.True(403 == Fake.Code(await c.GetFeeDues("GHS001ST001", default)), $"fee-dues: {role}");
                Assert.True(403 == Fake.Code(await c.GetFees("GHS001ST001", default)), $"fees: {role}");
                Assert.Empty(Calls(b));
            }
        }

        [Fact]
        public void Controller_RequiresALogin_AnonymousIs401_AndEveryReadOfAStudentIsRoleChecked()
        {
            Assert.NotEmpty(typeof(StudentFeeController).GetCustomAttributes<AuthorizeAttribute>());
            Assert.Empty(typeof(StudentFeeController).GetCustomAttributes<AllowAnonymousAttribute>());
            var gets = typeof(StudentFeeController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()).Select(a => a.Template).OrderBy(t => t, StringComparer.Ordinal).ToList();
            // the complete list of reads: preview (a class's structure, no student data), and the three per-student reads, all covered above
            Assert.Equal(new[] { "preview", "{studentId:length(1,20)}", "{studentId:length(1,20)}/fee-dues", "{studentId:length(1,20)}/fee-items" }, gets);
        }

        // ============================================================== a Student's own items vs everyone else's (scratch PostgreSQL)
        private static StudentFeeBillingService Billing(TaxDb db) =>
            new(new StudentFeeBillingRepository(db.Ctx()), new StudentFeeRepository(db.Ctx()), NullLogger<StudentFeeBillingService>.Instance);

        private static long Login(TaxDb db, string school, string loginId)
        {
            var existing = db.Scalar<long>($"select user_id from tb_users where school_id='{school}' and org_user_id='{loginId}'");
            if (existing != 0) return existing;
            // a throwaway login with NO password hash: these tests never create or touch a credential
            return db.Scalar<long>($"insert into tb_users (user_type, org_user_id, school_id, display_name, status_id) values ('Student', '{loginId}', '{school}', '{loginId}', (select sid from lut_status where lower(sname)='active' limit 1)) returning user_id");
        }

        [DbFact]
        public async Task Student_ReadsOwnFeeItems_AnotherStudentIs404_AndIndistinguishableFromUnknown()
        {
            using var db = new TaxDb();
            var u1 = Login(db, TaxDb.SchoolA, "GHS001ST001");
            var svc = Billing(db);

            var own = await svc.GetOwnItemsAsync(TaxDb.SchoolA, u1, "GHS001ST001", default);
            Assert.Equal(200, own.StatusCode);
            Assert.Equal("GHS001ST001", own.Data!.StudentId);
            Assert.NotEmpty(own.Data.Items);

            var other = await svc.GetOwnItemsAsync(TaxDb.SchoolA, u1, "GHS001ST002", default);   // a real student of the same school
            var unknown = await svc.GetOwnItemsAsync(TaxDb.SchoolA, u1, "NOPE999", default);
            Assert.Equal(404, other.StatusCode); Assert.Null(other.Data);
            Assert.Equal(404, unknown.StatusCode);
            Assert.Equal(unknown.Message, other.Message);
            Assert.Equal(404, (await svc.GetOwnItemsAsync(TaxDb.SchoolA, u1, "ghs001st001", default)).StatusCode);   // the id is exact, not fuzzy
        }

        [DbFact]
        public async Task OtherSchool_IsAlways404_StaffAndStudent_AndANonStudentLoginSeesNothing()
        {
            using var db = new TaxDb();
            var svc = Billing(db);
            Assert.Equal(404, (await svc.GetItemsAsync(TaxDb.SchoolB, "GHS001ST001", default)).StatusCode);   // staff of school B asking for school A's student
            var intruder = Login(db, TaxDb.SchoolB, "GHS001ST001");   // same login text, different school
            Assert.Equal(404, (await svc.GetOwnItemsAsync(TaxDb.SchoolB, intruder, "GHS001ST001", default)).StatusCode);
            Assert.Equal(404, (await svc.GetOwnItemsAsync(TaxDb.SchoolA, intruder, "GHS001ST001", default)).StatusCode);   // a token school that does not match the user's school
            Assert.Equal(404, (await svc.GetOwnItemsAsync(TaxDb.SchoolA, TaxDb.UserA, "GHS001ST001", default)).StatusCode);   // an employee login id is not a student id
            Assert.Equal(400, (await svc.GetOwnItemsAsync(null, TaxDb.UserA, "GHS001ST001", default)).StatusCode);
        }
    }
}
