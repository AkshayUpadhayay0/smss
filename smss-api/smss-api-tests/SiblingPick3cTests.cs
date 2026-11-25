using System.Text.Json;
using ClosedXML.Excel;
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
    /// Stage 3c (no migration): the sibling pick and guardian reuse. A sibling's parents are LINKED to the new student, never copied: the search masks mobile numbers and is limited to this school's active students;
    /// an application (and Add Existing Student) can name an existing primary guardian and extra guardians; at Confirm the guardian rows are locked FOR UPDATE, a merged guardian is replaced by its survivor, an inactive
    /// one is refused, what the admin entered for WhatsApp and email is written to the shared row and logged, and there is exactly one primary guardian per student. [DbFact]: needs SMSS_TEST_PG and a template with 3a.
    /// </summary>
    public class SiblingPick3cTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long Y = TaxDb.YearA;
        private const long UserA = TaxDb.UserA;

        private static AdmissionService Admissions(TaxDb db) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);
        private static StudentOnboardingService Onboarding(TaxDb db) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<StudentOnboardingService>.Instance);
        private static GuardianService Guardians(TaxDb db) => new(new GuardianRepository(db.Ctx()), NullLogger<GuardianService>.Instance);

        private static long Guardian(TaxDb db, int skip = 0, string school = A) =>
            db.Scalar<long>($"select guardian_id from tb_guardians where school_id='{school}' order by guardian_id offset {skip} limit 1");
        private static long Section(TaxDb db) => db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
        private static string Status(TaxDb db, long id) => db.Scalar<string>($"select s.sname from tb_admissions a join lut_status s on s.sid=a.status_id where a.admission_id={id}");
        private static string Inactive => "(select sid from lut_status where sname='Inactive' and stype='general status')";

        private static SubmitAdmissionRequestDto Request(TaxDb db, string first, long? primary = null, string? email = "p@example.com", string? whatsapp = null, bool same = true, params (long Id, string? Relationship)[] extras) => new()
        {
            AcademicYearId = Y, AppliedClassId = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{A}'"), AdmissionTypeId = db.Scalar<long>("select min(admission_type_id) from lut_admission_type"),
            ApplicantFirstName = first, DateOfBirth = new DateTime(2018, 3, 3),
            ExistingGuardianId = primary,
            GuardianName = primary == null ? "Typed Parent" : "ignored when an existing guardian is picked", GuardianRelationship = "MOTHER", GuardianMobileNumber = primary == null ? "9876543210" : "9000000000",
            GuardianEmail = email, GuardianWhatsAppNumber = whatsapp, GuardianWhatsAppSameAsMobile = same,
            AdditionalGuardians = extras.Select(e => new ExistingGuardianPickDto { GuardianId = e.Id, Relationship = e.Relationship }).ToList(),
        };

        private static async Task<long> Added(TaxDb db, SubmitAdmissionRequestDto req)
        {
            var r = await Admissions(db).SubmitAsync(A, req, default);
            Assert.True(r.Status, r.Message);
            return r.Data!.AdmissionId;
        }

        private static Task<ApiResponse<AdmissionDetailDto>> Confirm(TaxDb db, long id) =>
            Admissions(db).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);

        // ------------------------------------------------------------------------------------------------ the search
        [DbFact]
        public async Task The_search_returns_only_active_students_of_this_school_with_masked_numbers_and_never_a_whole_mobile_number()
        {
            using var db = new TaxDb();
            var cls = db.Scalar<long>($"select c.class_id from tb_student_enrollments e join tb_sections s on s.section_id=e.class_section_id join tb_classes c on c.class_id=s.class_id where e.academic_year_id={Y} and e.school_id='{A}' group by c.class_id order by count(*) desc limit 1");
            var r = await Guardians(db).SearchSiblingsAsync(A, cls, null, 100, default);   // 100 asked, 30 is the cap
            Assert.True(r.Status, r.Message);
            Assert.NotEmpty(r.Data!);
            Assert.True(r.Data!.Count <= GuardianService.MaxSiblingResults);
            var json = JsonSerializer.Serialize(r.Data);
            foreach (var number in db.Scalar<string>($"select string_agg(mobile_number, ',') from tb_guardians where school_id='{A}'").Split(','))
                Assert.DoesNotContain(number, json);   // no whole mobile number anywhere in the answer
            Assert.All(r.Data.SelectMany(s => s.Guardians), g => Assert.Matches(@"^\d\dX+\d\d$", g.MaskedMobile));
            Assert.All(r.Data, s => Assert.NotEmpty(s.Guardians));
            Assert.Equal(r.Data.Count, r.Data.Select(s => s.StudentId).Distinct().Count());
            var ids = r.Data.Select(s => s.StudentId).ToList();
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_students where student_id in ('{string.Join("','", ids)}') and school_id<>'{A}'"));

            // another school's family never appears, whatever is typed
            var other = db.Scalar<string>($"select first_name from tb_students where school_id='{B}' order by student_id limit 1");
            var sameSchool = db.Scalar<int>($"select count(*) from tb_students where school_id='{A}' and first_name ilike '{other}'");
            var byName = (await Guardians(db).SearchSiblingsAsync(A, null, other, 30, default)).Data!;
            Assert.Equal(sameSchool, byName.Count(s => s.StudentName.StartsWith(other, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(db.Scalar<string>($"select student_id from tb_students where school_id='{B}' order by student_id limit 1"), byName.Select(s => s.StudentId));
        }

        [DbFact]
        public async Task The_search_needs_a_class_or_two_letters_skips_inactive_students_and_inactive_or_merged_guardians()
        {
            using var db = new TaxDb();
            Assert.Equal(400, (await Guardians(db).SearchSiblingsAsync(A, null, null, 20, default)).StatusCode);
            Assert.Equal(400, (await Guardians(db).SearchSiblingsAsync(A, null, "a", 20, default)).StatusCode);
            var first = (await Guardians(db).SearchSiblingsAsync(A, null, "ar", 30, default)).Data!;
            Assert.NotEmpty(first);
            var student = first[0];
            db.Exec($"update tb_students set status_id={Inactive} where student_id='{student.StudentId}'");
            Assert.DoesNotContain(student.StudentId, (await Guardians(db).SearchSiblingsAsync(A, null, "ar", 30, default)).Data!.Select(s => s.StudentId));
            var second = (await Guardians(db).SearchSiblingsAsync(A, null, "ar", 30, default)).Data![0];
            var gid = second.Guardians[0].GuardianId;
            db.Exec($"update tb_guardians set status_id={Inactive} where guardian_id={gid}");
            var again = (await Guardians(db).SearchSiblingsAsync(A, null, "ar", 30, default)).Data!;
            Assert.DoesNotContain(gid, again.SelectMany(s => s.Guardians).Select(g => g.GuardianId));
        }

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_School_Admin_Principal_and_Admission_Staff_may_use_the_sibling_search(string role)
        {
            var ok = new[] { "School Admin", "Principal", "Admission Staff" }.Contains(role);
            var svc = Fake.Of<IGuardianService>();
            var c = Fake.WithUser(new GuardiansController(svc, Fake.Of<IGuardianMergeService>()), Fake.User(A, 5, role));
            var code = Fake.Code(await c.Siblings(1, null, 20, default));
            if (ok) Assert.NotEqual(403, code); else Assert.Equal(403, code);
            Assert.Equal(ok, Fake.ProxyOf(svc).Calls.Contains("SearchSiblingsAsync"));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        // ------------------------------------------------------------------------------------------------ the application
        [DbFact]
        public async Task An_application_stores_the_picked_primary_and_extra_guardians_and_copies_name_and_mobile_only_for_display()
        {
            using var db = new TaxDb();
            var primary = Guardian(db, 0); var extra = Guardian(db, 1);
            var req = Request(db, "Sib", primary, email: "shared@example.com", extras: new[] { (extra, (string?)"FATHER") });
            var r = await Admissions(db).SubmitAsync(A, req, default);
            Assert.True(r.Status, r.Message);
            var d = r.Data!;
            Assert.Equal(primary, d.GuardianId);
            Assert.Equal(db.Scalar<string>($"select first_name from tb_guardians where guardian_id={primary}"), d.GuardianName);
            Assert.Equal(db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={primary}"), db.Scalar<string>($"select guardian_mobile_number from tb_admissions where admission_id={d.AdmissionId}"));
            Assert.Equal("shared@example.com", d.GuardianEmail);
            Assert.NotNull(d.ExistingGuardian); Assert.Matches(@"^\d\dX+\d\d$", d.ExistingGuardian!.MaskedMobile);
            Assert.Equal(new[] { extra }, d.AdditionalGuardians.Select(g => g.GuardianId));
            Assert.Equal("FATHER", db.Scalar<string>($"select relationship from tb_admission_guardians where admission_id={d.AdmissionId} and guardian_id={extra}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_guardians where school_id='{A}' and guardian_id > {db.Scalar<long>($"select max(guardian_id) from tb_guardians where school_id='{A}'")}"));
        }

        [DbFact]
        public async Task A_guardian_without_whatsapp_or_email_needs_them_entered_and_a_guardian_that_has_them_does_not()
        {
            using var db = new TaxDb();
            var bare = Guardian(db, 0); var full = Guardian(db, 1);
            db.Exec($"update tb_guardians set whatsapp_number='+919800000000', email='have@example.com' where guardian_id={full}");
            var noMail = await Admissions(db).SubmitAsync(A, Request(db, "NoMail", bare, email: null), default);
            Assert.Equal(400, noMail.StatusCode); Assert.Contains("email is required", noMail.Message);
            var noWa = await Admissions(db).SubmitAsync(A, Request(db, "NoWa", bare, same: false, whatsapp: null), default);
            Assert.Equal(400, noWa.StatusCode); Assert.Contains("WhatsApp number is required", noWa.Message);
            var ok = await Admissions(db).SubmitAsync(A, Request(db, "Have", full, email: null, same: false, whatsapp: null), default);   // nothing to enter: the record has both
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("have@example.com", ok.Data!.GuardianEmail);
            Assert.Equal("+919800000000", ok.Data.GuardianWhatsAppNumber);
            Assert.True(ok.Data.ExistingGuardian!.HasEmail && ok.Data.ExistingGuardian.HasWhatsApp);
        }

        [DbFact]
        public async Task A_guardian_of_another_school_is_a_404_and_nothing_is_stored()
        {
            using var db = new TaxDb();
            var theirs = Guardian(db, 0, B); var mine = Guardian(db, 0);
            var applications = db.Scalar<int>("select count(*) from tb_admissions");
            Assert.Equal(404, (await Admissions(db).SubmitAsync(A, Request(db, "X", theirs), default)).StatusCode);
            Assert.Equal(404, (await Admissions(db).SubmitAsync(A, Request(db, "Y", null, extras: new[] { (theirs, (string?)null) }), default)).StatusCode);
            Assert.Equal(404, (await Admissions(db).SubmitAsync(A, Request(db, "Z", mine, extras: new[] { (theirs, (string?)null) }), default)).StatusCode);
            Assert.Equal(404, (await Admissions(db).SubmitAsync(A, Request(db, "W", 999999999), default)).StatusCode);
            Assert.Equal(applications, db.Scalar<int>("select count(*) from tb_admissions"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_admission_guardians"));
        }

        [DbFact]
        public async Task The_pick_can_be_changed_by_editing_the_application_before_it_is_confirmed()
        {
            using var db = new TaxDb();
            var g1 = Guardian(db, 0); var g2 = Guardian(db, 1); var g3 = Guardian(db, 2);
            var id = await Added(db, Request(db, "Edit", g1, extras: new[] { (g2, (string?)"FATHER") }));
            var edit = Request(db, "Edit", g3, extras: new[] { (g1, (string?)"MOTHER") });
            var r = await Admissions(db).UpdateAsync(A, id, edit, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(g3, r.Data!.GuardianId);
            Assert.Equal(new[] { g1 }, r.Data.AdditionalGuardians.Select(g => g.GuardianId));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_admission_guardians where admission_id={id}"));
            Assert.Equal("MOTHER", db.Scalar<string>($"select relationship from tb_admission_guardians where admission_id={id} and guardian_id={g1}"));

            // back to a typed-in person: nothing stays reused
            var typed = await Admissions(db).UpdateAsync(A, id, Request(db, "Edit"), default);
            Assert.True(typed.Status, typed.Message);
            Assert.Null(typed.Data!.GuardianId); Assert.Empty(typed.Data.AdditionalGuardians);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_admission_guardians where admission_id={id}"));
            Assert.Equal("Typed Parent", typed.Data.GuardianName);
        }

        // ------------------------------------------------------------------------------------------------ Confirm
        [DbFact]
        public async Task Confirm_links_the_existing_guardians_without_copying_them_and_writes_the_entered_contact_to_the_shared_row_with_a_log()
        {
            using var db = new TaxDb();
            var primary = Guardian(db, 0); var extra = Guardian(db, 1);
            var guardians = db.Scalar<int>("select count(*) from tb_guardians");
            var users = db.Scalar<int>("select count(*) from tb_users");
            var id = await Added(db, Request(db, "Linked", primary, email: "family@example.com", extras: new[] { (extra, (string?)"FATHER") }));
            var r = await Confirm(db, id);
            Assert.True(r.Status, r.Message);
            var student = r.Data!.StudentId!;

            Assert.Equal(guardians, db.Scalar<int>("select count(*) from tb_guardians"));   // linked, never copied
            Assert.Equal(users, db.Scalar<int>("select count(*) from tb_users"));          // Stage 2 and 3c create no login and send no message
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and is_primary"));
            Assert.Equal(primary, db.Scalar<long>($"select guardian_id from tb_student_guardians where student_id='{student}' and is_primary"));
            Assert.Equal("FATHER", db.Scalar<string>($"select relationship from tb_student_guardians where student_id='{student}' and guardian_id={extra}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and not portal_access"));
            Assert.Equal("family@example.com", db.Scalar<string>($"select email from tb_guardians where guardian_id={primary}"));
            Assert.Equal("+91" + db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={primary}"), db.Scalar<string>($"select whatsapp_number from tb_guardians where guardian_id={primary}"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={primary} and source='EDIT' and changed_by={UserA} and old_value is null"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={extra}"));   // an extra guardian is only linked
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_guardian_changes where field_name='mobile_number'"));   // the stored mobile is never changed here
        }

        [DbFact]
        public async Task A_value_already_on_the_guardian_record_is_never_overwritten_and_not_logged_again()
        {
            using var db = new TaxDb();
            var g = Guardian(db, 0);
            db.Exec($"update tb_guardians set whatsapp_number='+919800000001', email='old@example.com' where guardian_id={g}");
            var id = await Added(db, Request(db, "Keep", g, email: null, same: false));
            db.Exec($"update tb_admissions set guardian_email='other@example.com', guardian_whatsapp_number='+919800000002' where admission_id={id}");   // as if the application held different values
            Assert.True((await Confirm(db, id)).Status);
            Assert.Equal("old@example.com", db.Scalar<string>($"select email from tb_guardians where guardian_id={g}"));
            Assert.Equal("+919800000001", db.Scalar<string>($"select whatsapp_number from tb_guardians where guardian_id={g}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={g}"));
        }

        [DbFact]
        public async Task The_stage_2_rule_is_checked_on_the_guardian_record_whether_it_is_new_or_reused()
        {
            using var db = new TaxDb();
            var g = Guardian(db, 0);
            var id = await Added(db, Request(db, "Rule", g));
            db.Exec($"update tb_admissions set guardian_email=null where admission_id={id}");   // the record has no email and the application has none either
            var students = db.Scalar<int>($"select count(*) from tb_students where school_id='{A}'");
            var r = await Confirm(db, id);
            Assert.Equal(409, r.StatusCode); Assert.Contains("email", r.Message); Assert.Contains("missing", r.Message);
            Assert.Equal(students, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal("APPLICATION_SUBMITTED", Status(db, id));
            // the record gets an email some other way (the edit screen): Confirm now passes without the application having it
            db.Exec($"update tb_guardians set email='later@example.com' where guardian_id={g}");
            Assert.True((await Confirm(db, id)).Status);
        }

        [DbFact]
        public async Task A_guardian_merged_into_another_is_followed_to_the_survivor_at_submit_and_at_confirm()
        {
            using var db = new TaxDb();
            var old = Guardian(db, 0); var survivor = Guardian(db, 1); var later = Guardian(db, 2); var laterSurvivor = Guardian(db, 3);
            db.Exec($"update tb_guardians set merged_into_guardian_id={survivor}, status_id={Inactive} where guardian_id={old}");
            var picked = await Admissions(db).SubmitAsync(A, Request(db, "Followed", old), default);   // picking the merged one stores the survivor
            Assert.True(picked.Status, picked.Message);
            Assert.Equal(survivor, picked.Data!.GuardianId);

            var id = await Added(db, Request(db, "Later", later));   // merged AFTER the application was made
            db.Exec($"update tb_guardians set merged_into_guardian_id={laterSurvivor}, status_id={Inactive} where guardian_id={later}");
            var r = await Confirm(db, id);
            Assert.True(r.Status, r.Message);
            Assert.Equal(laterSurvivor, db.Scalar<long>($"select guardian_id from tb_student_guardians where student_id='{r.Data!.StudentId}' and is_primary"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={later}"));   // the merged guardian keeps only its own old link: never linked to anyone new
        }

        [DbFact]
        public async Task An_inactive_guardian_is_refused_with_a_clear_message_at_submit_and_at_confirm_and_nothing_is_created()
        {
            using var db = new TaxDb();
            var g = Guardian(db, 0); var extra = Guardian(db, 1);
            var name = db.Scalar<string>($"select first_name from tb_guardians where guardian_id={g}");
            var id = await Added(db, Request(db, "Soon", g, extras: new[] { (extra, (string?)null) }));
            db.Exec($"update tb_guardians set status_id={Inactive} where guardian_id={extra}");
            var students = db.Scalar<int>($"select count(*) from tb_students where school_id='{A}'");
            var r = await Confirm(db, id);
            Assert.Equal(409, r.StatusCode); Assert.Contains("inactive", r.Message); Assert.Contains("choose another guardian", r.Message);
            Assert.Equal(students, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal("APPLICATION_SUBMITTED", Status(db, id));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_changes"));   // nothing was half-applied

            db.Exec($"update tb_guardians set status_id={Inactive} where guardian_id={g}");
            var submit = await Admissions(db).SubmitAsync(A, Request(db, "New", g), default);
            Assert.Equal(409, submit.StatusCode); Assert.Contains(name, submit.Message);
        }

        [DbFact]
        public async Task A_reused_primary_guardian_still_gives_exactly_one_primary_per_student_even_when_a_typed_primary_has_extras()
        {
            using var db = new TaxDb();
            var extra = Guardian(db, 1);
            var id = await Added(db, Request(db, "TypedPrimary", null, extras: new[] { (extra, (string?)"FATHER") }));   // a person typed in + an existing guardian
            var guardians = db.Scalar<int>("select count(*) from tb_guardians");
            var r = await Confirm(db, id);
            Assert.True(r.Status, r.Message);
            var student = r.Data!.StudentId!;
            Assert.Equal(guardians + 1, db.Scalar<int>("select count(*) from tb_guardians"));   // the typed person is a NEW row
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and is_primary"));
            Assert.False(db.Scalar<bool>($"select is_primary from tb_student_guardians where student_id='{student}' and guardian_id={extra}"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}'"));
        }

        [DbFact]
        public async Task Two_siblings_confirmed_at_the_same_moment_with_the_same_guardian_leave_one_guardian_row_two_links_and_no_duplicate_or_lost_update()
        {
            using var db = new TaxDb();
            var g = Guardian(db, 0);
            var a1 = await Added(db, Request(db, "SibOne", g, email: "first@example.com"));
            var a2 = await Added(db, Request(db, "SibTwo", g, email: "second@example.com"));
            var guardians = db.Scalar<int>("select count(*) from tb_guardians");
            var results = await Task.WhenAll(Task.Run(() => Confirm(db, a1)), Task.Run(() => Confirm(db, a2)));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(guardians, db.Scalar<int>("select count(*) from tb_guardians"));
            // the guardian's own child plus the two new siblings: three links to the SAME guardian row, one per student, primary for each
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={g}"));
            Assert.Equal(3, db.Scalar<int>($"select count(distinct student_id) from tb_student_guardians where guardian_id={g}"));
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={g} and is_primary"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from (select student_id from tb_student_guardians where is_primary group by 1 having count(*) > 1) x"));   // never two primaries for one student
            // the shared record got its email and WhatsApp ONCE (the second confirmation waited for the lock, saw them and wrote nothing): two log rows, not four
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={g}"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={g} and field_name='email'"));
            Assert.Contains(db.Scalar<string>($"select email from tb_guardians where guardian_id={g}"), new[] { "first@example.com", "second@example.com" });
            Assert.NotNull(db.Scalar<string?>($"select whatsapp_number from tb_guardians where guardian_id={g}"));
        }

        // ------------------------------------------------------------------------------------------------ the five open applications
        [DbFact]
        public async Task The_open_applications_of_the_real_data_still_work_they_have_no_guardian_link_and_confirm_as_before_once_completed()
        {
            using var db = new TaxDb();
            var open = db.Scalar<long>($"select min(a.admission_id) from tb_admissions a join lut_status s on s.sid=a.status_id where a.school_id in ('{A}','{B}','LAS261103001') and s.sname='APPLICATION_SUBMITTED' and a.guardian_id is null");
            var school = db.Scalar<string>($"select school_id from tb_admissions where admission_id={open}");
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_admission_guardians where admission_id={open}"));
            var before = db.Scalar<int>("select count(*) from tb_guardians");
            var detail = await Admissions(db).GetByIdAsync(school, open, default);
            Assert.True(detail.Status, detail.Message);
            Assert.Null(detail.Data!.GuardianId); Assert.Null(detail.Data.ExistingGuardian); Assert.Empty(detail.Data.AdditionalGuardians);
            // an incomplete one is refused by the Stage 2 rule exactly as before
            db.Exec($"update tb_admissions set guardian_email=null, guardian_whatsapp_number=null where admission_id={open}");
            var section = db.Scalar<string>($"select min(section_id)::text from tb_academic_year_sections s join tb_academic_years y on y.academic_year_id=s.academic_year_id where y.school_id='{school}' and s.status_id=1");
            var refused = await Admissions(db).ConfirmAdmissionAsync(school, open, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = long.Parse(section) }, default);
            Assert.Equal(409, refused.StatusCode); Assert.Contains("missing", refused.Message);
            Assert.Equal(before, db.Scalar<int>("select count(*) from tb_guardians"));
        }

        // ------------------------------------------------------------------------------------------------ Add Existing Student
        private static DirectStudentEntryRequestDto Direct(TaxDb db, string first, long? primary = null, string? email = "p@example.com", bool same = true, params (long Id, string? Relationship)[] extras)
        {
            var section = Section(db);
            return new DirectStudentEntryRequestDto
            {
                AdmissionDate = DateTime.UtcNow.Date, AcademicYearId = Y, ClassSectionId = section, FirstName = first, DateOfBirth = new DateTime(2016, 1, 2),
                ExistingGuardianId = primary, GuardianName = primary == null ? "Typed Parent" : "ignored", GuardianRelationship = "FATHER", GuardianMobileNumber = primary == null ? "9876543211" : "9000000000",
                GuardianEmail = email, GuardianWhatsAppSameAsMobile = same,
                AdditionalGuardians = extras.Select(e => new ExistingGuardianPickDto { GuardianId = e.Id, Relationship = e.Relationship }).ToList(),
            };
        }

        [DbFact]
        public async Task Add_Existing_Student_uses_the_same_pick_links_and_logs_and_follows_the_same_rules()
        {
            using var db = new TaxDb();
            var primary = Guardian(db, 0); var extra = Guardian(db, 1);
            var guardians = db.Scalar<int>("select count(*) from tb_guardians");
            var r = await Onboarding(db).CreateDirectEntryAsync(A, Direct(db, "DirectSib", primary, "direct@example.com", true, (extra, "MOTHER")), default, UserA);
            Assert.True(r.Status, r.Message);
            var student = r.Data!.StudentId;
            Assert.Equal(guardians, db.Scalar<int>("select count(*) from tb_guardians"));
            Assert.Equal(primary, db.Scalar<long>($"select guardian_id from tb_student_guardians where student_id='{student}' and is_primary"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and is_primary"));
            Assert.Equal("MOTHER", db.Scalar<string>($"select relationship from tb_student_guardians where student_id='{student}' and guardian_id={extra}"));
            Assert.Equal("direct@example.com", db.Scalar<string>($"select email from tb_guardians where guardian_id={primary}"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={primary} and source='EDIT' and changed_by={UserA}"));

            // missing contact on a bare record, another school's guardian, an inactive guardian, a merged one
            var bare = Guardian(db, 2);
            var students = db.Scalar<int>($"select count(*) from tb_students where school_id='{A}'");
            Assert.Equal(400, (await Onboarding(db).CreateDirectEntryAsync(A, Direct(db, "NoMail", bare, email: null), default, UserA)).StatusCode);
            Assert.Equal(404, (await Onboarding(db).CreateDirectEntryAsync(A, Direct(db, "Cross", Guardian(db, 0, B)), default, UserA)).StatusCode);
            db.Exec($"update tb_guardians set status_id={Inactive} where guardian_id={bare}");
            var inactive = await Onboarding(db).CreateDirectEntryAsync(A, Direct(db, "Gone", bare), default, UserA);
            Assert.Equal(409, inactive.StatusCode); Assert.Contains("inactive", inactive.Message);
            var merged = Guardian(db, 3); var survivor = Guardian(db, 4);
            db.Exec($"update tb_guardians set merged_into_guardian_id={survivor}, status_id={Inactive} where guardian_id={merged}");
            var followed = await Onboarding(db).CreateDirectEntryAsync(A, Direct(db, "Merged", merged), default, UserA);
            Assert.True(followed.Status, followed.Message);
            Assert.Equal(survivor, db.Scalar<long>($"select guardian_id from tb_student_guardians where student_id='{followed.Data!.StudentId}' and is_primary"));
            Assert.Equal(students + 1, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}'"));   // only the merged-follow one was created
        }

        [DbFact]
        public async Task A_typed_in_parent_still_creates_a_new_guardian_row_exactly_as_before()
        {
            using var db = new TaxDb();
            var guardians = db.Scalar<int>("select count(*) from tb_guardians");
            var r = await Onboarding(db).CreateDirectEntryAsync(A, Direct(db, "Plain"), default, UserA);
            Assert.True(r.Status, r.Message);
            Assert.Equal(guardians + 1, db.Scalar<int>("select count(*) from tb_guardians"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{r.Data!.StudentId}' and is_primary and portal_access"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_changes"));
        }

        // ------------------------------------------------------------------------------------------------ the Excel import hint
        private static byte[] Sheet(params string[][] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Students");
            var headers = new[] { "Admission number", "Full name", "Gender", "Date of birth", "Class", "Section", "Roll number", "Father name", "Father phone", "Primary guardian WhatsApp", "Primary guardian email" };
            for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }

        [DbFact]
        public async Task The_import_preview_hints_when_a_guardian_number_already_exists_counts_the_hints_and_never_links_anything()
        {
            using var db = new TaxDb();
            var section = Section(db);
            var cls = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={section}");
            var sec = db.Scalar<string>($"select section_name from tb_sections where section_id={section}");
            var existing = db.Scalar<string>($"select mobile_number from tb_guardians where school_id='{A}' order by guardian_id limit 1");
            var otherSchool = db.Scalar<string>($"select mobile_number from tb_guardians where school_id='{B}' order by guardian_id limit 1");
            string[] Row(string name, string phone) => new[] { "", name, "Male", "05/05/2018", cls, sec, "", "Father", phone, "SAME", "f@example.com" };
            var file = Sheet(Row("Has Sibling", existing), Row("Has Sibling Spaced", " " + existing[..5] + " " + existing[5..] + " "), Row("Brand New", "9100000001"), Row("Other School Number", otherSchool), Row("Bad Row", "123"));
            var guardians = db.Scalar<int>("select count(*) from tb_guardians");
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var preview = (await svc.PreviewAsync(A, file, default)).Data!;
            Assert.Equal(2, preview.Summary.GuardianHints);
            Assert.NotEmpty(preview.Rows[0].GuardianHint); Assert.Contains("sibling?", preview.Rows[0].GuardianHint);
            Assert.NotEmpty(preview.Rows[1].GuardianHint);
            Assert.Empty(preview.Rows[2].GuardianHint);
            Assert.Empty(preview.Rows[3].GuardianHint);   // another school's number is not a hint: families are per school
            Assert.Empty(preview.Rows[4].GuardianHint);   // a row with an error has no hint
            // a hint is information only: it is not a warning, so the status and the counts are those of a file without hints
            Assert.Equal("OK", preview.Rows[0].Status);
            Assert.DoesNotContain(preview.Rows[0].Messages, m => m.Contains("sibling"));
            Assert.Equal(guardians, db.Scalar<int>("select count(*) from tb_guardians"));   // a preview creates and links nothing
        }
    }
}
