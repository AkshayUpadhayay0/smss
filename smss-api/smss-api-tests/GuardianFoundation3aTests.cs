using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.entity;
using smss_api_service_layer.helper;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 3a (Docs/smss-st-3a-RUN-ME.sql): the schema foundation for the guardian stage. The derived phone key (the stored mobile number is NOT rewritten), portal_access per link, ONE primary guardian per student,
    /// the guardian-to-login and merge pointers, the extra guardians of an application, the contact-change and merge audit tables, and the school's WhatsApp sender table. No behaviour changes except the one-primary rule.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the 3a script applied.
    /// </summary>
    public class GuardianFoundation3aTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;

        // ------------------------------------------------------------------------------------------------ the derived key
        [Theory]
        [InlineData("9876543210", "9876543210")]
        [InlineData(" 98765 43210 ", "9876543210")]
        [InlineData("+91 98765-43210", "9876543210")]
        [InlineData("919876543210", "9876543210")]
        [InlineData("09876543210", "9876543210")]
        [InlineData("5876543210", null)]      // an Indian mobile number starts 6 to 9
        [InlineData("98765", null)]
        [InlineData("+1 415 555 2671", null)] // another country's number is not matched by this key
        [InlineData("abc", null)]
        [InlineData("", null)]
        public void The_national_key_reads_every_usual_spelling_of_an_indian_mobile_number(string raw, string? expected) =>
            Assert.Equal(expected, PhoneNumbers.NationalKey(raw));

        [DbFact]
        public void The_database_key_function_gives_the_same_answer_as_the_code_for_every_spelling()
        {
            using var db = new TaxDb();
            foreach (var raw in new[] { "9876543210", " 98765 43210 ", "+91 98765-43210", "919876543210", "09876543210", "5876543210", "98765", "+1 415 555 2671", "abc", "", "+919000000001", "(91) 9000-000002" })
            {
                var inDb = db.Scalar<string?>($"select public.fn_phone_key('{raw}')") ;
                Assert.Equal(PhoneNumbers.NationalKey(raw), string.IsNullOrEmpty(inDb) ? null : inDb);
            }
            Assert.Equal("i", db.Scalar<string>("select provolatile::text from pg_proc where proname='fn_phone_key'"));   // IMMUTABLE: an index can use it
            Assert.Contains("fn_phone_key", db.Scalar<string>("select indexdef from pg_indexes where indexname='idx_tb_guardians_school_phone_key'"));
        }

        [DbFact]
        public void No_stored_mobile_number_was_rewritten_and_the_duplicate_key_finds_the_known_sibling_pairs()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardians where mobile_number !~ '^[6-9][0-9]{9}$'"));   // still bare 10-digit numbers
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardians where public.fn_phone_key(mobile_number) is null"));
            // the same shape as the real data: pairs of siblings sharing a parent's name and number
            Assert.Equal(6, db.Scalar<int>("select count(*) from (select school_id, public.fn_phone_key(mobile_number) from tb_guardians group by 1,2 having count(*)>1) x"));
        }

        // ------------------------------------------------------------------------------------------------ portal access and the one-primary rule
        [DbFact]
        public void Every_link_has_portal_access_on_and_a_link_made_by_the_code_without_saying_so_gets_it_on_too()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_guardians where not portal_access"));
            var guardian = db.Scalar<long>($"select min(guardian_id) from tb_guardians where school_id='{A}'");
            var student = db.Scalar<string>($"select student_id from tb_students where school_id='{A}' and student_id not in (select student_id from tb_student_guardians where guardian_id={guardian}) order by 1 limit 1");
            using var ctx = db.Ctx();
            ctx.StudentGuardians.Add(new TbStudentGuardian { StudentId = student, GuardianId = guardian, Relationship = "FATHER", IsPrimary = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            ctx.SaveChanges();   // PortalAccess is never set here
            Assert.True(db.Scalar<bool>($"select portal_access from tb_student_guardians where student_id='{student}' and guardian_id={guardian}"));
        }

        [DbFact]
        public void A_student_can_have_only_one_primary_guardian_but_any_number_of_others()
        {
            using var db = new TaxDb();
            var student = db.Scalar<string>($"select student_id from tb_students where school_id='{A}' order by 1 limit 1");
            var other = db.Scalar<long>($"select min(guardian_id) from tb_guardians where school_id='{A}' and guardian_id not in (select guardian_id from tb_student_guardians where student_id='{student}')");
            var another = db.Scalar<long>($"select max(guardian_id) from tb_guardians where school_id='{A}' and guardian_id not in (select guardian_id from tb_student_guardians where student_id='{student}')");
            var second = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary) values ('{student}', {other}, 'MOTHER', true)"));
            Assert.Contains("uq_tb_student_guardians_one_primary", second.ToString());
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary) values ('{student}', {other}, 'MOTHER', false)");
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary) values ('{student}', {another}, 'GUARDIAN', false)");
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and is_primary"));
        }

        [DbFact]
        public void The_script_stops_and_names_a_student_with_two_primaries_before_it_changes_anything()
        {
            using var db = new TaxDb();
            var student = db.Scalar<string>($"select student_id from tb_students where school_id='{A}' order by 1 limit 1");
            var other = db.Scalar<long>($"select min(guardian_id) from tb_guardians where school_id='{A}' and guardian_id not in (select guardian_id from tb_student_guardians where student_id='{student}')");
            db.Exec("drop index uq_tb_student_guardians_one_primary");   // back to the state before 3a
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary) values ('{student}', {other}, 'MOTHER', true)");
            var ex = Assert.ThrowsAny<Exception>(() => db.Exec(ScriptText()));
            Assert.Contains("PRE-CHECK FAILED", ex.ToString());
            Assert.Contains(student, ex.ToString());
            Assert.Contains("2 primaries", ex.ToString());
        }

        // ------------------------------------------------------------------------------------------------ guardian pointers
        [DbFact]
        public void One_login_per_guardian_and_a_guardian_cannot_be_merged_into_itself()
        {
            using var db = new TaxDb();
            var ids = db.Scalar<string>($"select string_agg(guardian_id::text, ',' order by guardian_id) from (select guardian_id from tb_guardians where school_id='{A}' order by 1 limit 3) x").Split(',');
            var user = db.Scalar<long>($"select min(user_id) from tb_users where school_id='{A}'");
            db.Exec($"update tb_guardians set user_id={user} where guardian_id={ids[0]}");
            var dup = Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_guardians set user_id={user} where guardian_id={ids[1]}"));
            Assert.Contains("uq_tb_guardians_user", dup.ToString());
            var self = Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_guardians set merged_into_guardian_id=guardian_id where guardian_id={ids[2]}"));
            Assert.Contains("chk_tb_guardians_merged_not_self", self.ToString());
            db.Exec($"update tb_guardians set merged_into_guardian_id={ids[0]} where guardian_id={ids[2]}");   // into another guardian is fine
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_guardians where merged_into_guardian_id is not null"));
        }

        [DbFact]
        public async Task The_new_columns_and_tables_are_readable_through_the_data_model()
        {
            using var db = new TaxDb();
            using var ctx = db.Ctx();
            Assert.Equal(0, await ctx.AdmissionGuardians.CountAsync());
            Assert.Equal(0, await ctx.GuardianChanges.CountAsync());
            Assert.Equal(0, await ctx.GuardianMerges.CountAsync());
            Assert.True(await ctx.SchoolWhatsAppSenders.CountAsync() >= 5);
            var g = await ctx.Guardians.AsNoTracking().FirstAsync();
            Assert.Null(g.UserId); Assert.Null(g.MergedIntoGuardianId);
            Assert.True((await ctx.StudentGuardians.AsNoTracking().FirstAsync()).PortalAccess);
            Assert.Null((await ctx.Admissions.AsNoTracking().FirstAsync()).GuardianId);
        }

        // ------------------------------------------------------------------------------------------------ applications reusing guardians
        [DbFact]
        public void An_application_can_name_a_reused_guardian_and_extra_ones_once_each()
        {
            using var db = new TaxDb();
            var admission = db.Scalar<long>($"select min(admission_id) from tb_admissions where school_id='{A}'");
            var g1 = db.Scalar<long>($"select min(guardian_id) from tb_guardians where school_id='{A}'");
            db.Exec($"update tb_admissions set guardian_id={g1} where admission_id={admission}");
            db.Exec($"insert into tb_admission_guardians (admission_id, guardian_id, school_id, relationship) values ({admission}, {g1}, '{A}', 'FATHER')");
            var dup = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_admission_guardians (admission_id, guardian_id, school_id) values ({admission}, {g1}, '{A}')"));
            Assert.Contains("pk_tb_admission_guardians", dup.ToString());
            var bad = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_admission_guardians (admission_id, guardian_id, school_id) values ({admission}, 999999999, '{A}')"));
            Assert.Contains("fk_tb_admission_guardians_guardian", bad.ToString());
        }

        // ------------------------------------------------------------------------------------------------ audit tables
        [DbFact]
        public void The_change_log_only_takes_the_three_contact_fields_and_the_three_sources_and_the_merge_log_needs_two_different_guardians()
        {
            using var db = new TaxDb();
            var g = db.Scalar<long>($"select min(guardian_id) from tb_guardians where school_id='{A}'");
            var h = db.Scalar<long>($"select max(guardian_id) from tb_guardians where school_id='{A}'");
            var user = db.Scalar<long>($"select min(user_id) from tb_users where school_id='{A}'");
            foreach (var source in new[] { "EDIT", "BULK", "MERGE" })
                foreach (var field in new[] { "mobile_number", "whatsapp_number", "email" })
                    db.Exec($"insert into tb_guardian_changes (school_id, guardian_id, field_name, old_value, new_value, source, changed_by) values ('{A}', {g}, '{field}', 'a', 'b', '{source}', {user})");
            Assert.Contains("chk_tb_guardian_changes_field", Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_guardian_changes (school_id, guardian_id, field_name, source) values ('{A}', {g}, 'first_name', 'EDIT')")).ToString());
            Assert.Contains("chk_tb_guardian_changes_source", Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_guardian_changes (school_id, guardian_id, field_name, source) values ('{A}', {g}, 'email', 'IMPORT')")).ToString());

            db.Exec($"insert into tb_guardian_merges (school_id, survivor_id, absorbed_id, merged_by, moved_links, copied_fields, absorbed_snapshot) values ('{A}', {g}, {h}, {user}, '[{{\"studentId\":\"S1\"}}]', '{{\"email\":\"a@b.c\"}}', '{{\"guardianId\":{h}}}')");
            Assert.Equal("S1", db.Scalar<string>("select moved_links->0->>'studentId' from tb_guardian_merges"));
            Assert.Contains("chk_tb_guardian_merges_distinct", Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_guardian_merges (school_id, survivor_id, absorbed_id, moved_links, copied_fields, absorbed_snapshot) values ('{A}', {g}, {g}, '[]', '{{}}', '{{}}')")).ToString());
        }

        // ------------------------------------------------------------------------------------------------ the school's WhatsApp sender
        [DbFact]
        public void Every_school_has_one_sender_row_starting_not_registered_and_an_active_one_needs_its_phone_number_id_and_verified_time()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_schools s where (select count(*) from tb_school_whatsapp_senders w where w.school_id=s.school_id) <> 1"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_school_whatsapp_senders where status <> 'NOT_REGISTERED'"));
            Assert.Contains("uq_tb_school_whatsapp_senders_school", Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_school_whatsapp_senders (school_id) values ('{A}')")).ToString());
            Assert.Contains("chk_tb_school_whatsapp_senders_active", Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_school_whatsapp_senders set status='ACTIVE' where school_id='{A}'")).ToString());
            Assert.Contains("chk_tb_school_whatsapp_senders_status", Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_school_whatsapp_senders set status='LIVE' where school_id='{A}'")).ToString());
            Assert.Contains("chk_tb_school_whatsapp_senders_number", Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_school_whatsapp_senders set whatsapp_number='9876543210' where school_id='{A}'")).ToString());   // stored with the country code
            db.Exec($"update tb_school_whatsapp_senders set status='PENDING', whatsapp_number='+919876543210' where school_id='{B}'");
            db.Exec($"update tb_school_whatsapp_senders set status='ACTIVE', phone_number_id='1234567890', verified_at=now(), whatsapp_number='+919876543210' where school_id='{A}'");
            Assert.Equal("ACTIVE", db.Scalar<string>($"select status from tb_school_whatsapp_senders where school_id='{A}'"));
        }

        // ------------------------------------------------------------------------------------------------ the script
        private static string ScriptText()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Docs", "smss-st-3a-RUN-ME.sql"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!.FullName, "Docs", "smss-st-3a-RUN-ME.sql"));
        }

        [DbFact]
        public void The_script_is_safe_to_run_again_and_changes_nothing_on_the_second_run()
        {
            using var db = new TaxDb();
            string Fp() => db.Scalar<string>(@"select md5(
                coalesce((select string_agg(t::text, '|' order by guardian_id) from tb_guardians t), '') || coalesce((select string_agg(t::text, '|' order by student_guardian_id) from tb_student_guardians t), '') ||
                coalesce((select string_agg(t::text, '|' order by sender_id) from tb_school_whatsapp_senders t), '') || coalesce((select string_agg(t::text, '|' order by admission_id) from tb_admissions t), ''))");
            var before = Fp();
            var script = ScriptText();
            db.Exec(script);
            Assert.Equal(before, Fp());
            db.Exec(script);
            Assert.Equal(before, Fp());
            Assert.Equal(5, db.Scalar<int>("select count(*) from tb_school_whatsapp_senders"));   // no second round of sender rows
        }

        [DbFact]
        public void A_school_that_lost_its_sender_row_gets_it_back_on_a_rerun_without_touching_the_others()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_school_whatsapp_senders set status='PENDING' where school_id='{B}'");
            db.Exec($"delete from tb_school_whatsapp_senders where school_id='{A}'");
            db.Exec(ScriptText());
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_school_whatsapp_senders where school_id='{A}'"));
            Assert.Equal("PENDING", db.Scalar<string>($"select status from tb_school_whatsapp_senders where school_id='{B}'"));   // an existing row is left as it is
        }
    }
}
