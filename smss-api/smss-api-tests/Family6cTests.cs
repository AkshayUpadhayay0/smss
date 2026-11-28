using System.Reflection;
using System.Text.Json;
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
    /// Stage 6c (no migration): the access helper ("which student is this request for") and the parent's read-only children endpoints. A guardian reaches a child ONLY when the guardian is Active and not merged, the child is
    /// a student of the same school, and the link has portal access on; every other case is the same 404. The downstream student-side services are fakes here that record whether they were called: a refused request
    /// must never reach them. [DbFact]: needs SMSS_TEST_PG and a template with the 6a migration (smss_tpl_6a).
    /// </summary>
    public class Family6cTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;

        // ------------------------------------------------------------------------------------------------ the world
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private static string Student(TaxDb db, string school, int n = 0) =>
            db.Scalar<string>($"select student_id from tb_students where school_id = {Q(school)} order by student_id offset {n} limit 1");

        /// <summary>A guardian with a Guardian login (user_type Guardian, role GUARDIAN) linked, with portal access on, to the given students.</summary>
        private static (long GuardianId, long UserId) Parent(TaxDb db, string school, string loginId, params string[] students)
        {
            var user = db.Scalar<long>($"insert into tb_users (school_id, user_type, org_user_id, display_name, status_id, is_first_login, password_hash) values ({Q(school)}, 'Guardian', {Q(loginId)}, 'Parent', (select sid from lut_status where sname = 'Active' and stype = 'general status'), false, 'x') returning user_id");
            db.Exec($"insert into tb_user_roles (user_id, role_id) select {user}, role_id from lut_roles where role_code = 'GUARDIAN'");
            var g = db.Scalar<long>($@"insert into tb_guardians (school_id, first_name, relationship, mobile_number, status_id, user_id)
                values ({Q(school)}, {Q("Parent " + loginId)}, 'MOTHER', '9876543210', (select sid from lut_status where sname = 'Active' and stype = 'general status'), {user}) returning guardian_id");
            foreach (var s in students) db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship) values ({Q(s)}, {g}, 'MOTHER')");
            return (g, user);
        }

        private static AccessCaller Guardian(string school, (long GuardianId, long UserId) p, long? claim = null) => new(school, p.UserId, false, true, claim ?? p.GuardianId);

        // ------------------------------------------------------------------------------------------------ the downstream fakes
        private sealed class Probe
        {
            public readonly IStudentService Students = Fake.Of<IStudentService>(new() { ["GetProfileForStudentAsync"] = Fake.Ok(new StudentMyProfileDto { StudentId = "S", FullName = "Child", AdmissionNumber = "1", StatusName = "Active", GenderName = "Female", DateOfBirth = new DateTime(2015, 1, 1), Address = "Addr", MobileNumber = "9999999999", Email = "child@example.com", Guardians = new() { new MyGuardianDto { FullName = "Other Parent", Relationship = "FATHER", MobileNumber = "9111111111", Email = "other@example.com" } }, ProfilePhotoUrl = "/api/Student/S/photo?v=abc" }) });
            public readonly IAttendanceService Attendance = Fake.Of<IAttendanceService>(new() { ["GetForStudentAsync"] = Fake.Ok(new StudentOwnAttendanceDto { Days = new() }) });
            public readonly IHomeworkService Homework = Fake.Of<IHomeworkService>(new() { ["GetForStudentAsync"] = Fake.Ok(new List<HomeworkDto>()) });
            public readonly ITimetableService Timetable = Fake.Of<ITimetableService>(new() { ["GetStudentGridAsync"] = Fake.Ok(new TimetableGridDto()) });
            public readonly IReportResultService Reports = Fake.Of<IReportResultService>(new()
            {
                ["CardsForStudentAsync"] = Fake.Ok(new List<MyReportCardDto>()), ["CardForStudentAsync"] = Fake.Ok(new MyReportCardDetailDto()),
                ["CardPdfForStudentAsync"] = new ReportFileResult { Content = new byte[] { 1 }, FileName = "c.pdf", StatusCode = 200 },
            });
            public readonly IReportScheduleService Schedule = Fake.Of<IReportScheduleService>(new() { ["GetAsync"] = Fake.Ok(new List<ScheduleSchemeDto>()) });
            public readonly IStudentMyFeesService Fees = Fake.Of<IStudentMyFeesService>(new() { ["GetFeesForStudentAsync"] = Fake.Ok(new StudentMyFeesDto()) });
            public readonly IFeePaymentService Payments = Fake.Of<IFeePaymentService>(new() { ["GetReceiptForStudentAsync"] = Fake.Ok(new FeeReceiptDto()) });
            public readonly IStudentLeaveService Leave = Fake.Of<IStudentLeaveService>(new() { ["GetForStudentAsync"] = Fake.Ok(new List<StudentLeaveDto>()) });
            public readonly IStudentPhotoService Photos = Fake.Of<IStudentPhotoService>(new() { ["GetAsync"] = new StudentPhotoResult { Content = new MemoryStream(new byte[] { 1 }), ContentType = "image/png", ETag = "\"x\"", StatusCode = 200 } });
            public readonly INoticeService Notices = Fake.Of<INoticeService>(new() { ["GetMyAsync"] = Fake.Ok(new List<NoticeDto>()), ["GetUnreadCountAsync"] = Fake.Ok(new UnreadCountDto { Count = 3 }), ["GetAsync"] = Fake.Ok(new NoticeDto { NoticeId = 4, Title = "T", Body = "B", Audience = "STUDENTS_GUARDIANS", CreatedBy = 99, CreatedByName = "Office", StatusName = "Active" }) });
            public readonly ISchoolEventService Events = Fake.Of<ISchoolEventService>(new() { ["GetRangeAsync"] = Fake.Ok(new List<SchoolEventDto> { new() { EventId = 1, EventTitle = "PTM", CategoryName = "PTM", CategoryCode = "PTM", ColorHex = "#f59e0b", CreatedByName = "Admin", AcademicYearId = 41 } }) });

            public int Calls => new object[] { Students, Attendance, Homework, Timetable, Reports, Schedule, Fees, Payments, Leave, Photos, Notices, Events }.Sum(f => Fake.ProxyOf(f).Calls.Count);

            public FamilyService Service(TaxDb db)
            {
                var ctx = db.Ctx(); var accounts = new ParentAccountRepository(ctx); var students = new StudentRepository(ctx); var guardians = new GuardianRepository(ctx);
                return new FamilyService(new StudentAccessService(accounts, students, guardians), accounts, students, guardians, Students, Attendance, Homework, Timetable, Reports, Schedule, Fees, Payments, Leave, Photos, Notices, Events, NullLogger<FamilyService>.Instance);
            }
        }

        private static StudentAccessService Helper(TaxDb db)
        {
            var ctx = db.Ctx();
            return new StudentAccessService(new ParentAccountRepository(ctx), new StudentRepository(ctx), new GuardianRepository(ctx));
        }

        /// <summary>Every per-child read, by name: runs it and returns the HTTP status it answers with.</summary>
        private static readonly (string Name, Func<FamilyService, AccessCaller, string, Task<int>> Run)[] ChildReads =
        {
            ("profile", async (s, c, id) => (await s.ProfileAsync(c, id, default)).StatusCode),
            ("attendance", async (s, c, id) => (await s.AttendanceAsync(c, id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20), default)).StatusCode),
            ("homework", async (s, c, id) => (await s.HomeworkAsync(c, id, null, null, default)).StatusCode),
            ("timetable", async (s, c, id) => (await s.TimetableAsync(c, id, default)).StatusCode),
            ("exam-schedule", async (s, c, id) => (await s.ExamScheduleAsync(c, id, default)).StatusCode),
            ("report-cards", async (s, c, id) => (await s.ReportCardsAsync(c, id, default)).StatusCode),
            ("report-card", async (s, c, id) => (await s.ReportCardAsync(c, id, 5, default)).StatusCode),
            ("report-card-pdf", async (s, c, id) => { var (d, f) = await s.ReportCardPdfAsync(c, id, 5, default); return f == null ? d.StatusCode : f.StatusCode; }),
            ("fees", async (s, c, id) => (await s.FeesAsync(c, id, default)).StatusCode),
            ("receipt", async (s, c, id) => (await s.ReceiptAsync(c, id, 9, default)).StatusCode),
            ("leave", async (s, c, id) => (await s.LeaveAsync(c, id, null, null, null, default)).StatusCode),
            ("photo", async (s, c, id) => { var (d, p) = await s.PhotoAsync(c, id, default); return p == null ? d.StatusCode : p.StatusCode; }),
        };
        public static IEnumerable<object[]> Reads() => ChildReads.Select(r => new object[] { r.Name });
        private static Func<FamilyService, AccessCaller, string, Task<int>> Read(string name) => ChildReads.Single(r => r.Name == name).Run;

        // ------------------------------------------------------------------------------------------------ the access helper
        [DbFact]
        public async Task A_guardian_reaches_a_linked_child_and_only_a_linked_child()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1); var s3 = Student(db, A, 2);
            var p = Parent(db, A, "GHS001GD001", s1);
            Parent(db, A, "GHS001GD002", s2);                                                                                         // another parent's child
            var ok = await Helper(db).ResolveAsync(Guardian(A, p), s1, default);
            Assert.True(ok.Allowed); Assert.Equal(s1, ok.Access!.StudentId); Assert.True(ok.Access.ViaGuardian); Assert.Equal(p.GuardianId, ok.Access.GuardianId);
            foreach (var other in new[] { s2, s3, "NO-SUCH-STUDENT", "", null })
            {
                var d = await Helper(db).ResolveAsync(Guardian(A, p), other, default);
                Assert.False(d.Allowed); Assert.Equal(404, d.StatusCode);
            }
        }

        [DbFact]
        public async Task Portal_access_off_hides_the_child_everywhere_and_switching_it_back_on_restores_it()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1);
            var p = Parent(db, A, "GHS001GD001", s1, s2);
            db.Exec($"update tb_student_guardians set portal_access = false where guardian_id = {p.GuardianId} and student_id = '{s2}'");
            Assert.Equal(404, (await Helper(db).ResolveAsync(Guardian(A, p), s2, default)).StatusCode);
            var probe = new Probe();
            var list = await probe.Service(db).ListChildrenAsync(Guardian(A, p), default);
            Assert.Equal(new[] { s1 }, list.Data!.Children.Select(c => c.StudentId));
            var callsAfterList = probe.Calls;
            foreach (var (name, run) in ChildReads) Assert.True(404 == await run(probe.Service(db), Guardian(A, p), s2), name);
            Assert.Equal(callsAfterList, probe.Calls);                                                                                  // the switched-off child never reached a service (only the visible child's card did)
            db.Exec($"update tb_student_guardians set portal_access = true where guardian_id = {p.GuardianId} and student_id = '{s2}'");
            Assert.True((await Helper(db).ResolveAsync(Guardian(A, p), s2, default)).Allowed);
        }

        [DbFact]
        public async Task Another_schools_student_is_a_404_even_when_a_link_to_it_exists_and_a_school_is_never_taken_from_the_request()
        {
            using var db = new TaxDb();
            var foreign = Student(db, B);
            var p = Parent(db, A, "GHS001GD001", Student(db, A));
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship) values ({Q(foreign)}, {p.GuardianId}, 'MOTHER')");   // a link across schools (should never exist; still refused)
            Assert.Equal(404, (await Helper(db).ResolveAsync(Guardian(A, p), foreign, default)).StatusCode);
            Assert.Equal(404, (await Helper(db).ResolveAsync(new AccessCaller(B, p.UserId, false, true, p.GuardianId), Student(db, A), default)).StatusCode);   // a token for the other school: the guardian is not that school's
            var probe = new Probe();
            Assert.Equal(new[] { Student(db, A) }, (await probe.Service(db).ListChildrenAsync(Guardian(A, p), default)).Data!.Children.Select(c => c.StudentId));
        }

        [DbFact]
        public async Task A_merged_or_inactive_guardian_and_a_mismatching_token_claim_reach_nothing()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A);
            var p = Parent(db, A, "GHS001GD001", s1);
            var probe = new Probe();
            Assert.Equal(404, (await Helper(db).ResolveAsync(Guardian(A, p, claim: p.GuardianId + 1), s1, default)).StatusCode);        // the claim must be the guardian behind the user
            Assert.Equal(404, (await probe.Service(db).ListChildrenAsync(Guardian(A, p, claim: p.GuardianId + 1), default)).StatusCode);
            db.Exec($"update tb_guardians set merged_into_guardian_id = (select min(guardian_id) from tb_guardians where guardian_id <> {p.GuardianId}) where guardian_id = {p.GuardianId}");
            Assert.Equal(404, (await Helper(db).ResolveAsync(Guardian(A, p), s1, default)).StatusCode);
            Assert.Equal(404, (await probe.Service(db).ListChildrenAsync(Guardian(A, p), default)).StatusCode);
            db.Exec($"update tb_guardians set merged_into_guardian_id = null, status_id = (select sid from lut_status where sname = 'Inactive' and stype = 'general status') where guardian_id = {p.GuardianId}");
            Assert.Equal(404, (await Helper(db).ResolveAsync(Guardian(A, p), s1, default)).StatusCode);
            foreach (var (name, run) in ChildReads) Assert.True(404 == await run(probe.Service(db), Guardian(A, p), s1), name);
            Assert.Equal(0, probe.Calls);
        }

        [DbFact]
        public async Task A_student_login_gets_its_own_record_only_never_the_family_screen_and_other_logins_are_refused()
        {
            using var db = new TaxDb();
            var s1 = db.Scalar<string>($"select min(s.student_id) from tb_students s where s.school_id = {Q(A)} and not exists (select 1 from tb_users u where u.school_id = {Q(A)} and u.org_user_id = s.student_id)");
            var s2 = db.Scalar<string>($"select min(student_id) from tb_students where school_id = {Q(A)} and student_id > {Q(s1)}");
            var user = db.Scalar<long>($"insert into tb_users (school_id, user_type, org_user_id, status_id) values ({Q(A)}, 'Student', {Q(s1)}, (select sid from lut_status where sname = 'Active' and stype = 'general status')) returning user_id");
            var student = new AccessCaller(A, user, true, false);
            var own = await Helper(db).ResolveAsync(student, null, default);
            Assert.True(own.Allowed); Assert.Equal(s1, own.Access!.StudentId); Assert.False(own.Access.ViaGuardian);
            Assert.True((await Helper(db).ResolveAsync(student, s1, default)).Allowed);
            Assert.Equal(404, (await Helper(db).ResolveAsync(student, s2, default)).StatusCode);                                         // another student: a 404, never a 403
            var probe = new Probe();
            Assert.Equal(403, (await probe.Service(db).ListChildrenAsync(student, default)).StatusCode);                                 // no family screen for a student login
            Assert.Equal(403, (await probe.Service(db).NoticesAsync(student, false, default)).StatusCode);
            var employee = new AccessCaller(A, UserA, false, false);
            Assert.Equal(403, (await Helper(db).ResolveAsync(employee, s1, default)).StatusCode);
            Assert.Equal(403, (await probe.Service(db).ListChildrenAsync(employee, default)).StatusCode);
            Assert.Equal(400, (await Helper(db).ResolveAsync(new AccessCaller(null, user, true, false), s1, default)).StatusCode);
            Assert.Equal(0, probe.Calls);
        }

        [DbFact]
        public async Task A_login_that_somehow_holds_both_roles_gets_nothing()
        {
            using var db = new TaxDb();
            var p = Parent(db, A, "GHS001GD001", Student(db, A));
            Assert.Equal(403, (await Helper(db).ResolveAsync(new AccessCaller(A, p.UserId, true, true, p.GuardianId), Student(db, A), default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ every child endpoint behind the helper
        [DbTheory, MemberData(nameof(Reads))]
        public async Task A_linked_child_reaches_the_student_side_service_and_nothing_else_does(string name)
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1);
            var p = Parent(db, A, "GHS001GD001", s1);
            var probe = new Probe();
            Assert.Equal(200, await Read(name)(probe.Service(db), Guardian(A, p), s1));
            Assert.True(probe.Calls >= 1, "the existing student-side service was used");
            var before = probe.Calls;
            foreach (var refused in new[] { s2, Student(db, B), "NO-SUCH" }) Assert.Equal(404, await Read(name)(probe.Service(db), Guardian(A, p), refused));
            Assert.Equal(before, probe.Calls);                                                                                           // the refused ones did not reach any service
            Assert.Equal(403, await Read(name)(probe.Service(db), new AccessCaller(A, UserA, false, false), s1));                 // an employee token is refused (403) before any service
            Assert.Equal(before, probe.Calls);
        }

        [DbFact]
        public async Task The_profile_hides_other_guardians_the_students_own_contact_and_points_the_photo_at_the_family_endpoint()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A);
            var p = Parent(db, A, "GHS001GD001", s1);
            var r = await new Probe().Service(db).ProfileAsync(Guardian(A, p), s1, default);
            var json = JsonSerializer.Serialize(r.Data);
            foreach (var leak in new[] { "Other Parent", "9111111111", "other@example.com", "9999999999", "child@example.com", "uardians" }) Assert.DoesNotContain(leak, json);
            Assert.Equal("/api/Family/children/S/photo?v=abc", r.Data!.ProfilePhotoUrl);
            // the response shape is exactly this (no field is added without the test noticing)
            Assert.Equal(new[] { "Address", "AcademicYearName", "AdmissionNumber", "BloodGroupName", "ClassName", "DateOfBirth", "FullName", "GenderName", "ProfilePhotoUrl", "RollNumber", "SectionName", "StatusName", "StudentId" }.OrderBy(x => x), Names<FamilyChildProfileDto>());
        }

        private static IEnumerable<string> Names<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(x => x);

        [Fact]
        public void The_family_responses_have_exactly_the_agreed_fields_and_none_that_could_carry_another_guardian_or_an_internal_remark()
        {
            Assert.Equal(new[] { "AdmissionNumber", "AcademicYearName", "AttendanceToday", "ChipState", "ClassName", "FeeDueNow", "FeeEarlierYears", "FullName", "ProfilePhotoUrl", "ReadOnly", "RollNumber", "SectionName", "StatusName", "StudentId" }.OrderBy(x => x), Names<FamilyChildCardDto>());
            Assert.Equal(new[] { "Children", "FeeTile" }, Names<FamilyChildrenDto>());
            Assert.Equal(new[] { "AllPaid", "Children", "TotalDueNow", "TotalEarlierYears" }, Names<FamilyFeeTileDto>());
            Assert.Equal(new[] { "DueNow", "EarlierYears", "FullName", "StudentId" }, Names<FamilyFeeChildDto>());
            Assert.Equal(new[] { "ExamDate", "ExamName", "TermName" }, Names<FamilyExamDto>());
            // the own-view fee DTOs the family reuses carry no concession reason and the payment has no void fields
            var banned = new[] { "Reason", "Remarks", "VoidedBy", "VoidReason", "CollectedBy", "Waive", "Note" };
            foreach (var t in new[] { typeof(StudentMyFeesDto), typeof(MyFeePaymentDto), typeof(MyFeeDueDto), typeof(FamilyChildCardDto), typeof(FamilyChildProfileDto) })
                foreach (var n in t.GetProperties().Select(p => p.Name))
                    Assert.DoesNotContain(banned, b => n.Contains(b, StringComparison.OrdinalIgnoreCase));   // (a late fee line reaches a student only when APPLIED: the student service never fills a waiver note or amount)
        }

        [DbFact]
        public async Task The_exam_schedule_shows_only_the_schemes_and_dates_of_the_childs_own_class()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A);
            var p = Parent(db, A, "GHS001GD001", s1);
            var cls = db.Scalar<string>($"select c.class_name from tb_student_enrollments e join tb_sections s on s.section_id = e.class_section_id join tb_classes c on c.class_id = s.class_id where e.student_id = '{s1}' limit 1");
            var schemes = new List<ScheduleSchemeDto>
            {
                new() { SchemeId = 1, SchemeName = "Mine", AcademicYearName = "Y", Classes = new() { new SchemeClassDto { ClassId = 7, ClassName = cls } }, Exams = new() { new ScheduleExamDto { ExamId = 1, TermName = "T1", ExamName = "Unit 1", ExamDate = new DateOnly(2026, 12, 1), ClassDates = new() { new ScheduleClassDateDto { ClassId = 7, ClassName = cls, ExamDate = new DateOnly(2026, 12, 3) } } } } },
                new() { SchemeId = 2, SchemeName = "Other class", AcademicYearName = "Y", Classes = new() { new SchemeClassDto { ClassId = 8, ClassName = "Some Other Class" } }, Exams = new() { new ScheduleExamDto { ExamId = 2, TermName = "T1", ExamName = "Secret", ExamDate = new DateOnly(2026, 12, 9) } } },
            };
            var probe = new Probe();
            var svc = new Probe { };
            var custom = Fake.Of<IReportScheduleService>(new() { ["GetAsync"] = Fake.Ok(schemes) });
            var ctx = db.Ctx(); var accounts = new ParentAccountRepository(ctx); var students = new StudentRepository(ctx); var guardians = new GuardianRepository(ctx);
            var family = new FamilyService(new StudentAccessService(accounts, students, guardians), accounts, students, guardians, probe.Students, probe.Attendance, probe.Homework, probe.Timetable, probe.Reports, custom, probe.Fees, probe.Payments, probe.Leave, probe.Photos, probe.Notices, probe.Events, NullLogger<FamilyService>.Instance);
            var r = await family.ExamScheduleAsync(Guardian(A, p), s1, default);
            var only = Assert.Single(r.Data!);
            Assert.Equal("Mine", only.SchemeName);
            Assert.Equal(new DateOnly(2026, 12, 3), Assert.Single(only.Exams).ExamDate);                                                  // the class's own date, not the general one
            Assert.DoesNotContain("Secret", JsonSerializer.Serialize(r.Data));
        }

        // ------------------------------------------------------------------------------------------------ the children list
        [DbFact]
        public async Task The_list_shows_only_this_parents_children_with_their_own_class_and_the_photo_path_of_the_family_endpoint()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1); var s3 = Student(db, A, 2);
            var p = Parent(db, A, "GHS001GD001", s1, s2);
            Parent(db, A, "GHS001GD002", s3);
            var r = await new Probe().Service(db).ListChildrenAsync(Guardian(A, p), default);
            Assert.True(r.Status);
            Assert.Equal(new[] { s1, s2 }.OrderBy(x => x), r.Data!.Children.Select(c => c.StudentId).OrderBy(x => x));
            var card = r.Data.Children.Single(c => c.StudentId == s1);
            Assert.False(string.IsNullOrEmpty(card.ClassName)); Assert.False(string.IsNullOrEmpty(card.SectionName)); Assert.Equal("Active", card.StatusName);
            Assert.Null(card.ChipState); Assert.False(card.ReadOnly);
            Assert.DoesNotContain(s3, JsonSerializer.Serialize(r.Data));
        }

        [DbFact]
        public async Task A_passed_out_child_and_a_child_not_yet_promoted_stay_visible_read_only_with_their_own_last_enrolment()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1); var s3 = Student(db, A, 2);
            var p = Parent(db, A, "GHS001GD001", s1, s2, s3);
            db.Exec($"update tb_student_enrollments set outcome = 'PASSED_OUT', outcome_at = now() where student_id = '{s1}'");
            db.Exec($"update tb_students set status_id = (select sid from lut_status where sname = 'Inactive' and stype = 'general status') where student_id = '{s1}'");
            // s2: its enrolment is in a year that is no longer the current one (the school moved on, the child is not promoted yet)
            db.Exec($"update tb_academic_years set is_current = false where academic_year_id = {TaxDb.YearA}");
            var r = (await new Probe().Service(db).ListChildrenAsync(Guardian(A, p), default)).Data!;
            var passed = r.Children.Single(c => c.StudentId == s1);
            Assert.Equal("PASSED_OUT", passed.ChipState); Assert.True(passed.ReadOnly); Assert.False(string.IsNullOrEmpty(passed.ClassName)); Assert.Null(passed.AttendanceToday);
            var pending = r.Children.Single(c => c.StudentId == s2);
            Assert.Equal("PENDING_PROMOTION", pending.ChipState); Assert.True(pending.ReadOnly); Assert.False(string.IsNullOrEmpty(pending.ClassName));
            // and the read-only child's own records can still be read
            var probe = new Probe();
            Assert.Equal(200, await Read("report-cards")(probe.Service(db), Guardian(A, p), s1));
        }

        [DbFact]
        public async Task The_fee_tile_is_Paid_only_when_every_child_is_paid_otherwise_the_total_due_now_with_who_owes_and_future_dues_do_not_count()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1);
            var p = Parent(db, A, "GHS001GD001", s1, s2);
            string Due(string s) => $@"select coalesce(sum(d.amount_due - d.amount_paid), 0) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id = d.student_fee_item_id
                where i.student_id = '{s}' and i.academic_year_id = {TaxDb.YearA} and d.due_date <= current_date + 0 and d.amount_due > d.amount_paid";
            // India date for "today"
            var today = db.Scalar<string>("select (now() at time zone 'Asia/Kolkata')::date::text");
            string DueNow(string s) => Due(s).Replace("current_date + 0", $"'{today}'::date");
            var probe = new Probe();
            var r = (await probe.Service(db).ListChildrenAsync(Guardian(A, p), default)).Data!;
            var e1 = db.Scalar<decimal>(DueNow(s1)); var e2 = db.Scalar<decimal>(DueNow(s2));
            Assert.Equal(e1, r.Children.Single(c => c.StudentId == s1).FeeDueNow);
            Assert.Equal(e2, r.Children.Single(c => c.StudentId == s2).FeeDueNow);
            Assert.Equal(e1 + e2, r.FeeTile.TotalDueNow);
            Assert.Equal(e1 + e2 == 0, r.FeeTile.AllPaid);
            Assert.Equal(new[] { (s1, e1), (s2, e2) }.Where(x => x.Item2 > 0).Select(x => x.Item1).OrderBy(x => x), r.FeeTile.Children.Select(c => c.StudentId).OrderBy(x => x));
            Assert.True(e1 + e2 > 0, "the template has dues that are already due");

            // pay everything that is due now for child 1: only child 2 is left in the breakdown
            db.Exec($"update tb_student_fee_dues d set amount_paid = amount_due from tb_student_fee_items i where i.student_fee_item_id = d.student_fee_item_id and i.student_id = '{s1}' and d.due_date <= '{today}'::date");
            var half = (await probe.Service(db).ListChildrenAsync(Guardian(A, p), default)).Data!;
            Assert.Equal(0, half.Children.Single(c => c.StudentId == s1).FeeDueNow);
            Assert.False(half.FeeTile.AllPaid);
            Assert.Equal(new[] { s2 }, half.FeeTile.Children.Select(c => c.StudentId));
            Assert.Equal(e2, half.FeeTile.TotalDueNow);

            // and when both are paid up to today the tile is Paid, even though later instalments are still to come
            db.Exec($"update tb_student_fee_dues d set amount_paid = amount_due from tb_student_fee_items i where i.student_fee_item_id = d.student_fee_item_id and i.student_id = '{s2}' and d.due_date <= '{today}'::date");
            var paid = (await probe.Service(db).ListChildrenAsync(Guardian(A, p), default)).Data!;
            Assert.True(paid.FeeTile.AllPaid); Assert.Equal(0, paid.FeeTile.TotalDueNow); Assert.Empty(paid.FeeTile.Children);
        }

        // ------------------------------------------------------------------------------------------------ earlier years (derived, nothing carried forward)
        private static FamilyService EarlierService(TaxDb db, Probe probe)
        {
            var ctx = db.Ctx(); var accounts = new ParentAccountRepository(ctx); var students = new StudentRepository(ctx); var guardians = new GuardianRepository(ctx);
            return new FamilyService(new StudentAccessService(accounts, students, guardians), accounts, students, guardians, probe.Students, probe.Attendance, probe.Homework, probe.Timetable, probe.Reports, probe.Schedule, probe.Fees, probe.Payments, probe.Leave, probe.Photos, probe.Notices, probe.Events,
                NullLogger<FamilyService>.Instance, new EarlierYearDuesRepository(ctx));
        }

        [DbFact]
        public async Task A_parents_tile_shows_each_childs_earlier_year_amount_is_not_Paid_while_any_remains_and_never_counts_another_familys_child()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEG");
            var mine = Parent(db, "ZEG", "ZEGGD1", w.P, w.R, w.L);          // promoted with override, repeated, left
            var other = Parent(db, "ZEG", "ZEGGD2", w.N);                   // someone else's child
            var probe = new Probe();
            var r = (await EarlierService(db, probe).ListChildrenAsync(new AccessCaller("ZEG", mine.UserId, false, true, mine.GuardianId), default)).Data!;
            Assert.Equal(new[] { 300m, 500m, 200m }, new[] { w.P, w.R, w.L }.Select(id => r.Children.Single(c => c.StudentId == id).FeeEarlierYears).ToArray());
            Assert.Equal(1000m, r.FeeTile.TotalEarlierYears);
            Assert.False(r.FeeTile.AllPaid);
            // every current-year due paid: the tile is STILL not Paid because earlier years remain
            db.Exec("update tb_student_fee_dues d set amount_paid = amount_due from tb_student_fee_items i, tb_academic_years y where i.student_fee_item_id = d.student_fee_item_id and y.academic_year_id = i.academic_year_id and y.is_current and d.school_id = 'ZEG'");
            var after = (await EarlierService(db, probe).ListChildrenAsync(new AccessCaller("ZEG", mine.UserId, false, true, mine.GuardianId), default)).Data!;
            Assert.Equal(0, after.FeeTile.TotalDueNow); Assert.False(after.FeeTile.AllPaid); Assert.Equal(1000m, after.FeeTile.TotalEarlierYears);
            // the other parent sees only their own child (nothing earlier owed)
            var theirs = (await EarlierService(db, probe).ListChildrenAsync(new AccessCaller("ZEG", other.UserId, false, true, other.GuardianId), default)).Data!;
            Assert.Equal(0, theirs.FeeTile.TotalEarlierYears); Assert.True(theirs.FeeTile.AllPaid);
        }

        [DbFact]
        public async Task A_students_own_fees_carry_the_earlier_years_and_a_school_admins_cross_school_read_finds_nothing()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEH");
            var ctx = db.Ctx();
            var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(ctx), new StudentFeeRepository(ctx), NullLogger<StudentFeeBillingService>.Instance, new EarlierYearDuesRepository(ctx));
            var my = new StudentMyFeesService(new StudentRepository(ctx), billing, db.Payments(), NullLogger<StudentMyFeesService>.Instance);
            var fees = (await my.GetFeesForStudentAsync("ZEH", w.P, default)).Data!;
            Assert.Equal(300, fees.EarlierYearsOutstanding);
            Assert.Equal("2025-26", Assert.Single(fees.EarlierYears).AcademicYearName);
            Assert.Equal(("2025-26", 300m), (Assert.Single(fees.EarlierDues).AcademicYearName, Assert.Single(fees.EarlierDues).Balance));
            Assert.Equal(1000, fees.Balance);                                          // this year's own balance is separate
            Assert.Equal(404, (await my.GetFeesForStudentAsync("ZEG", w.P, default)).StatusCode);   // another school's id: not found
        }

        [DbFact]
        public async Task Todays_attendance_chip_comes_from_the_attendance_service_for_that_child_only()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0);
            var p = Parent(db, A, "GHS001GD001", s1);
            var today = smss_api_service_layer.helper.SchoolClock.Today();
            var probe = new Probe();
            var seen = new List<string>();
            var att = Fake.Of<IAttendanceService>(new() { ["GetForStudentAsync"] = (Func<object?[], object?>)(a => { seen.Add((string)a[1]!); return Fake.Ok(new StudentOwnAttendanceDto { Days = new() { new StudentOwnAttendanceDayDto { Date = today, StatusCode = "PRESENT", StatusName = "Present" } } }); }) });
            var ctx = db.Ctx(); var accounts = new ParentAccountRepository(ctx); var students = new StudentRepository(ctx); var guardians = new GuardianRepository(ctx);
            var family = new FamilyService(new StudentAccessService(accounts, students, guardians), accounts, students, guardians, probe.Students, att, probe.Homework, probe.Timetable, probe.Reports, probe.Schedule, probe.Fees, probe.Payments, probe.Leave, probe.Photos, probe.Notices, probe.Events, NullLogger<FamilyService>.Instance);
            var r = await family.ListChildrenAsync(Guardian(A, p), default);
            Assert.Equal("Present", r.Data!.Children.Single().AttendanceToday);
            Assert.Equal(new[] { s1 }, seen);
        }

        // ------------------------------------------------------------------------------------------------ notices, photo, controller
        [DbFact]
        public async Task The_notices_use_the_guardians_own_login_for_the_guardian_tier_and_the_photo_goes_through_the_helper()
        {
            using var db = new TaxDb();
            var s1 = Student(db, A, 0); var s2 = Student(db, A, 1);
            var p = Parent(db, A, "GHS001GD001", s1);
            var probe = new Probe();
            Assert.Equal(200, (await probe.Service(db).NoticesAsync(Guardian(A, p), false, default)).StatusCode);
            var (ok, photo) = await probe.Service(db).PhotoAsync(Guardian(A, p), s1, default);
            Assert.True(ok.Allowed); Assert.Equal(200, photo!.StatusCode);
            var (denied, none) = await probe.Service(db).PhotoAsync(Guardian(A, p), s2, default);
            Assert.False(denied.Allowed); Assert.Null(none); Assert.Equal(404, denied.StatusCode);
        }

        [DbFact]
        public async Task The_notices_calendar_and_unread_count_are_for_a_parent_login_only_show_trimmed_fields_and_take_the_school_from_the_token()
        {
            using var db = new TaxDb();
            var p = Parent(db, A, "GHS001GD001", Student(db, A));
            var probe = new Probe(); var svc = probe.Service(db); var caller = Guardian(A, p);
            Assert.Equal(3, (await svc.UnreadNoticesAsync(caller, default)).Data!.Count);
            var n = (await svc.NoticeAsync(caller, 4, default)).Data!;
            Assert.Equal("Office", n.PostedBy);
            Assert.DoesNotContain("STUDENTS_GUARDIANS", JsonSerializer.Serialize(n)); Assert.DoesNotContain("99", JsonSerializer.Serialize(n));   // no audience tier, no author id
            var ev = Assert.Single((await svc.CalendarAsync(caller, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), default)).Data!);
            Assert.Equal("PTM", ev.Title);
            Assert.DoesNotContain("Admin", JsonSerializer.Serialize(ev));                                                              // who created the event stays internal
            var student = new AccessCaller(A, UserA, true, false);
            Assert.Equal(403, (await svc.UnreadNoticesAsync(student, default)).StatusCode);
            Assert.Equal(403, (await svc.NoticeAsync(student, 4, default)).StatusCode);
            Assert.Equal(403, (await svc.CalendarAsync(student, null, null, default)).StatusCode);
            Assert.Equal(new[] { "Description", "CategoryName", "ColorHex", "EndDate", "EndTime", "EventId", "IsAllDay", "StartDate", "StartTime", "Title" }.OrderBy(x => x), Names<FamilyEventDto>());
            Assert.Equal(new[] { "Body", "IsRead", "NoticeId", "PostedBy", "PublishedAt", "Title" }, Names<FamilyNoticeDto>());
        }

        [Fact]
        public void The_controller_takes_no_identity_from_the_request_and_every_action_is_a_read()
        {
            var actions = typeof(FamilyController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.NotEmpty(actions);
            foreach (var m in actions)
            {
                Assert.Contains(m.GetCustomAttributes(true), a => a is HttpGetAttribute);
                Assert.DoesNotContain(m.GetCustomAttributes(true), a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute);
                foreach (var prm in m.GetParameters())
                    Assert.DoesNotContain(new[] { "guardianId", "schoolId", "userId", "guardian", "school" }, bad => string.Equals(prm.Name, bad, StringComparison.OrdinalIgnoreCase));   // a child id only; nothing that names a guardian or a school
            }
            Assert.Contains(typeof(FamilyController).GetCustomAttributes(true), a => a.GetType().Name == "AuthorizeAttribute");
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task The_controller_hands_the_helper_only_what_the_token_says_and_a_student_login_gets_the_service_refusal(string role)
        {
            var svc = Fake.Of<IFamilyService>(new() { ["ListChildrenAsync"] = Fake.Ok(new FamilyChildrenDto()) });
            var user = Fake.User(A, 77, role);
            var c = Fake.WithUser(new FamilyController(svc), user);
            Assert.Equal(200, Fake.Code(await c.Children(default)));
            Assert.Contains("ListChildrenAsync", Fake.ProxyOf(svc).Calls);      // every role reaches the service, which does the deciding with the token's roles (tested above)
            var noSchool = Fake.WithUser(new FamilyController(Fake.Of<IFamilyService>(new() { ["ListChildrenAsync"] = Fake.Ok(new FamilyChildrenDto()) })), Fake.User(null, 77, role));
            Assert.NotEqual(500, Fake.Code(await noSchool.Children(default)));
        }
    }

    /// <summary>[Theory] over named cases that need a database: skipped like [DbFact] when no scratch server is configured.</summary>
    public sealed class DbTheoryAttribute : TheoryAttribute
    {
        public DbTheoryAttribute()
        {
            if (TaxDb.Server == null) Skip = "Set SMSS_TEST_PG (a scratch PostgreSQL) and SMSS_TEST_TEMPLATE to run the database tests.";
        }
    }
}
