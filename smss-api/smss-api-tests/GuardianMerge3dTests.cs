using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 3d (Docs/smss-st-3d-RUN-ME.sql adds reason and repointed to the merge audit): the duplicate guardian report and the merge. School Admin and Principal only, school from the token, a preview that changes
    /// nothing, an explicit confirmation, one transaction with both rows locked FOR UPDATE, links moved or combined, applications and earlier merges repointed, contact copied only where empty and logged (MERGE), logins
    /// never doubled, the absorbed guardian deactivated and kept, and an audit row. Never run by a migration or at startup. SCRATCH ONLY: [DbFact] needs SMSS_TEST_PG and a template with 3a and 3d.
    /// </summary>
    public class GuardianMerge3dTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const string Las = "LAS261103001";
        private const long UserA = TaxDb.UserA;
        private const long Y = TaxDb.YearA;

        private static GuardianMergeService Merge(TaxDb db) => new(new GuardianRepository(db.Ctx()), NullLogger<GuardianMergeService>.Instance);
        private static AdmissionService Admissions(TaxDb db) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);
        private static string Inactive => "(select sid from lut_status where sname='Inactive' and stype='general status')";

        private static long Guardian(TaxDb db, int skip = 0, string school = A) =>
            db.Scalar<long>($"select guardian_id from tb_guardians where school_id='{school}' order by guardian_id offset {skip} limit 1");
        private static string StudentOf(TaxDb db, long guardianId) => db.Scalar<string>($"select student_id from tb_student_guardians where guardian_id={guardianId} order by student_id limit 1");
        private static MergeRequestDto Req(long survivor, long absorbed, bool confirm = true, string? reason = null) => new() { SurvivorId = survivor, AbsorbedId = absorbed, Confirm = confirm, Reason = reason };

        private static string Fingerprint(TaxDb db) => db.Scalar<string>(@"select md5(
            coalesce((select string_agg(t::text, '|' order by guardian_id) from tb_guardians t), '') || coalesce((select string_agg(t::text, '|' order by student_guardian_id) from tb_student_guardians t), '') ||
            coalesce((select string_agg(t::text, '|' order by admission_id) from tb_admissions t), '') || coalesce((select string_agg(t::text, '|' order by admission_id, guardian_id) from tb_admission_guardians t), '') ||
            coalesce((select string_agg(t::text, '|' order by merge_id) from tb_guardian_merges t), '') || coalesce((select string_agg(t::text, '|' order by change_id) from tb_guardian_changes t), ''))");

        // ------------------------------------------------------------------------------------------------ the name flag
        [Theory]
        [InlineData("Neel Tomar", "Neel Tomar", "SAME")]
        [InlineData("neel  TOMAR", "Neel Tomar.", "SAME")]
        [InlineData("Tomar Neel", "Neel Tomar", "SIMILAR")]
        [InlineData("Neel Tomar", "Neel Singh Tomar", "SIMILAR")]
        [InlineData("Neel Tomar", "Neal Tomar", "SIMILAR")]
        [InlineData("Neel Tomar", "Vanya Singh", "DIFFERENT")]
        [InlineData("", "Neel Tomar", "DIFFERENT")]
        public void The_name_flag_tells_the_same_similar_and_different(string a, string b, string expected) => Assert.Equal(expected, GuardianNames.Compare(a, b));

        // ------------------------------------------------------------------------------------------------ the report and the 6 real pairs
        [DbFact]
        public async Task The_report_finds_the_six_real_sibling_pairs_scoped_to_the_callers_school_and_each_merges_cleanly()
        {
            using var db = new TaxDb();
            var guardiansBefore = db.Scalar<int>("select count(*) from tb_guardians");
            var students = db.Scalar<int>("select count(*) from tb_students");
            var users = db.Scalar<int>("select count(*) from tb_users");
            var total = 0;
            foreach (var school in new[] { A, Las })
            {
                var report = (await Merge(db).ListDuplicatesAsync(school, default)).Data!;
                Assert.Equal(3, report.GroupCount);
                Assert.All(report.Groups, g =>
                {
                    Assert.Equal("SAME", g.NameMatch);
                    Assert.Equal(2, g.Guardians.Count);
                    Assert.Single(g.Guardians, x => x.IsOldest);
                    Assert.All(g.Guardians, x => { Assert.Single(x.Students); Assert.NotEmpty(x.Students[0].AdmissionNumber); Assert.True(x.Students[0].IsPrimary); });
                });
                foreach (var g in report.Groups)
                {
                    var survivor = g.Guardians.Single(x => x.IsOldest);
                    var absorbed = g.Guardians.Single(x => !x.IsOldest);
                    var preview = (await Merge(db).PreviewAsync(school, Req(survivor.GuardianId, absorbed.GuardianId), default)).Data!;
                    Assert.True(preview.CanMerge);
                    Assert.Single(preview.Links); Assert.Equal("MOVE", preview.Links[0].Action);
                    var r = await Merge(db).MergeAsync(school, Req(survivor.GuardianId, absorbed.GuardianId, true, "same parent, two children"), UserA, default);
                    Assert.True(r.Status, r.Message);
                    total++;
                    Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={survivor.GuardianId}"));   // one parent, both children
                    Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={survivor.GuardianId} and is_primary and portal_access"));
                    Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={absorbed.GuardianId}"));
                    Assert.Equal(survivor.GuardianId, db.Scalar<long>($"select merged_into_guardian_id from tb_guardians where guardian_id={absorbed.GuardianId}"));
                    Assert.Equal("Inactive", db.Scalar<string>($"select s.sname from tb_guardians g join lut_status s on s.sid=g.status_id where g.guardian_id={absorbed.GuardianId}"));
                }
                Assert.Equal(0, (await Merge(db).ListDuplicatesAsync(school, default)).Data!.GroupCount);
            }
            Assert.Equal(6, total);
            Assert.Equal(guardiansBefore, db.Scalar<int>("select count(*) from tb_guardians"));   // nothing was deleted
            Assert.Equal(guardiansBefore - 6, db.Scalar<int>("select count(*) from tb_guardians where merged_into_guardian_id is null"));
            Assert.Equal(students, db.Scalar<int>("select count(*) from tb_students"));
            Assert.Equal(users, db.Scalar<int>("select count(*) from tb_users"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_students s where (select count(*) from tb_student_guardians l where l.student_id=s.student_id and l.is_primary) <> 1"));   // every student has exactly one primary
            Assert.Equal(6, db.Scalar<int>("select count(*) from tb_guardian_merges"));
        }

        [DbFact]
        public async Task The_report_never_shows_another_schools_guardians()
        {
            using var db = new TaxDb();
            var other = (await Merge(db).ListDuplicatesAsync(B, default)).Data!;   // a school with no pairs
            Assert.Equal(0, other.GroupCount);
            var mine = (await Merge(db).ListDuplicatesAsync(A, default)).Data!;
            var ids = mine.Groups.SelectMany(g => g.Guardians).Select(g => g.GuardianId).ToList();
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_guardians where guardian_id in ({string.Join(",", ids)}) and school_id<>'{A}'"));
            // a pair made of a shared number across two schools is not a duplicate: families are per school
            var g1 = Guardian(db, 0, A); var g2 = Guardian(db, 0, B);
            db.Exec($"update tb_guardians set mobile_number='9300000001' where guardian_id in ({g1},{g2})");
            Assert.DoesNotContain(g1, (await Merge(db).ListDuplicatesAsync(A, default)).Data!.Groups.SelectMany(g => g.Guardians).Select(g => g.GuardianId));
        }

        // ------------------------------------------------------------------------------------------------ preview and confirmation
        [DbFact]
        public async Task The_preview_shows_exactly_what_would_move_and_changes_nothing()
        {
            using var db = new TaxDb();
            var survivor = Guardian(db, 0); var absorbed = Guardian(db, 1);
            db.Exec($"update tb_guardians set whatsapp_number='+919800000009', email='ab@example.com', occupation='Farmer' where guardian_id={absorbed}");
            var adm = db.Scalar<long>($"select min(admission_id) from tb_admissions where school_id='{A}'");
            db.Exec($"update tb_admissions set guardian_id={absorbed} where admission_id={adm}");
            var before = Fingerprint(db);
            var p = (await Merge(db).PreviewAsync(A, Req(survivor, absorbed), default)).Data!;
            Assert.True(p.CanMerge);
            Assert.Equal(survivor, p.Survivor.GuardianId); Assert.Equal(absorbed, p.Absorbed.GuardianId);
            Assert.Equal(new[] { "whatsapp_number", "email", "occupation" }.OrderBy(x => x), p.FieldsToCopy.Keys.OrderBy(x => x));
            Assert.Equal(StudentOf(db, absorbed), p.Links.Single().StudentId);
            Assert.NotEmpty(p.Links.Single().StudentName);
            Assert.Equal(("PRIMARY", "REPOINT"), (p.Applications.Single().Role, p.Applications.Single().Action));
            Assert.Equal("NONE", p.Account);
            Assert.Equal(before, Fingerprint(db));   // nothing changed
        }

        [DbFact]
        public async Task A_merge_needs_the_confirmation_flag_two_different_guardians_and_a_short_reason()
        {
            using var db = new TaxDb();
            var s = Guardian(db, 0); var a = Guardian(db, 1);
            var before = Fingerprint(db);
            Assert.Equal(400, (await Merge(db).MergeAsync(A, Req(s, a, false), UserA, default)).StatusCode);
            Assert.Equal(400, (await Merge(db).MergeAsync(A, Req(s, s), UserA, default)).StatusCode);
            Assert.Equal(400, (await Merge(db).MergeAsync(A, Req(s, a, true, new string('x', 501)), UserA, default)).StatusCode);
            Assert.Equal(before, Fingerprint(db));
        }

        // ------------------------------------------------------------------------------------------------ the move
        [DbFact]
        public async Task A_student_linked_to_both_keeps_one_link_with_the_flags_combined_and_one_primary_whichever_was_primary()
        {
            using var db = new TaxDb();
            foreach (var survivorIsPrimary in new[] { true, false })
            {
                var s = Guardian(db, survivorIsPrimary ? 10 : 12); var a = Guardian(db, survivorIsPrimary ? 11 : 13);
                var student = StudentOf(db, survivorIsPrimary ? s : a);
                // the survivor's child is linked to the other guardian too (not primary); the flags differ between the two links
                db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship, is_primary, is_emergency_contact, can_pickup_student, portal_access) values ('{student}', {(survivorIsPrimary ? a : s)}, 'FATHER', false, true, false, {(survivorIsPrimary ? "true" : "false")})");
                db.Exec($"update tb_student_guardians set portal_access={(survivorIsPrimary ? "false" : "true")}, can_pickup_student=true, is_emergency_contact=false where student_id='{student}' and is_primary");
                var r = await Merge(db).MergeAsync(A, Req(s, a), UserA, default);
                Assert.True(r.Status, r.Message);
                Assert.Equal(1, r.Data!.LinksCombined);
                Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and guardian_id={s}"));
                Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and guardian_id={a}"));
                Assert.True(db.Scalar<bool>($"select is_primary from tb_student_guardians where student_id='{student}' and guardian_id={s}"));   // the primary wins
                Assert.True(db.Scalar<bool>($"select portal_access from tb_student_guardians where student_id='{student}' and guardian_id={s}"));   // on if it was on for either
                Assert.True(db.Scalar<bool>($"select can_pickup_student from tb_student_guardians where student_id='{student}' and guardian_id={s}"));
                Assert.True(db.Scalar<bool>($"select is_emergency_contact from tb_student_guardians where student_id='{student}' and guardian_id={s}"));
                Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{student}' and is_primary"));
            }
        }

        [DbFact]
        public async Task A_pending_application_is_repointed_and_can_still_be_confirmed_on_the_survivor()
        {
            using var db = new TaxDb();
            var survivor = Guardian(db, 0); var absorbed = Guardian(db, 1); var extraOnly = Guardian(db, 2);
            var req = new SubmitAdmissionRequestDto
            {
                AcademicYearId = Y, AppliedClassId = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{A}'"), AdmissionTypeId = db.Scalar<long>("select min(admission_type_id) from lut_admission_type"),
                ApplicantFirstName = "Pending", DateOfBirth = new DateTime(2018, 3, 3), ExistingGuardianId = absorbed, GuardianRelationship = "MOTHER", GuardianEmail = "p@example.com", GuardianWhatsAppSameAsMobile = true,
                AdditionalGuardians = new() { new ExistingGuardianPickDto { GuardianId = extraOnly, Relationship = "FATHER" } },
            };
            var submitted = await Admissions(db).SubmitAsync(A, req, default);
            Assert.True(submitted.Status, submitted.Message);
            var id = submitted.Data!.AdmissionId;
            // a second application where the SURVIVOR is the primary and the absorbed one an extra: that extra row is dropped (the survivor is already there)
            var second = await Admissions(db).SubmitAsync(A, new SubmitAdmissionRequestDto
            {
                AcademicYearId = Y, AppliedClassId = req.AppliedClassId, AdmissionTypeId = req.AdmissionTypeId, ApplicantFirstName = "Second", DateOfBirth = new DateTime(2017, 1, 1),
                ExistingGuardianId = survivor, GuardianRelationship = "MOTHER", GuardianEmail = "p@example.com", GuardianWhatsAppSameAsMobile = true,
                AdditionalGuardians = new() { new ExistingGuardianPickDto { GuardianId = absorbed, Relationship = "FATHER" } },
            }, default);
            Assert.True(second.Status, second.Message);

            var r = await Merge(db).MergeAsync(A, Req(survivor, absorbed), UserA, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(survivor, db.Scalar<long>($"select guardian_id from tb_admissions where admission_id={id}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_admission_guardians where guardian_id={absorbed}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_admissions where guardian_id={absorbed}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_admission_guardians where admission_id={second.Data!.AdmissionId}"));   // the survivor was already the primary there: nothing doubled

            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var confirmed = await Admissions(db).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.True(confirmed.Status, confirmed.Message);
            Assert.Equal(survivor, db.Scalar<long>($"select guardian_id from tb_student_guardians where student_id='{confirmed.Data!.StudentId}' and is_primary"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_guardians where student_id='{confirmed.Data.StudentId}' and guardian_id={extraOnly} and not is_primary"));
        }

        [DbFact]
        public async Task A_guardian_merged_earlier_into_the_absorbed_one_is_repointed_so_there_are_no_chains()
        {
            using var db = new TaxDb();
            var survivor = Guardian(db, 0); var absorbed = Guardian(db, 1); var earlier = Guardian(db, 2);
            db.Exec($"update tb_guardians set merged_into_guardian_id={absorbed}, status_id={Inactive} where guardian_id={earlier}");
            var r = await Merge(db).MergeAsync(A, Req(survivor, absorbed), UserA, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(1, r.Data!.GuardiansRepointed);
            Assert.Equal(survivor, db.Scalar<long>($"select merged_into_guardian_id from tb_guardians where guardian_id={earlier}"));
            Assert.Equal(survivor, db.Scalar<long>($"select merged_into_guardian_id from tb_guardians where guardian_id={absorbed}"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardians g join tb_guardians t on t.guardian_id=g.merged_into_guardian_id where t.merged_into_guardian_id is not null"));   // nobody points at a merged guardian
            Assert.Contains(earlier, JsonSerializer.Deserialize<long[]>(db.Scalar<string>("select repointed->'mergedPointers' from tb_guardian_merges"))!);
        }

        [DbFact]
        public async Task Contact_is_copied_only_where_the_survivor_has_none_and_the_copies_are_logged_as_MERGE_and_the_mobile_is_never_copied()
        {
            using var db = new TaxDb();
            var survivor = Guardian(db, 0); var absorbed = Guardian(db, 1);
            db.Exec($"update tb_guardians set email='keep@example.com' where guardian_id={survivor}");
            db.Exec($"update tb_guardians set whatsapp_number='+919800000010', email='lose@example.com', occupation='Driver' where guardian_id={absorbed}");
            var mobile = db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={survivor}");
            var r = await Merge(db).MergeAsync(A, Req(survivor, absorbed), UserA, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(new[] { "occupation", "whatsapp_number" }, r.Data!.FieldsCopied.OrderBy(x => x));
            Assert.Equal("keep@example.com", db.Scalar<string>($"select email from tb_guardians where guardian_id={survivor}"));   // never overwritten
            Assert.Equal("+919800000010", db.Scalar<string>($"select whatsapp_number from tb_guardians where guardian_id={survivor}"));
            Assert.Equal("Driver", db.Scalar<string>($"select occupation from tb_guardians where guardian_id={survivor}"));
            Assert.Equal(mobile, db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={survivor}"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={survivor}"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_guardian_changes where guardian_id={survivor} and source='MERGE' and field_name='whatsapp_number' and old_value is null and new_value='+919800000010' and changed_by={UserA}"));
        }

        // ------------------------------------------------------------------------------------------------ logins
        [DbFact]
        public async Task Two_guardians_that_both_have_a_login_cannot_be_merged_and_nothing_changes()
        {
            using var db = new TaxDb();
            var s = Guardian(db, 0); var a = Guardian(db, 1);
            var users = db.Scalar<string>($"select string_agg(user_id::text, ',' order by user_id) from (select user_id from tb_users where school_id='{A}' order by user_id limit 2) x").Split(',');
            db.Exec($"update tb_guardians set user_id={users[0]} where guardian_id={s}");
            db.Exec($"update tb_guardians set user_id={users[1]} where guardian_id={a}");
            var before = Fingerprint(db);
            var preview = (await Merge(db).PreviewAsync(A, Req(s, a), default)).Data!;
            Assert.Equal("BLOCKED", preview.Account); Assert.False(preview.CanMerge); Assert.NotEmpty(preview.Blockers);
            var r = await Merge(db).MergeAsync(A, Req(s, a), UserA, default);
            Assert.Equal(409, r.StatusCode); Assert.Contains("login", r.Message);
            Assert.Equal(before, Fingerprint(db));
        }

        [DbFact]
        public async Task When_only_the_absorbed_guardian_has_a_login_it_moves_to_the_survivor_and_is_recorded()
        {
            using var db = new TaxDb();
            var s = Guardian(db, 0); var a = Guardian(db, 1);
            var user = db.Scalar<long>($"select min(user_id) from tb_users where school_id='{A}'");
            db.Exec($"update tb_guardians set user_id={user} where guardian_id={a}");
            Assert.Equal("MOVE", (await Merge(db).PreviewAsync(A, Req(s, a), default)).Data!.Account);
            var r = await Merge(db).MergeAsync(A, Req(s, a), UserA, default);
            Assert.True(r.Status, r.Message);
            Assert.True(r.Data!.AccountMoved);
            Assert.Equal(user, db.Scalar<long>($"select user_id from tb_guardians where guardian_id={s}"));
            Assert.Null(db.Scalar<string?>($"select user_id::text from tb_guardians where guardian_id={a}"));
            Assert.Equal(user, db.Scalar<long>("select (repointed->'account'->>'movedUserId')::bigint from tb_guardian_merges"));
        }

        // ------------------------------------------------------------------------------------------------ other schools, roles, repeats
        [DbFact]
        public async Task Another_schools_guardian_is_a_404_for_preview_and_merge_and_nothing_changes()
        {
            using var db = new TaxDb();
            var mine = Guardian(db, 0); var theirs = Guardian(db, 0, B);
            var before = Fingerprint(db);
            Assert.Equal(404, (await Merge(db).PreviewAsync(A, Req(mine, theirs), default)).StatusCode);
            Assert.Equal(404, (await Merge(db).PreviewAsync(A, Req(theirs, mine), default)).StatusCode);
            Assert.Equal(404, (await Merge(db).MergeAsync(A, Req(mine, theirs), UserA, default)).StatusCode);
            Assert.Equal(404, (await Merge(db).MergeAsync(A, Req(theirs, mine), UserA, default)).StatusCode);
            Assert.Equal(404, (await Merge(db).MergeAsync(A, Req(mine, 999999999), UserA, default)).StatusCode);
            Assert.Equal(before, Fingerprint(db));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_School_Admin_and_Principal_may_see_the_duplicates_preview_and_merge(string role)
        {
            var ok = role is "School Admin" or "Principal";
            var svc = Fake.Of<IGuardianMergeService>();
            var c = Fake.WithUser(new GuardiansController(Fake.Of<IGuardianService>(), svc), Fake.User(A, 5, role));
            foreach (var (name, run) in new (string, Func<Task<IActionResult>>)[]
            {
                ("ListDuplicatesAsync", () => c.Duplicates(default)),
                ("PreviewAsync", () => c.MergePreview(Req(1, 2), default)),
                ("MergeAsync", () => c.Merge(Req(1, 2), default)),
            })
            {
                var code = Fake.Code(await run());
                if (ok) Assert.NotEqual(403, code); else Assert.Equal(403, code);
                Assert.Equal(ok, Fake.ProxyOf(svc).Calls.Contains(name));
            }
        }

        [DbFact]
        public async Task Merging_a_guardian_that_was_already_absorbed_is_refused_cleanly()
        {
            using var db = new TaxDb();
            var s = Guardian(db, 0); var a = Guardian(db, 1); var other = Guardian(db, 2);
            Assert.True((await Merge(db).MergeAsync(A, Req(s, a), UserA, default)).Status);
            var before = Fingerprint(db);
            var again = await Merge(db).MergeAsync(A, Req(s, a), UserA, default);
            Assert.Equal(409, again.StatusCode); Assert.Contains("already merged", again.Message);
            Assert.Equal(409, (await Merge(db).MergeAsync(A, Req(other, a), UserA, default)).StatusCode);   // the absorbed one into a third guardian
            var survivorMerged = await Merge(db).MergeAsync(A, Req(a, other), UserA, default);            // the merged one as the survivor
            Assert.Equal(409, survivorMerged.StatusCode); Assert.Contains("itself merged", survivorMerged.Message);
            db.Exec($"update tb_guardians set status_id={Inactive} where guardian_id={other}");
            Assert.Equal(409, (await Merge(db).MergeAsync(A, Req(s, other), UserA, default)).StatusCode);   // an inactive guardian
            Assert.Equal(before.Length, Fingerprint(db).Length);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_guardian_merges"));
        }

        [DbFact]
        public async Task Two_simultaneous_merges_of_the_same_pair_or_of_opposite_directions_leave_consistent_data()
        {
            using var db = new TaxDb();
            var s = Guardian(db, 0); var a = Guardian(db, 1);
            var same = await Task.WhenAll(Task.Run(() => Merge(db).MergeAsync(A, Req(s, a), UserA, default)), Task.Run(() => Merge(db).MergeAsync(A, Req(s, a), UserA, default)));
            Assert.Equal(1, same.Count(r => r.Status));
            Assert.All(same.Where(r => !r.Status), r => Assert.Equal(409, r.StatusCode));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_guardian_merges"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={s}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_guardians where guardian_id={a}"));

            var x = Guardian(db, 3); var y = Guardian(db, 4);
            var opposite = await Task.WhenAll(Task.Run(() => Merge(db).MergeAsync(A, Req(x, y), UserA, default)), Task.Run(() => Merge(db).MergeAsync(A, Req(y, x), UserA, default)));
            Assert.Equal(1, opposite.Count(r => r.Status));
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_guardian_merges"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_guardians where guardian_id in ({x},{y}) and merged_into_guardian_id is null"));   // exactly one survivor
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_students s where (select count(*) from tb_student_guardians l where l.student_id=s.student_id and l.is_primary) <> 1"));
        }

        // ------------------------------------------------------------------------------------------------ the audit
        [DbFact]
        public async Task The_audit_row_records_who_when_the_pair_the_moved_links_the_copied_fields_a_snapshot_the_repointed_and_the_reason()
        {
            using var db = new TaxDb();
            var s = Guardian(db, 0); var a = Guardian(db, 1);
            db.Exec($"update tb_guardians set email='snap@example.com' where guardian_id={a}");
            var student = StudentOf(db, a);
            var link = db.Scalar<long>($"select student_guardian_id from tb_student_guardians where guardian_id={a}");
            var originalMobile = db.Scalar<string>($"select mobile_number from tb_guardians where guardian_id={a}");
            var r = await Merge(db).MergeAsync(A, Req(s, a, true, "  same mother, entered twice  "), UserA, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_guardian_merges"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_guardian_merges where merge_id={r.Data!.MergeId} and school_id='{A}' and survivor_id={s} and absorbed_id={a} and merged_by={UserA} and merged_at > now() - interval '5 minutes' and reason='same mother, entered twice'"));
            using var moved = JsonDocument.Parse(db.Scalar<string>("select moved_links::text from tb_guardian_merges"));
            var first = moved.RootElement[0];
            Assert.Equal("MOVED", first.GetProperty("action").GetString());
            Assert.Equal(student, first.GetProperty("studentId").GetString());
            Assert.Equal(link, first.GetProperty("studentGuardianId").GetInt64());
            Assert.Equal(a, first.GetProperty("fromGuardianId").GetInt64());
            Assert.True(first.GetProperty("isPrimary").GetBoolean());
            Assert.Equal("snap@example.com", db.Scalar<string>("select copied_fields->>'email' from tb_guardian_merges"));
            Assert.Equal(originalMobile, db.Scalar<string>("select absorbed_snapshot->>'mobileNumber' from tb_guardian_merges"));   // the absorbed row as it was
            Assert.Equal(a, db.Scalar<long>("select (absorbed_snapshot->>'guardianId')::bigint from tb_guardian_merges"));
            Assert.Equal("[]", db.Scalar<string>("select (repointed->'admissions')::text from tb_guardian_merges"));
            Assert.Equal("[]", db.Scalar<string>("select (repointed->'mergedPointers')::text from tb_guardian_merges"));
        }

        // ------------------------------------------------------------------------------------------------ never by a migration
        [Fact]
        public void No_migration_script_merges_guardians()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Docs"))) dir = dir.Parent;
            Assert.NotNull(dir);
            foreach (var file in Directory.GetFiles(Path.Combine(dir!.FullName, "Docs"), "smss-st-3*-RUN-ME.sql"))
            {
                var sql = string.Join("\n", File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("--")));   // the executable lines
                Assert.DoesNotContain("INSERT INTO public.tb_guardian_merges", sql, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotMatch(@"UPDATE\s+public\.tb_guardians\s+SET[^;]*merged_into_guardian_id", sql);
            }
        }

        [DbFact]
        public void The_3d_script_is_safe_to_run_again_and_adds_only_reason_and_repointed()
        {
            using var db = new TaxDb();
            Assert.Equal(2, db.Scalar<int>("select count(*) from information_schema.columns where table_name='tb_guardian_merges' and column_name in ('reason','repointed')"));
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Docs", "smss-st-3d-RUN-ME.sql"))) dir = dir.Parent;
            var script = File.ReadAllText(Path.Combine(dir!.FullName, "Docs", "smss-st-3d-RUN-ME.sql"));
            var before = Fingerprint(db);
            db.Exec(script); db.Exec(script);
            Assert.Equal(before, Fingerprint(db));
        }
    }
}
