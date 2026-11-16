using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Controllers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Who may read a fee receipt (and a student's payment list). School Admin, Accountant and Principal (CanViewFees) any receipt of their own school; a Student only their OWN;
    /// everyone else 403; another school's id 404; anonymous 401. The first half needs no database; the [DbFact] half runs on a scratch PostgreSQL (see TaxTestKit).
    /// </summary>
    public class FeeReceiptAccessTests
    {
        private const string School = "SCH1";
        private static readonly string[] Viewers = { "School Admin", "Accountant", "Principal" };
        private static readonly string[] Refused = { "Teacher", "Guardian", "Admission Staff", "Super Admin", "Driver" };

        private sealed class Spy
        {
            public IFeePaymentService Service = null!;
            public List<string> Calls => Fake.ProxyOf(Service).Calls;
        }

        private static (FeePaymentController c, Spy spy) Controller(params string[] roles)
        {
            var spy = new Spy
            {
                Service = Fake.Of<IFeePaymentService>(new()
                {
                    ["GetReceiptAsync"] = Fake.Ok(new FeeReceiptDto()),
                    ["GetOwnReceiptAsync"] = Fake.Ok(new FeeReceiptDto()),
                    ["GetPaymentsAsync"] = Fake.Ok(new List<PaymentSummaryDto>()),
                })
            };
            return (Fake.WithUser(new FeePaymentController(spy.Service), Fake.User(School, 7, roles)), spy);
        }

        // ============================================================== receipt: role matrix (no database)
        [Fact]
        public async Task Receipt_SchoolAdminAccountantPrincipal_ReadAnyReceiptOfTheirSchool()
        {
            foreach (var role in Viewers)
            {
                var (c, spy) = Controller(role);
                Assert.Equal(200, Fake.Code(await c.GetReceipt(11, default)));
                Assert.Equal(new[] { "GetReceiptAsync" }, spy.Calls);   // the unrestricted staff read, never the student-own one
            }
        }

        [Fact]
        public async Task Receipt_AStudentGoesThroughTheOwnReceiptPath_WithTheUserIdFromTheToken()
        {
            var (c, spy) = Controller("Student");
            Assert.Equal(200, Fake.Code(await c.GetReceipt(11, default)));
            Assert.Equal(new[] { "GetOwnReceiptAsync" }, spy.Calls);   // never the unrestricted read
        }

        [Fact]
        public async Task Receipt_EveryOtherRoleGets403_AndTheServiceIsNeverCalled()
        {
            foreach (var role in Refused)
            {
                var (c, spy) = Controller(role);
                Assert.True(403 == Fake.Code(await c.GetReceipt(11, default)), $"{role} must be refused");
                Assert.Empty(spy.Calls);
            }
            var (none, spy2) = Controller();   // a login with no role at all
            Assert.Equal(403, Fake.Code(await none.GetReceipt(11, default)));
            Assert.Empty(spy2.Calls);
        }

        [Fact]
        public async Task Receipt_AStudentWhoIsAlsoStaffKeepsTheStaffRead_AStudentTokenWithoutAUserIdIs401()
        {
            var (c, spy) = Controller("Student", "Accountant");
            Assert.Equal(200, Fake.Code(await c.GetReceipt(11, default)));
            Assert.Equal(new[] { "GetReceiptAsync" }, spy.Calls);

            var noId = Fake.WithUser(new FeePaymentController(Fake.Of<IFeePaymentService>()), new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("school_id", School), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Student")
            }, "test")));
            Assert.IsType<UnauthorizedResult>(await noId.GetReceipt(11, default));
        }

        // ============================================================== the same rule for the other payment reads
        [Fact]
        public async Task PaymentHistory_IsCanViewFeesOnly_AStudentCannotListAnotherStudentsPayments()
        {
            foreach (var role in Viewers)
            {
                var (c, _) = Controller(role);
                Assert.Equal(200, Fake.Code(await c.GetPayments("GHS001ST001", default)));
            }
            foreach (var role in Refused.Append("Student"))
            {
                var (c, spy) = Controller(role);
                Assert.True(403 == Fake.Code(await c.GetPayments("GHS001ST001", default)), $"{role} must be refused");
                Assert.Empty(spy.Calls);
            }
        }

        [Fact]
        public async Task RecordAndVoid_StayAtSchoolAdminAndAccountant()
        {
            foreach (var role in new[] { "Principal", "Student", "Teacher", "Guardian", "Admission Staff", "Super Admin" })
            {
                var (c, _) = Controller(role);
                Assert.True(403 == Fake.Code(await c.RecordPayment("GHS001ST001", new RecordPaymentRequest { Amount = 1, PaymentMode = "Cash" }, default)), $"record: {role}");
                Assert.True(403 == Fake.Code(await c.VoidPayment(11, new VoidPaymentRequest { VoidReason = "x" }, default)), $"void: {role}");
            }
        }

        [Fact]
        public void Controller_RequiresALogin_AnonymousIs401_NoEndpointAllowsAnonymousOrReturnsAPaymentById()
        {
            Assert.NotEmpty(typeof(FeePaymentController).GetCustomAttributes<AuthorizeAttribute>());
            Assert.Empty(typeof(FeePaymentController).GetCustomAttributes<AllowAnonymousAttribute>());
            foreach (var m in typeof(FeePaymentController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
            // there is no GET {id} and no PDF endpoint: the receipt route is the only read of a payment by id, and print / PDF are drawn in the browser from that same response
            var gets = typeof(FeePaymentController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()).Select(a => a.Template).OrderBy(t => t, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "students/{studentId:length(1,20)}", "{paymentId:long}/receipt" }, gets);
        }

        // ============================================================== receipt: who may see which payment (scratch PostgreSQL)
        private static long NewLogin(TaxDb db, string school, string loginId, string type)
        {
            // reuse the login if the template already has one; otherwise a throwaway login with NO password hash: these tests never create or touch a credential
            var existing = db.Scalar<long>($"select user_id from tb_users where school_id='{school}' and org_user_id='{loginId}'");
            if (existing != 0) return existing;
            return db.Scalar<long>($"insert into tb_users (user_type, org_user_id, school_id, display_name, status_id) values ('{type}', '{loginId}', '{school}', '{loginId}', (select sid from lut_status where lower(sname)='active' limit 1)) returning user_id");
        }

        [DbFact]
        public async Task Student_ReadsOwnReceipt_But_AnotherStudentsIs404_AndSoIsAVoidedOne()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 5);
            var p1 = (await db.Pay("GHS001ST001", 1000, gst.TaxTypeId)).Data!;
            var p2 = (await db.Pay("GHS001ST002", 500)).Data!;
            var u1 = NewLogin(db, TaxDb.SchoolA, "GHS001ST001", "Student");
            var u2 = NewLogin(db, TaxDb.SchoolA, "GHS001ST002", "Student");
            var svc = db.Payments();

            var own = await svc.GetOwnReceiptAsync(TaxDb.SchoolA, u1, p1.PaymentId, default);
            Assert.Equal(200, own.StatusCode);
            Assert.Equal(p1.ReceiptNumber, own.Data!.ReceiptNumber);
            Assert.Equal(50m, own.Data.TaxAmount); Assert.Equal(1050m, own.Data.TotalAmount);

            // student 2 reading student 1's receipt (and the other way round): 404, the same answer as a payment that does not exist
            var other = await svc.GetOwnReceiptAsync(TaxDb.SchoolA, u2, p1.PaymentId, default);
            Assert.Equal(404, other.StatusCode); Assert.Null(other.Data);
            Assert.Equal(404, (await svc.GetOwnReceiptAsync(TaxDb.SchoolA, u1, p2.PaymentId, default)).StatusCode);
            Assert.Equal(404, (await svc.GetOwnReceiptAsync(TaxDb.SchoolA, u1, 987654321, default)).StatusCode);
            Assert.Equal(other.Message, (await svc.GetOwnReceiptAsync(TaxDb.SchoolA, u1, 987654321, default)).Message);   // indistinguishable from "no such payment"

            // their own receipt once it has been voided: hidden (who voided it and why is internal), like the student's fee view
            Assert.Equal(200, (await svc.VoidPaymentAsync(TaxDb.SchoolA, p1.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "entered twice" }, default)).StatusCode);
            Assert.Equal(404, (await svc.GetOwnReceiptAsync(TaxDb.SchoolA, u1, p1.PaymentId, default)).StatusCode);
            Assert.Equal(200, (await svc.GetReceiptAsync(TaxDb.SchoolA, p1.PaymentId, default)).StatusCode);   // staff still see it, VOID and all
        }

        [DbFact]
        public async Task OtherSchoolsPayment_Is404_ForStaffAndForAStudent_AndAStudentLoginOfAnotherSchoolSeesNothing()
        {
            using var db = new TaxDb();
            var p = (await db.Pay("GHS001ST001", 1000)).Data!;
            var svc = db.Payments();
            Assert.Equal(404, (await svc.GetReceiptAsync(TaxDb.SchoolB, p.PaymentId, default)).StatusCode);   // staff of school B
            var intruder = NewLogin(db, TaxDb.SchoolB, "GHS001ST001", "Student");   // same login id text, different school
            Assert.Equal(404, (await svc.GetOwnReceiptAsync(TaxDb.SchoolB, intruder, p.PaymentId, default)).StatusCode);
            Assert.Equal(404, (await svc.GetOwnReceiptAsync(TaxDb.SchoolA, intruder, p.PaymentId, default)).StatusCode);   // a token school that does not match the user's school
        }

        [DbFact]
        public async Task ANonStudentLogin_UsingTheOwnPath_SeesNothing_AndAMissingSchoolIs400()
        {
            using var db = new TaxDb();
            var p = (await db.Pay("GHS001ST001", 1000)).Data!;
            var svc = db.Payments();
            Assert.Equal(404, (await svc.GetOwnReceiptAsync(TaxDb.SchoolA, TaxDb.UserA, p.PaymentId, default)).StatusCode);   // an employee login id is not the student id
            Assert.Equal(400, (await svc.GetOwnReceiptAsync(null, TaxDb.UserA, p.PaymentId, default)).StatusCode);
        }
    }
}
