using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.entity;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 3e (no migration): a school's WhatsApp sender status (NOT_REGISTERED, PENDING, ACTIVE). The Super Admin sets the number, the Meta phone-number id and the status; School Admin and Principal only read their own
    /// school's status (school from the token); everyone else is refused. No sending, no API call, and no credential anywhere. [DbFact]: needs SMSS_TEST_PG and a template with 3a.
    /// </summary>
    public class WhatsAppSender3eTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;
        /// <summary>The Super Admin's real user id (updated_by is a foreign key to tb_users).</summary>
        private static long SuperUser(TaxDb db) => db.Scalar<long>("select user_id from tb_users where user_type='Super Admin' order by user_id limit 1");

        private static WhatsAppSenderService Svc(TaxDb db) => new(new SchoolWhatsAppSenderRepository(db.Ctx()), NullLogger<WhatsAppSenderService>.Instance);
        private static SaveSenderRequestDto Save(string status, string? number = "9876543210", string? phoneId = null) => new() { Status = status, WhatsAppNumber = number, PhoneNumberId = phoneId };

        // ------------------------------------------------------------------------------------------------ roles
        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_Super_Admin_may_change_the_sender_and_only_School_Admin_Principal_and_Super_Admin_may_read_it(string role)
        {
            var superAdmin = role == "Super Admin";
            var reads = superAdmin || role is "School Admin" or "Principal";
            var svc = Fake.Of<IWhatsAppSenderService>();
            var c = Fake.WithUser(new WhatsAppSenderController(svc), Fake.User(superAdmin ? null : A, 5, role));

            var save = Fake.Code(await c.Save(A, Save("PENDING"), default));
            if (superAdmin) Assert.NotEqual(403, save); else Assert.Equal(403, save);
            Assert.Equal(superAdmin, Fake.ProxyOf(svc).Calls.Contains("SaveAsync"));

            var get = Fake.Code(await c.Get(A, default));
            if (reads) Assert.NotEqual(403, get); else Assert.Equal(403, get);

            var mine = Fake.Code(await c.Mine(default));
            if (role is "School Admin" or "Principal") Assert.NotEqual(403, mine); else Assert.Equal(403, mine);   // "me" is a school user's own school: the Super Admin has none
        }

        [DbFact]
        public async Task A_school_user_reads_only_their_own_school_another_school_or_a_missing_one_is_a_404_and_no_row_is_made_for_it()
        {
            using var db = new TaxDb();
            db.Exec($"delete from tb_school_whatsapp_senders where school_id='{B}'");
            Assert.Equal(404, (await Svc(db).GetAsync(A, B, false, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).GetAsync(A, "NO-SUCH-SCHOOL", false, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).GetAsync(A, "NO-SUCH-SCHOOL", true, default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_school_whatsapp_senders where school_id='{B}'"));   // a refused read creates nothing
            Assert.Equal(200, (await Svc(db).GetAsync(A, A, false, default)).StatusCode);
            Assert.Equal(200, (await Svc(db).GetAsync(null, B, true, default)).StatusCode);   // the Super Admin reaches any school
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_school_whatsapp_senders where school_id='{B}'"));
        }

        // ------------------------------------------------------------------------------------------------ the lazy row
        [DbFact]
        public async Task A_school_without_a_sender_row_counts_as_not_registered_and_the_row_is_made_on_first_read_and_on_first_save()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_schools set whatsapp_number='+919800000042' where school_id='{A}'");
            db.Exec($"delete from tb_school_whatsapp_senders where school_id in ('{A}','{B}')");
            var read = (await Svc(db).GetAsync(A, A, false, default)).Data!;
            Assert.Equal("NOT_REGISTERED", read.Status);
            Assert.Equal("Not registered", read.StatusLabel);
            Assert.Equal("+919800000042", read.WhatsAppNumber);   // the school's own number is the starting point
            Assert.Contains("platform", read.SendsFrom);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_school_whatsapp_senders where school_id='{A}'"));

            var saved = await Svc(db).SaveAsync(B, Save("PENDING"), SuperUser(db), default);   // first save for a school with no row
            Assert.True(saved.Status, saved.Message);
            Assert.Equal("PENDING", db.Scalar<string>($"select status from tb_school_whatsapp_senders where school_id='{B}'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_school_whatsapp_senders where school_id='{B}'"));
        }

        [DbFact]
        public async Task Two_reads_at_the_same_moment_for_a_school_without_a_row_leave_exactly_one_row()
        {
            using var db = new TaxDb();
            db.Exec($"delete from tb_school_whatsapp_senders where school_id='{A}'");
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Svc(db).GetAsync(A, A, false, default))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_school_whatsapp_senders where school_id='{A}'"));
        }

        // ------------------------------------------------------------------------------------------------ the status rules
        [DbFact]
        public async Task ACTIVE_needs_the_phone_number_id_and_a_number_and_records_when_it_was_verified_and_the_database_agrees()
        {
            using var db = new TaxDb();
            var noId = await Svc(db).SaveAsync(A, Save("ACTIVE"), SuperUser(db), default);
            Assert.Equal(400, noId.StatusCode); Assert.Contains("phone number id", noId.Message);
            var noNumber = await Svc(db).SaveAsync(A, Save("ACTIVE", null, "1234567890"), SuperUser(db), default);
            Assert.Equal(400, noNumber.StatusCode); Assert.Contains("WhatsApp number is required", noNumber.Message);
            Assert.Equal(400, (await Svc(db).SaveAsync(A, Save("PENDING", null), SuperUser(db), default)).StatusCode);   // PENDING needs the number too
            Assert.Equal(400, (await Svc(db).SaveAsync(A, Save("ACTIVE", "12345", "1234567890"), SuperUser(db), default)).StatusCode);   // an invalid number
            Assert.Equal(400, (await Svc(db).SaveAsync(A, Save("ACTIVE", "9876543210", "abc-123"), SuperUser(db), default)).StatusCode);   // a phone number id is digits
            Assert.Equal(400, (await Svc(db).SaveAsync(A, Save("LIVE"), SuperUser(db), default)).StatusCode);
            Assert.Equal("NOT_REGISTERED", db.Scalar<string>($"select status from tb_school_whatsapp_senders where school_id='{A}'"));   // nothing changed by the refusals

            var ok = await Svc(db).SaveAsync(A, Save("ACTIVE", "98765 43210", " 1234567890 "), SuperUser(db), default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("ACTIVE", ok.Data!.Status);
            Assert.Contains("own WhatsApp number", ok.Data.SendsFrom);
            Assert.Equal("+919876543210", db.Scalar<string>($"select whatsapp_number from tb_school_whatsapp_senders where school_id='{A}'"));
            Assert.Equal("1234567890", db.Scalar<string>($"select phone_number_id from tb_school_whatsapp_senders where school_id='{A}'"));
            var verified = db.Scalar<string>($"select to_char(verified_at, 'YYYY-MM-DD HH24:MI:SS.US') from tb_school_whatsapp_senders where school_id='{A}'");
            Assert.NotNull(ok.Data.VerifiedAt);

            // saving again while ACTIVE keeps the original verification time; leaving ACTIVE clears it
            await Svc(db).SaveAsync(A, Save("ACTIVE", "9876543210", "1234567890"), SuperUser(db), default);
            Assert.Equal(verified, db.Scalar<string>($"select to_char(verified_at, 'YYYY-MM-DD HH24:MI:SS.US') from tb_school_whatsapp_senders where school_id='{A}'"));
            await Svc(db).SaveAsync(A, Save("PENDING", "9876543210", "1234567890"), SuperUser(db), default);
            Assert.Null(db.Scalar<string?>($"select verified_at::text from tb_school_whatsapp_senders where school_id='{A}'"));
            // and the database itself still refuses an ACTIVE row without them
            Assert.Contains("chk_tb_school_whatsapp_senders_active", Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_school_whatsapp_senders set status='ACTIVE', verified_at=null where school_id='{A}'")).ToString());
        }

        [DbFact]
        public async Task NOT_REGISTERED_needs_no_number_and_the_save_records_who_and_when()
        {
            using var db = new TaxDb();
            var r = await Svc(db).SaveAsync(A, Save("NOT_REGISTERED", null), SuperUser(db), default);
            Assert.True(r.Status, r.Message);
            Assert.Null(r.Data!.WhatsAppNumber);
            Assert.Equal(SuperUser(db), db.Scalar<long>($"select updated_by from tb_school_whatsapp_senders where school_id='{A}'"));
            Assert.True(db.Scalar<bool>($"select updated_at > now() - interval '5 minutes' from tb_school_whatsapp_senders where school_id='{A}'"));
            Assert.False(string.IsNullOrWhiteSpace(r.Data.UpdatedByName));
        }

        // ------------------------------------------------------------------------------------------------ what each caller sees
        [DbFact]
        public async Task A_school_user_sees_the_status_read_only_without_the_phone_number_id_or_who_changed_it()
        {
            using var db = new TaxDb();
            await Svc(db).SaveAsync(A, Save("ACTIVE", "9876543210", "1234567890"), SuperUser(db), default);
            var school = (await Svc(db).GetAsync(A, A, false, default)).Data!;
            Assert.Equal("ACTIVE", school.Status);
            Assert.False(school.CanEdit);
            Assert.Null(school.PhoneNumberId); Assert.Null(school.UpdatedByName);
            var super = (await Svc(db).GetAsync(null, A, true, default)).Data!;
            Assert.True(super.CanEdit);
            Assert.Equal("1234567890", super.PhoneNumberId);
            Assert.NotNull(super.UpdatedByName);
        }

        // ------------------------------------------------------------------------------------------------ no credentials
        private static readonly string[] Forbidden = { "token", "secret", "password", "credential", "apikey", "bearer", "appsecret", "accesskey" };

        [Fact]
        public void No_response_or_request_model_and_no_entity_has_a_credential_field()
        {
            foreach (var type in new[] { typeof(SenderStatusDto), typeof(SaveSenderRequestDto), typeof(TbSchoolWhatsAppSender) })
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    Assert.DoesNotContain(Forbidden, f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        [DbFact]
        public async Task The_table_has_no_credential_column_and_a_posted_token_is_ignored_and_never_echoed()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from information_schema.columns where table_name='tb_school_whatsapp_senders' and (column_name ilike '%token%' or column_name ilike '%secret%' or column_name ilike '%password%' or column_name ilike '%credential%' or column_name ilike '%key%')"));
            var body = JsonSerializer.Deserialize<SaveSenderRequestDto>("{\"status\":\"PENDING\",\"whatsAppNumber\":\"9876543210\",\"accessToken\":\"EAAG-super-secret\",\"appSecret\":\"s3cr3t\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var r = await Svc(db).SaveAsync(A, body, SuperUser(db), default);
            Assert.True(r.Status, r.Message);
            var json = JsonSerializer.Serialize(r);
            Assert.DoesNotContain("EAAG", json); Assert.DoesNotContain("s3cr3t", json); Assert.DoesNotContain("oken", json.Replace("StatusCode", ""));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_school_whatsapp_senders t where t::text ilike '%EAAG%' or t::text ilike '%s3cr3t%'"));
        }
    }
}
