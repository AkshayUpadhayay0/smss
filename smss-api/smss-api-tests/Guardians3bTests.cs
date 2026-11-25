using System.ComponentModel.DataAnnotations;
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
    /// Stage 3b (no migration): the guardian edit endpoint, the "Complete parent details" list, the bulk "use mobile number as WhatsApp" action and the dashboard count. School comes from the token only;
    /// School Admin, Principal and Admission Staff work with it, every other role is refused. [DbFact]: needs SMSS_TEST_PG and a template with the 3a script applied.
    /// </summary>
    public class Guardians3bTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;

        private static GuardianService Svc(TaxDb db) => new(new GuardianRepository(db.Ctx()), NullLogger<GuardianService>.Instance);

        private static GuardianUpdateRequestDto Edit(string name = "Meera Rao", string mobile = "9876543210", string? whatsapp = null, bool same = true, string? email = "meera@example.com", string? occupation = "Teacher") =>
            new() { Name = name, MobileNumber = mobile, WhatsAppNumber = whatsapp, WhatsAppSameAsMobile = same, Email = email, Occupation = occupation };

        private static long Guardian(TaxDb db, int skip = 0, string school = A) =>
            db.Scalar<long>($"select guardian_id from tb_guardians where school_id='{school}' order by guardian_id offset {skip} limit 1");

        // ------------------------------------------------------------------------------------------------ who may do what
        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_School_Admin_Principal_and_Admission_Staff_may_read_edit_list_count_and_run_the_bulk_action(string role)
        {
            var ok = new[] { "School Admin", "Principal", "Admission Staff" }.Contains(role);
            var svc = Fake.Of<IGuardianService>();
            var c = Fake.WithUser(new GuardiansController(svc, Fake.Of<IGuardianMergeService>()), Fake.User(A, 5, role));
            var calls = new (string Name, Func<Task<IActionResult>> Run)[]
            {
                ("GetAsync", () => c.Get(1, default)),
                ("UpdateAsync", () => c.Update(1, Edit(), default)),
                ("ListIncompleteAsync", () => c.Incomplete(null, null, null, 1, 25, default)),
                ("CountIncompleteAsync", () => c.IncompleteCount(default)),
                ("UseMobileAsWhatsAppAsync", () => c.BulkWhatsApp(new BulkWhatsAppRequestDto { GuardianIds = new() { 1 } }, default)),
            };
            foreach (var (name, run) in calls)
            {
                var code = Fake.Code(await run());
                if (ok) Assert.NotEqual(403, code); else Assert.Equal(403, code);
                Assert.Equal(ok, Fake.ProxyOf(svc).Calls.Contains(name));
            }
        }

        [Fact]
        public async Task A_token_without_a_school_is_a_400_and_the_service_is_not_reached()
        {
            var svc = Fake.Of<IGuardianService>();
            var c = Fake.WithUser(new GuardiansController(svc, Fake.Of<IGuardianMergeService>()), Fake.User(null, 5, "School Admin"));
            Assert.Equal(400, Fake.Code(await c.Get(1, default)));
            Assert.Empty(Fake.ProxyOf(svc).Calls);
        }

        // ------------------------------------------------------------------------------------------------ the edit
        [Fact]
        public void The_request_checks_the_mobile_number_the_whatsapp_number_and_the_email()
        {
            bool Valid(GuardianUpdateRequestDto r) => Validator.TryValidateObject(r, new ValidationContext(r), new List<ValidationResult>(), true);
            Assert.True(Valid(Edit()));
            Assert.True(Valid(Edit(same: false, whatsapp: "+91 98765 43210")));
            Assert.True(Valid(Edit(same: false, whatsapp: null, email: null)));          // blank is fine here: the service decides who must have them
            Assert.False(Valid(Edit(name: "")));
            Assert.False(Valid(Edit(mobile: "12345")));
            Assert.False(Valid(Edit(mobile: "5876543210")));
            Assert.False(Valid(Edit(same: false, whatsapp: "12ab")));
            Assert.False(Valid(Edit(email: "not-an-email")));
            Assert.False(Valid(Edit(name: new string('x', 101))));
        }

        [DbFact]
        public async Task The_primary_guardian_of_an_active_student_cannot_be_saved_without_whatsapp_and_email_and_nothing_is_changed()
        {
            using var db = new TaxDb();
            var id = Guardian(db);
            var before = db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={id}");
            var noWa = await Svc(db).UpdateAsync(A, id, Edit(same: false, whatsapp: null), UserA, default);
            Assert.Equal(400, noWa.StatusCode); Assert.Contains("WhatsApp number is required", noWa.Message);
            var noMail = await Svc(db).UpdateAsync(A, id, Edit(email: null), UserA, default);
            Assert.Equal(400, noMail.StatusCode); Assert.Contains("email is required", noMail.Message);
            Assert.Equal(before, db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={id}"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_changes"));

            var ok = await Svc(db).UpdateAsync(A, id, Edit(whatsapp: "9123456789", same: false), UserA, default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("+919123456789", ok.Data!.WhatsAppNumber);
            Assert.Equal("meera@example.com", ok.Data.Email);
            Assert.Equal("Meera Rao", ok.Data.Name);
            Assert.Equal("Teacher", db.Scalar<string>($"select occupation from tb_guardians where guardian_id={id}"));
        }

        [DbFact]
        public async Task The_same_as_mobile_tick_stores_the_mobile_number_with_the_country_code()
        {
            using var db = new TaxDb();
            var id = Guardian(db);
            var r = await Svc(db).UpdateAsync(A, id, Edit(mobile: "9000000001", same: true, whatsapp: "9111111111"), UserA, default);   // the number that came with the tick is ignored
            Assert.Equal("+919000000001", r.Data!.WhatsAppNumber);
            Assert.Equal("9000000001", r.Data.MobileNumber);
        }

        [DbFact]
        public async Task A_guardian_who_is_not_the_primary_of_an_active_student_may_be_saved_without_whatsapp_and_email()
        {
            using var db = new TaxDb();
            var id = Guardian(db);
            db.Exec($"update tb_student_guardians set is_primary=false where guardian_id={id}");
            var r = await Svc(db).UpdateAsync(A, id, Edit(same: false, whatsapp: null, email: null), UserA, default);
            Assert.True(r.Status, r.Message);
            Assert.False(r.Data!.ContactRequired);
            Assert.Null(r.Data.Email); Assert.Null(r.Data.WhatsAppNumber);
            // the same guardian becomes required again as soon as it is the primary of an active student
            db.Exec($"update tb_student_guardians set is_primary=true where guardian_id={id}");
            Assert.Equal(400, (await Svc(db).UpdateAsync(A, id, Edit(same: false, whatsapp: null, email: null), UserA, default)).StatusCode);
            // and the primary of an INACTIVE student is not required either
            db.Exec($"update tb_students set status_id=(select sid from lut_status where sname='Inactive' and stype='general status') where student_id=(select student_id from tb_student_guardians where guardian_id={id} limit 1)");
            Assert.True((await Svc(db).UpdateAsync(A, id, Edit(same: false, whatsapp: null, email: null), UserA, default)).Status);
        }

        [DbFact]
        public async Task Changes_to_mobile_whatsapp_and_email_are_logged_with_who_when_old_and_new_and_nothing_else_is()
        {
            using var db = new TaxDb();
            var id = Guardian(db);
            var oldMobile = db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={id}");
            await Svc(db).UpdateAsync(A, id, Edit(mobile: "9000000002", same: true, email: "one@example.com"), UserA, default);
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={id} and source='EDIT' and changed_by={UserA} and school_id='{A}'"));
            Assert.Equal(oldMobile, db.Scalar<string>($"select old_value from tb_guardian_changes where guardian_id={id} and field_name='mobile_number'"));
            Assert.Equal("9000000002", db.Scalar<string>($"select new_value from tb_guardian_changes where guardian_id={id} and field_name='mobile_number'"));
            Assert.Null(db.Scalar<string?>($"select old_value from tb_guardian_changes where guardian_id={id} and field_name='email'") is { Length: > 0 } s ? s : null);
            Assert.Equal("one@example.com", db.Scalar<string>($"select new_value from tb_guardian_changes where guardian_id={id} and field_name='email'"));
            Assert.Equal("+919000000002", db.Scalar<string>($"select new_value from tb_guardian_changes where guardian_id={id} and field_name='whatsapp_number'"));

            // only the name and the occupation change: nothing is logged; an email change logs just the email
            await Svc(db).UpdateAsync(A, id, Edit(name: "Another Name", mobile: "9000000002", same: true, email: "one@example.com", occupation: "Doctor"), UserA, default);
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={id}"));
            await Svc(db).UpdateAsync(A, id, Edit(name: "Another Name", mobile: "9000000002", same: true, email: "two@example.com", occupation: "Doctor"), UserA, default);
            Assert.Equal(4, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={id}"));
            Assert.Equal("one@example.com", db.Scalar<string>($"select old_value from tb_guardian_changes where guardian_id={id} and field_name='email' order by change_id desc limit 1"));
        }

        [DbFact]
        public async Task A_guardian_linked_to_several_students_says_so_and_an_inactive_or_merged_guardian_cannot_be_edited()
        {
            using var db = new TaxDb();
            var id = Guardian(db);
            var other = db.Scalar<string>($"select student_id from tb_students where school_id='{A}' and student_id not in (select student_id from tb_student_guardians where guardian_id={id}) order by 1 limit 1");
            Assert.Equal(1, (await Svc(db).GetAsync(A, id, default)).Data!.LinkedStudentCount);
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary) values ('{other}', {id}, 'MOTHER', false)");
            var shared = (await Svc(db).GetAsync(A, id, default)).Data!;
            Assert.Equal(2, shared.LinkedStudentCount);
            Assert.Equal(2, shared.Students.Count);
            Assert.Equal(2, (await Svc(db).UpdateAsync(A, id, Edit(), UserA, default)).Data!.LinkedStudentCount);   // the edit answer carries it too

            var gone = Guardian(db, 5);
            db.Exec($"update tb_guardians set merged_into_guardian_id={id}, status_id=(select sid from lut_status where sname='Inactive' and stype='general status') where guardian_id={gone}");
            var r = await Svc(db).UpdateAsync(A, gone, Edit(), UserA, default);
            Assert.Equal(409, r.StatusCode); Assert.Contains("merged", r.Message);
            Assert.False((await Svc(db).GetAsync(A, gone, default)).Data!.IsActive);
        }

        // ------------------------------------------------------------------------------------------------ other schools
        [DbFact]
        public async Task Another_schools_guardian_is_a_404_for_read_edit_and_bulk_and_never_in_the_list_or_count()
        {
            using var db = new TaxDb();
            var mine = Guardian(db);
            var theirs = Guardian(db, 0, B);
            var before = db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={theirs}");
            Assert.Equal(404, (await Svc(db).GetAsync(A, theirs, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).UpdateAsync(A, theirs, Edit(), UserA, default)).StatusCode);
            var bulk = await Svc(db).UseMobileAsWhatsAppAsync(A, new[] { mine, theirs }, UserA, default);
            Assert.Equal(404, bulk.StatusCode);
            Assert.Equal(before, db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={theirs}"));
            Assert.Null(db.Scalar<string?>($"select whatsapp_number from tb_guardians where guardian_id={mine}"));   // all or nothing: mine was not touched either
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_changes"));

            var list = (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 100, default)).Data!;
            Assert.DoesNotContain(theirs, list.Items.Select(i => i.GuardianId));
            Assert.Equal(db.Scalar<int>($"select count(distinct g.guardian_id) from tb_guardians g where g.school_id='{A}'"), (await Svc(db).CountIncompleteAsync(A, default)).Data);
            Assert.Equal(list.TotalIncomplete, (await Svc(db).CountIncompleteAsync(A, default)).Data);
            Assert.Equal(404, (await Svc(db).GetAsync(B, mine, default)).StatusCode);   // and the other way round
        }

        // ------------------------------------------------------------------------------------------------ the list
        [DbFact]
        public async Task The_list_holds_primary_guardians_of_active_students_lacking_whatsapp_or_email_and_a_completed_one_leaves_it()
        {
            using var db = new TaxDb();
            var all = db.Scalar<int>($"select count(*) from tb_guardians where school_id='{A}'");
            var first = (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 25, default)).Data!;
            Assert.Equal(all, first.Total);
            Assert.Equal(25, first.Items.Count);
            Assert.All(first.Items, i => Assert.Equal(new[] { "WhatsApp", "Email" }, i.Missing));

            // paging: the next page continues, the last page is short, and a page size is capped
            var page2 = (await Svc(db).ListIncompleteAsync(A, null, null, null, 2, 25, default)).Data!;
            Assert.Empty(page2.Items.Select(i => i.GuardianId).Intersect(first.Items.Select(i => i.GuardianId)));
            Assert.Equal(all - 25 * ((all - 1) / 25), (await Svc(db).ListIncompleteAsync(A, null, null, null, (all + 24) / 25, 25, default)).Data!.Items.Count);
            Assert.Equal(100, (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 5000, default)).Data!.PageSize);

            var done = first.Items[0].GuardianId;
            db.Exec($"update tb_guardians set whatsapp_number='+919876500001', email='d@example.com' where guardian_id={done}");
            Assert.Equal(all - 1, (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 25, default)).Data!.Total);
            db.Exec($"update tb_guardians set email='d@example.com' where guardian_id={first.Items[1].GuardianId}");
            var partly = (await Svc(db).ListIncompleteAsync(A, null, null, "", 1, 100, default)).Data!.Items.First(i => i.GuardianId == first.Items[1].GuardianId);
            Assert.Equal(new[] { "WhatsApp" }, partly.Missing);   // only what is missing is flagged

            // a guardian whose student is NOT ENROLLED in the current-flagged year (passed out, left) and a guardian who is not primary are not in it
            // (Stage 4b-4, previous decision: "active student"; new decision: enrolled in the current-flagged year, so a student who passed out stays on no list)
            var inactive = first.Items[2];
            db.Exec($"delete from tb_student_enrollments where student_id in (select student_id from tb_student_guardians where guardian_id={inactive.GuardianId} and is_primary) and academic_year_id=(select academic_year_id from tb_academic_years where school_id='{A}' and is_current)");
            var nonPrimary = first.Items[3].GuardianId;
            db.Exec($"update tb_student_guardians set is_primary=false where guardian_id={nonPrimary}");
            var ids = (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 100, default)).Data!.Items.Select(i => i.GuardianId).ToList();
            Assert.DoesNotContain(inactive.GuardianId, ids); Assert.DoesNotContain(nonPrimary, ids);
        }

        [DbFact]
        public async Task Class_and_section_come_from_the_enrolment_in_the_current_flagged_year_and_a_student_not_enrolled_in_it_never_matches_a_filter()
        {
            using var db = new TaxDb();
            var current = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{A}' and is_current limit 1");
            var list = (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 100, default)).Data!;
            Assert.Equal(current, list.ViewedYearId);
            var withClass = list.Items.First(i => i.Students[0].SectionId != null);
            var student = withClass.Students[0];
            var expectedSection = db.Scalar<string>($"select s.section_name from tb_student_enrollments e join tb_sections s on s.section_id=e.class_section_id where e.student_id='{student.StudentId}' and e.academic_year_id={current}");
            Assert.Equal(expectedSection, student.SectionName);

            var bySection = (await Svc(db).ListIncompleteAsync(A, null, student.SectionId, null, 1, 100, default)).Data!;
            Assert.Contains(withClass.GuardianId, bySection.Items.Select(i => i.GuardianId));
            Assert.All(bySection.Items, i => Assert.Contains(i.Students, s => s.SectionId == student.SectionId));
            Assert.Equal(list.TotalIncomplete, bySection.TotalIncomplete);   // the tile number ignores filters
            var byClass = (await Svc(db).ListIncompleteAsync(A, student.ClassId, null, null, 1, 100, default)).Data!;
            Assert.All(byClass.Items, i => Assert.Contains(i.Students, s => s.ClassId == student.ClassId));

            // the student has no enrolment in the current year (Stage 4b-4: passed out or left, or not yet promoted): the student is no longer on the list, and no class filter finds them
            db.Exec($"delete from tb_student_enrollments where student_id='{student.StudentId}' and academic_year_id={current}");
            var afterAll = (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 100, default)).Data!;
            Assert.DoesNotContain(afterAll.Items.SelectMany(i => i.Students), s => s.StudentId == student.StudentId);
            Assert.DoesNotContain(withClass.GuardianId, (await Svc(db).ListIncompleteAsync(A, student.ClassId, null, null, 1, 100, default)).Data!.Items.Where(i => i.Students.Any(s => s.StudentId == student.StudentId)).Select(i => i.GuardianId));
        }

        [DbFact]
        public async Task The_search_finds_a_parent_by_name_a_student_by_name_or_id_and_a_number_by_its_digits()
        {
            using var db = new TaxDb();
            var id = Guardian(db, 3);
            db.Exec($"update tb_guardians set first_name='Zebulon Quimby', mobile_number='9012345678' where guardian_id={id}");
            var student = db.Scalar<string>($"select student_id from tb_student_guardians where guardian_id={id}");
            Assert.Equal(new[] { id }, (await Svc(db).ListIncompleteAsync(A, null, null, "quimby", 1, 25, default)).Data!.Items.Select(i => i.GuardianId));
            Assert.Equal(new[] { id }, (await Svc(db).ListIncompleteAsync(A, null, null, "90123", 1, 25, default)).Data!.Items.Select(i => i.GuardianId));
            Assert.Equal(new[] { id }, (await Svc(db).ListIncompleteAsync(A, null, null, student, 1, 25, default)).Data!.Items.Select(i => i.GuardianId));
            Assert.Empty((await Svc(db).ListIncompleteAsync(A, null, null, "no-such-person", 1, 25, default)).Data!.Items);
        }

        // ------------------------------------------------------------------------------------------------ the bulk action
        [DbFact]
        public async Task The_bulk_action_changes_only_the_selected_skips_those_with_whatsapp_never_touches_email_and_logs_every_change_as_BULK()
        {
            using var db = new TaxDb();
            var picked = new[] { Guardian(db, 0), Guardian(db, 1), Guardian(db, 2) };
            var notPicked = Guardian(db, 3);
            db.Exec($"update tb_guardians set whatsapp_number='+919800000001' where guardian_id={picked[1]}");   // already has one
            db.Exec($"update tb_guardians set email='keep@example.com' where guardian_id={picked[0]}");
            var untouched = db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={notPicked}");
            var skippedBefore = db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={picked[1]}");

            var r = await Svc(db).UseMobileAsWhatsAppAsync(A, new[] { picked[0], picked[1], picked[2], picked[0] }, UserA, default);   // a repeated id counts once
            Assert.True(r.Status, r.Message);
            Assert.Equal((3, 2, 1), (r.Data!.Selected, r.Data.Updated, r.Data.Skipped));
            foreach (var id in new[] { picked[0], picked[2] })
                Assert.Equal("+91" + db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={id}"), db.Scalar<string>($"select whatsapp_number from tb_guardians where guardian_id={id}"));
            Assert.Equal(skippedBefore, db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={picked[1]}"));   // the one with WhatsApp is exactly as it was
            Assert.Equal(untouched, db.Scalar<string>($"select md5(g::text) from tb_guardians g where guardian_id={notPicked}"));
            Assert.Equal("keep@example.com", db.Scalar<string>($"select email from tb_guardians where guardian_id={picked[0]}"));
            Assert.Null(db.Scalar<string?>($"select email from tb_guardians where guardian_id={picked[2]}") is { Length: > 0 } e ? e : null);   // never filled in

            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_guardian_changes"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_guardian_changes where source='BULK' and field_name='whatsapp_number' and changed_by={UserA} and old_value is null and new_value like '+91%'"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_changes where field_name='email'"));

            var again = await Svc(db).UseMobileAsWhatsAppAsync(A, picked, UserA, default);   // everyone already has one now
            Assert.Equal((0, 3), (again.Data!.Updated, again.Data.Skipped));
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_guardian_changes"));
        }

        [DbFact]
        public async Task The_bulk_action_is_all_or_nothing_for_an_unknown_inactive_or_invalid_guardian_and_for_a_database_failure()
        {
            using var db = new TaxDb();
            var a = Guardian(db, 0); var b = Guardian(db, 1); var c = Guardian(db, 2);
            string Snap() => db.Scalar<string>($"select md5(string_agg(g::text, '|' order by guardian_id)) from tb_guardians g where guardian_id in ({a},{b},{c})");
            var before = Snap();

            Assert.Equal(404, (await Svc(db).UseMobileAsWhatsAppAsync(A, new[] { a, b, 999999999L }, UserA, default)).StatusCode);
            db.Exec($"update tb_guardians set status_id=(select sid from lut_status where sname='Inactive' and stype='general status') where guardian_id={c}");
            var inactive = await Svc(db).UseMobileAsWhatsAppAsync(A, new[] { a, b, c }, UserA, default);
            Assert.Equal(409, inactive.StatusCode);
            db.Exec($"update tb_guardians set status_id=(select sid from lut_status where sname='Active' and stype='general status') where guardian_id={c}");
            db.Exec($"update tb_guardians set mobile_number='not a number' where guardian_id={b}");
            var invalid = await Svc(db).UseMobileAsWhatsAppAsync(A, new[] { a, b, c }, UserA, default);
            Assert.Equal(400, invalid.StatusCode); Assert.Contains("not a valid number", invalid.Message);
            db.Exec($"update tb_guardians set mobile_number='9000000009' where guardian_id={b}");
            before = Snap();

            // a database failure part-way (the user id does not exist, so the change-log insert is refused): the guardian updates are rolled back too
            var broken = await Svc(db).UseMobileAsWhatsAppAsync(A, new[] { a, b, c }, 987654321, default);
            Assert.Equal(500, broken.StatusCode);
            Assert.Equal(before, Snap());
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_changes"));

            // limits
            Assert.Equal(400, (await Svc(db).UseMobileAsWhatsAppAsync(A, Array.Empty<long>(), UserA, default)).StatusCode);
            Assert.Equal(400, (await Svc(db).UseMobileAsWhatsAppAsync(A, Enumerable.Range(1, GuardianService.MaxBulk + 1).Select(i => (long)i).ToList(), UserA, default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ the dashboard tile
        [DbFact]
        public async Task The_School_Admin_tile_counts_guardians_to_complete_and_says_unavailable_never_zero_when_it_cannot()
        {
            using var db = new TaxDb();
            var c = db.Ctx();
            SchoolAdminDashboardService Make(IGuardianService? g) => new(new SchoolAdminDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<SchoolAdminDashboardService>.Instance, null, g);
            var expected = (await Svc(db).CountIncompleteAsync(A, default)).Data;
            Assert.True(expected > 0);
            Assert.Equal(expected, (await Make(Svc(db)).GetAsync(A, default)).Data!.ParentDetailsToComplete);
            db.Exec($"update tb_guardians set whatsapp_number='+919876500001', email='d@example.com' where guardian_id={Guardian(db)}");
            Assert.Equal(expected - 1, (await Make(Svc(db)).GetAsync(A, default)).Data!.ParentDetailsToComplete);   // completing one lowers it
            Assert.Null((await Make(Fake.Of<IGuardianService>()).GetAsync(A, default)).Data!.ParentDetailsToComplete);   // the stand-in answers 500
        }

        [DbFact]
        public async Task A_guardian_of_several_siblings_counts_once_in_the_tile()
        {
            using var db = new TaxDb();
            var id = Guardian(db);
            var before = (await Svc(db).CountIncompleteAsync(A, default)).Data;
            var other = db.Scalar<string>($"select student_id from tb_students where school_id='{A}' and student_id not in (select student_id from tb_student_guardians where guardian_id={id}) order by 1 limit 1");
            db.Exec($"update tb_student_guardians set is_primary=false where student_id='{other}'");
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary) values ('{other}', {id}, 'MOTHER', true)");
            Assert.Equal(before - 1, (await Svc(db).CountIncompleteAsync(A, default)).Data);   // the other student's old guardian is no longer primary there; this guardian counts once for two students
            var row = (await Svc(db).ListIncompleteAsync(A, null, null, null, 1, 100, default)).Data!.Items.Single(i => i.GuardianId == id);
            Assert.Equal(2, row.Students.Count);
        }
    }
}
