using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S7: copy configuration from one year to another. Proves each part, the merge rules (skip what exists, never overwrite or delete, no duplicate masters, the source is
    /// untouched), eligibility and inactive rules, an idempotent re-run, a preview that writes nothing, the closed / inactive / same-year refusals, concurrent copies and tenant isolation.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the S1 to S7 RUN-ME scripts applied.
    /// </summary>
    public class AcademicYearCopyS7Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";
        private static readonly string[] All = { "CLASSES_SECTIONS", "SUBJECTS", "SCHEDULES", "CLASS_TEACHERS", "TIMETABLE" };

        private static YearCopyService Copy(TaxDb db) => new(new YearCopyRepository(db.Ctx()), NullLogger<YearCopyService>.Instance);
        private static TimetableVersionService Tt(TaxDb db) => new(new TimetableRepository(db.Ctx()), NullLogger<TimetableVersionService>.Instance);
        private static YearCopyRequest Req(string[]? parts = null, bool confirm = true) => new() { Parts = (parts ?? All).ToList(), Confirm = confirm };
        private static TimeOnly Tm(string s) => TimeOnly.Parse(s);

        private sealed class World
        {
            public string School = "";
            public long Y1, Y2, C1, C2, C3, A, B, A2, Maths, Science, Art, Music, Regular, Saturday, P1, P2, P3;
            public string T1 = "", T2 = "", T3 = "";
        }

        private static string Teacher(TaxDb db, string school, string tag)
        {
            var id = "ZT" + school[^5..] + tag;
            if (db.Scalar<int>($"select count(*) from tb_employee_designations where school_id='{school}'") == 0)
            {
                db.Exec($"insert into tb_employee_designations (school_id, designation_name, status_id) values ('{school}', 'Teacher', {Active})");
                db.Exec($"insert into tb_employee_departments (school_id, department_name, status_id) values ('{school}', 'Dept', {Active})");
            }
            db.Exec($@"insert into tb_employees (employee_id, school_id, designation_id, department_id, employment_status_id, employee_type, first_name, last_name, date_of_birth, mobile_number, address, date_of_joining, status_id)
                       values ('{id}', '{school}', (select min(designation_id) from tb_employee_designations where school_id='{school}'), (select min(department_id) from tb_employee_departments where school_id='{school}'),
                               (select min(employment_status_id) from lut_employment_status), 'Teaching', 'T', 'Teacher{tag}', '1990-01-01', '9876543210', 'x', '2020-01-01', {Active})");
            db.Exec($"insert into tb_users (school_id, user_type, org_user_id, display_name, password_hash, is_first_login, status_id) values ('{school}', 'School Id', '{id}', 'Teacher {tag}', 'x', false, {Active})");
            db.Exec($"insert into tb_user_roles (user_id, role_id) select user_id, 4 from tb_users where org_user_id='{id}'");
            return id;
        }

        /// <summary>
        /// 2026-27 (source): Class 1 (sections A, B), Class 2 (section A2), an inactive Class 3; subjects Maths, Science, Art and an inactive Music; schedules Regular (default) and Saturday;
        /// class teachers T1 (A), T2 (B), T3 (A2); active timetables for A, B and A2. 2027-28 (target) is Active, current is the source, and the target has nothing yet.
        /// </summary>
        private static async Task<World> BuildAsync(TaxDb db, string id, bool sourceCurrent = true)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Copy test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', {sourceCurrent.ToString().ToLower()}, {Active}), ('{id}', '2027-28', '2027-04-01', '2028-03-31', false, {Active})");
            var w = new World { School = id };
            w.Y1 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2026-27'");
            w.Y2 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2027-28'");
            var classes = new ClassService(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
            var sections = new SectionService(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
            long C(string n, int o) => ((ClassResponse)classes.CreateAsync(id, new CreateClassRequest { ClassName = n, SequenceOrder = o }).Result.Data!).ClassId;
            long S(long cls, string n) => ((SectionResponse)sections.CreateAsync(id, new CreateSectionRequest { ClassId = cls, SectionName = n }).Result.Data!).SectionId;
            (w.C1, w.C2, w.C3) = (C("Class 1", 1), C("Class 2", 2), C("Class 3", 3));
            (w.A, w.B, w.A2) = (S(w.C1, "A"), S(w.C1, "B"), S(w.C2, "A"));
            db.Exec($"update tb_academic_year_sections set max_strength=35 where academic_year_id={w.Y1} and section_id={w.A}");
            foreach (var n in new[] { "Maths", "Science", "Art", "Music" }) db.Exec($"insert into tb_subjects (school_id, subject_name, subject_code, status_id) values ('{id}', '{n}', '{n[..3].ToUpper()}', {Active})");
            long Sub(string n) => db.Scalar<long>($"select subject_id from tb_subjects where school_id='{id}' and subject_name='{n}'");
            (w.Maths, w.Science, w.Art, w.Music) = (Sub("Maths"), Sub("Science"), Sub("Art"), Sub("Music"));
            void Map(long cls, long sub) => db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{id}', {w.Y1}, {cls}, {sub}, {Active})");
            foreach (var s in new[] { w.Maths, w.Science, w.Art, w.Music }) Map(w.C1, s);
            Map(w.C2, w.Maths);
            db.Exec($"update tb_subjects set status_id={Inactive} where subject_id={w.Music}");   // mapped last year, inactive now
            db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{id}', {w.Y1}, {w.C1}, {w.A}, {w.Science}, 'REMOVE', {Active}), ('{id}', {w.Y1}, {w.C2}, {w.A2}, {w.Art}, 'ADD', {Active})");
            db.Exec($"update tb_classes set status_id={Inactive} where class_id={w.C3}");
            (w.T1, w.T2, w.T3) = (Teacher(db, id, "1"), Teacher(db, id, "2"), Teacher(db, id, "3"));
            var sched = new PeriodScheduleService(new PeriodRepository(db.Ctx()), NullLogger<PeriodScheduleService>.Instance);
            w.Regular = sched.CreateAsync(id, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "Regular" }, default).Result.Data!.ScheduleId;
            w.Saturday = sched.CreateAsync(id, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "Saturday" }, default).Result.Data!.ScheduleId;
            foreach (var (n, a, b) in new[] { (1, "09:00", "09:45"), (2, "09:45", "10:30"), (3, "10:30", "11:15") })
                Assert.True(sched.AddSlotAsync(id, w.Regular, new ScheduleSlotRequest { SlotType = "PERIOD", PeriodNumber = n, StartTime = Tm(a), EndTime = Tm(b) }, default).Result.Status);
            Assert.True(sched.AddSlotAsync(id, w.Saturday, new ScheduleSlotRequest { SlotType = "PERIOD", PeriodNumber = 1, StartTime = Tm("08:00"), EndTime = Tm("08:40") }, default).Result.Status);
            long P(int n) => db.Scalar<long>($"select period_id from tb_periods where schedule_id={w.Regular} and period_number={n}");
            (w.P1, w.P2, w.P3) = (P(1), P(2), P(3));
            db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{id}', {w.Y1}, null, 6, {w.Saturday}), ('{id}', {w.Y1}, {w.C2}, null, {w.Regular})");
            long At(long section, string name, params (int d, long p, long s, string? t)[] slots)
            {
                var tt = Tt(db);
                var c = tt.CreateAsync(id, TaxDb.UserA, new CreateTimetableRequest { AcademicYearId = w.Y1, SectionId = section, TimetableName = name }, default).Result;
                Assert.True(c.Status, c.Message);
                foreach (var s in slots) Assert.True(tt.AssignAsync(id, TaxDb.UserA, c.Data!.TimetableId, new AssignSlotRequest { DayOfWeek = s.d, PeriodId = s.p, SubjectId = s.s, EmployeeId = s.t }, default).Result.Status);
                Assert.True(tt.ActivateAsync(id, TaxDb.UserA, c.Data!.TimetableId, default).Result.Status);
                return c.Data.TimetableId;
            }
            At(w.A, "Regular timetable", (1, w.P1, w.Maths, w.T1), (2, w.P1, w.Art, w.T1), (3, w.P2, w.Maths, w.T1));
            At(w.B, "Regular timetable", (1, w.P1, w.Maths, w.T2), (1, w.P2, w.Science, w.T2));
            At(w.A2, "Class 2 plan", (1, w.P1, w.Maths, w.T3));
            foreach (var (sec, t) in new[] { (w.A, w.T1), (w.B, w.T2), (w.A2, w.T3) })
                db.Exec($@"insert into tb_class_teacher_assignments (school_id, academic_year_id, class_id, section_id, employee_id, status_id, effective_from) select '{id}', {w.Y1}, class_id, section_id, '{t}', {Active}, '2026-04-01' from tb_sections where section_id={sec}");
            return w;
        }

        private static int N(TaxDb db, string sql) => db.Scalar<int>(sql);
        private static string[] Tables = { "tb_academic_year_classes", "tb_academic_year_sections", "tb_class_subjects", "tb_section_subject_overrides", "tb_academic_year_schedules", "tb_class_teacher_assignments", "tb_timetables", "tb_timetable_entries" };
        private static string Fingerprint(TaxDb db, World w, long year) =>
            string.Join("|", Tables.Select(t => db.Scalar<string>($"select coalesce(md5(string_agg(x::text, ',' order by x::text)), 'none') from {t} x where x.academic_year_id={year} and x.school_id='{w.School}'")));
        private static string MasterCounts(TaxDb db, string school) =>
            string.Join(",", new[] { "tb_classes", "tb_sections", "tb_subjects", "tb_periods", "tb_period_schedules" }.Select(t => db.Scalar<int>($"select count(*) from {t} where school_id='{school}'")));
        private static int Created(ApiResponse<YearCopyResultDto> r) => r.Data!.Parts.Sum(p => p.Created);

        // ---------------------------------------------------------------- preview, each part, the rules
        [DbFact]
        public async Task A_preview_reports_every_part_and_writes_nothing()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000001");
            db.Exec($"update tb_employees set status_id={Inactive} where employee_id='{w.T2}'");
            var before = (Fingerprint(db, w, w.Y2), N(db, "select count(*) from tb_year_copy_log"), MasterCounts(db, w.School));
            var r = await Copy(db).PreviewAsync(w.School, w.Y2, w.Y1, Req(confirm: false), default);
            Assert.True(r.Status, r.Message);
            Assert.True(r.Data!.Preview);
            Assert.Null(r.Data.CopyId);
            Assert.Equal((Fingerprint(db, w, w.Y2), N(db, "select count(*) from tb_year_copy_log"), MasterCounts(db, w.School)), before);   // nothing written
            var p = r.Data.Parts.ToDictionary(x => x.Part);
            Assert.Equal(new[] { "CLASSES_SECTIONS", "SUBJECTS", "SCHEDULES", "CLASS_TEACHERS", "TIMETABLE" }, r.Data.Parts.Select(x => x.Part));
            Assert.Equal(2 + 3, p["CLASSES_SECTIONS"].Created);                        // classes 1 and 2, sections A, B, A2
            Assert.Contains(p["CLASSES_SECTIONS"].Skipped, s => s.Reason == "The class is inactive");   // Class 3 (offered last year, inactive now)
            Assert.Equal(4 + 2, p["SUBJECTS"].Created);                                // Class 1: Maths, Science, Art; Class 2: Maths; two exceptions
            Assert.Contains(p["SUBJECTS"].Skipped, s => s.Reason == "The subject is inactive" && s.Examples.Any(e => e.Contains("Music")));
            Assert.Equal(2, p["SCHEDULES"].Created);
            Assert.Equal(2, p["CLASS_TEACHERS"].Created);                              // T1 and T3; T2 is inactive
            Assert.Contains(p["CLASS_TEACHERS"].Skipped, s => s.Reason.Contains("no longer an active teaching employee"));
            Assert.Equal(3, p["TIMETABLE"].Details.Single(d => d.Kind == "timetables (drafts)").Count);
        }

        [DbFact]
        public async Task Apply_copies_every_part_with_the_stated_rules_and_logs_one_row()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000002");
            db.Exec($"update tb_employees set status_id={Inactive} where employee_id='{w.T2}'");
            var masters = MasterCounts(db, w.School);
            var source = Fingerprint(db, w, w.Y1);
            var r = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.True(r.Status, r.Message);
            Assert.NotNull(r.Data!.CopyId);

            // 1 offerings with capacity; the inactive class is not copied
            Assert.Equal(2, N(db, $"select count(*) from tb_academic_year_classes where academic_year_id={w.Y2} and status_id={Active}"));
            Assert.Equal(0, N(db, $"select count(*) from tb_academic_year_classes where academic_year_id={w.Y2} and class_id={w.C3}"));
            Assert.Equal(3, N(db, $"select count(*) from tb_academic_year_sections where academic_year_id={w.Y2}"));
            Assert.Equal(35, N(db, $"select max_strength from tb_academic_year_sections where academic_year_id={w.Y2} and section_id={w.A}"));
            // 2 subjects and exceptions; the inactive subject is not copied
            Assert.Equal(4, N(db, $"select count(*) from tb_class_subjects where academic_year_id={w.Y2}"));
            Assert.Equal(0, N(db, $"select count(*) from tb_class_subjects where academic_year_id={w.Y2} and subject_id={w.Music}"));
            Assert.Equal("REMOVE", db.Scalar<string>($"select action from tb_section_subject_overrides where academic_year_id={w.Y2} and section_id={w.A}"));
            Assert.Equal("ADD", db.Scalar<string>($"select action from tb_section_subject_overrides where academic_year_id={w.Y2} and section_id={w.A2}"));
            // 3 schedule assignments
            Assert.Equal(2, N(db, $"select count(*) from tb_academic_year_schedules where academic_year_id={w.Y2}"));
            // 4 class teachers: only the eligible ones, starting the day the target year starts, with the reason
            Assert.Equal(new[] { w.T1, w.T3 }.OrderBy(x => x), db.Scalar<string>($"select string_agg(employee_id, ',' order by employee_id) from tb_class_teacher_assignments where academic_year_id={w.Y2}").Split(',').OrderBy(x => x));
            Assert.Equal(2, N(db, $"select count(*) from tb_class_teacher_assignments where academic_year_id={w.Y2} and effective_from='2027-04-01' and effective_to is null and assign_reason='Copied from 2026-27'"));
            // 5 timetables as DRAFTS, never active; the inactive teacher's slots need a teacher
            Assert.Equal(3, N(db, $"select count(*) from tb_timetables where academic_year_id={w.Y2} and state='DRAFT' and not is_active and not ever_active"));
            Assert.Equal(0, N(db, $"select count(*) from tb_timetable_entries where academic_year_id={w.Y2} and tt_is_active"));
            Assert.Equal(2, N(db, $"select count(*) from tb_timetable_entries e join tb_timetables t on t.timetable_id=e.timetable_id where e.academic_year_id={w.Y2} and t.section_id={w.B} and e.needs_teacher and e.employee_id is null"));
            Assert.Equal(3, N(db, $"select count(*) from tb_timetable_entries e join tb_timetables t on t.timetable_id=e.timetable_id where e.academic_year_id={w.Y2} and t.section_id={w.A} and e.employee_id='{w.T1}'"));
            // never a master, never the source
            Assert.Equal(masters, MasterCounts(db, w.School));
            Assert.Equal(source, Fingerprint(db, w, w.Y1));
            // the log
            var log = (await Copy(db).GetLogAsync(w.School, w.Y2, default)).Data!;
            Assert.Single(log);
            Assert.Equal("COMPLETED", log[0].Status);
            Assert.Equal(All, log[0].Parts);
            Assert.Equal("2026-27", log[0].SourceYearName);
            Assert.True(log[0].Result.Sum(x => x.Created) > 20);
            Assert.Equal(1, N(db, $"select count(*) from tb_year_copy_log where school_id='{w.School}' and source_year_id={w.Y1} and target_year_id={w.Y2} and run_by={TaxDb.UserA}"));
        }

        [DbFact]
        public async Task Running_the_same_copy_again_creates_nothing_and_changes_nothing()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000003");
            Assert.True(Created(await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default)) > 0);
            var after = Fingerprint(db, w, w.Y2);
            var again = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.True(again.Status, again.Message);
            Assert.Equal(0, Created(again));
            Assert.Contains("Nothing new", again.Message);
            Assert.True(again.Data!.Parts.All(p => p.SkippedExisting > 0 || p.Part == "TIMETABLE" || p.Skipped.Count > 0));
            Assert.Equal(after, Fingerprint(db, w, w.Y2));
            Assert.Equal(2, N(db, $"select count(*) from tb_year_copy_log where school_id='{w.School}'"));   // both real runs are logged
        }

        [DbFact]
        public async Task Merging_into_a_target_that_already_has_data_never_overwrites_or_deletes()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000004");
            // the target already offers Class 1 and section A with ANOTHER capacity, teaches Class 1 only Maths, has a class teacher for A, and a timetable for B
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) values ('{w.School}', {w.Y2}, {w.C1}, {Active})");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id) values ('{w.School}', {w.Y2}, {w.C1}, {w.A}, 20, {Active})");
            db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{w.School}', {w.Y2}, {w.C1}, {w.Maths}, {Active})");
            db.Exec($"insert into tb_class_teacher_assignments (school_id, academic_year_id, class_id, section_id, employee_id, status_id, effective_from) values ('{w.School}', {w.Y2}, {w.C1}, {w.A}, '{w.T3}', {Active}, '2027-06-01')");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) values ('{w.School}', {w.Y2}, {w.C1}, {w.B}, {Active})");
            var own = await Tt(db).CreateAsync(w.School, TaxDb.UserA, new CreateTimetableRequest { AcademicYearId = w.Y2, SectionId = w.B, TimetableName = "My own plan" }, default);
            Assert.True(own.Status, own.Message);
            var ownRows = db.Scalar<string>($"select md5(string_agg(x::text, ',' order by x::text)) from tb_timetables x where timetable_id={own.Data!.TimetableId}");

            var r = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(20, N(db, $"select max_strength from tb_academic_year_sections where academic_year_id={w.Y2} and section_id={w.A}"));        // not overwritten
            Assert.Equal(1, N(db, $"select count(*) from tb_class_subjects where academic_year_id={w.Y2} and class_id={w.C1} and subject_id={w.Maths}"));   // not duplicated
            Assert.Equal(3, N(db, $"select count(*) from tb_class_subjects where academic_year_id={w.Y2} and class_id={w.C1}"));                          // Science and Art added
            Assert.Equal(w.T3, db.Scalar<string>($"select employee_id from tb_class_teacher_assignments where academic_year_id={w.Y2} and section_id={w.A}"));    // A keeps its own teacher
            Assert.Equal(1, N(db, $"select count(*) from tb_class_teacher_assignments where academic_year_id={w.Y2} and section_id={w.A}"));
            Assert.Equal(ownRows, db.Scalar<string>($"select md5(string_agg(x::text, ',' order by x::text)) from tb_timetables x where timetable_id={own.Data.TimetableId}"));   // B's own timetable untouched
            Assert.Equal(1, N(db, $"select count(*) from tb_timetables where academic_year_id={w.Y2} and section_id={w.B}"));                              // and nothing was added beside it
            Assert.Equal(2, N(db, $"select count(*) from tb_timetables where academic_year_id={w.Y2} and section_id in ({w.A}, {w.A2})"));
            Assert.Contains(r.Data!.Parts.Single(p => p.Part == "CLASS_TEACHERS").Details, d => d.Kind == "class teachers" && d.Count == 2);   // B and A2
            Assert.True(r.Data.Parts.Single(p => p.Part == "CLASSES_SECTIONS").SkippedExisting >= 2);
        }

        [DbFact]
        public async Task A_part_that_depends_on_an_unselected_part_copies_only_what_the_target_already_offers()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000005");
            var first = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(new[] { "SUBJECTS", "SCHEDULES", "CLASS_TEACHERS", "TIMETABLE" }), default);
            Assert.True(first.Status, first.Message);
            Assert.Equal(0, N(db, $"select count(*) from tb_class_subjects where academic_year_id={w.Y2}"));               // no class offered: nothing to hang subjects on
            Assert.Equal(0, N(db, $"select count(*) from tb_class_teacher_assignments where academic_year_id={w.Y2}"));
            Assert.Equal(0, N(db, $"select count(*) from tb_timetables where academic_year_id={w.Y2}"));
            Assert.Contains(first.Data!.Parts.Single(p => p.Part == "SUBJECTS").Skipped, s => s.Reason == "The class is not offered in the target year");
            Assert.Contains(first.Data.Parts.Single(p => p.Part == "CLASS_TEACHERS").Skipped, s => s.Reason == "The section is not offered in the target year");
            // a later run with the offerings fills the rest in
            Assert.True((await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(new[] { "CLASSES_SECTIONS" }), default)).Status);
            var second = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.True(second.Data!.Parts.Single(p => p.Part == "SUBJECTS").Created > 0);
            Assert.Equal(3, N(db, $"select count(*) from tb_class_teacher_assignments where academic_year_id={w.Y2}"));   // all three teachers are eligible here
            Assert.Equal(3, N(db, $"select count(*) from tb_timetables where academic_year_id={w.Y2}"));
        }

        [DbFact]
        public async Task Timetable_slots_that_no_longer_fit_the_target_year_are_left_out_with_the_reason()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000006");
            // in the target year Art is not taught in Class 1 (its mapping is copied minus Art) and an inactive period
            Assert.True((await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(new[] { "CLASSES_SECTIONS" }), default)).Status);
            db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{w.School}', {w.Y2}, {w.C1}, {w.Maths}, {Active}), ('{w.School}', {w.Y2}, {w.C1}, {w.Science}, {Active})");
            db.Exec($"update tb_periods set status_id={Inactive} where period_id={w.P2}");
            var r = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(new[] { "TIMETABLE" }), default);
            Assert.True(r.Status, r.Message);
            var part = r.Data!.Parts.Single();
            Assert.Contains(part.Skipped, s => s.Reason == "The subject is not taught in this section in the target year" && s.Examples.Any(e => e.Contains("Class 1 - A")));
            Assert.Contains(part.Skipped, s => s.Reason == "The period is no longer available");
            Assert.Equal(1, N(db, $"select count(*) from tb_timetable_entries e join tb_timetables t on t.timetable_id=e.timetable_id where t.academic_year_id={w.Y2} and t.section_id={w.A}"));   // only Monday period 1 Maths survives
        }

        // ---------------------------------------------------------------- the target and the request
        [DbFact]
        public async Task A_closed_or_inactive_target_the_same_year_unknown_parts_and_a_missing_confirm_are_refused()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000007");
            var svc = Copy(db);
            Assert.Equal(400, (await svc.ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(confirm: false), default)).StatusCode);
            Assert.Equal(400, (await svc.ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y2, Req(), default)).StatusCode);
            Assert.Equal(400, (await svc.PreviewAsync(w.School, w.Y2, w.Y1, new YearCopyRequest { Parts = new() { "STUDENTS" } }, default)).StatusCode);
            Assert.Equal(400, (await svc.PreviewAsync(w.School, w.Y2, w.Y1, new YearCopyRequest { Parts = new() }, default)).StatusCode);
            db.Exec($"update tb_academic_years set status_id={Inactive} where academic_year_id={w.Y2}");
            Assert.Equal(409, (await svc.PreviewAsync(w.School, w.Y2, w.Y1, Req(), default)).StatusCode);
            db.Exec($"update tb_academic_years set status_id={Active} where academic_year_id={w.Y2}");
            db.Exec($"update tb_academic_years set closed_at=now() where academic_year_id={w.Y2}");
            var closed = await svc.ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.Equal(409, closed.StatusCode);
            Assert.Contains("closed", closed.Message);
            Assert.Equal(409, (await svc.PreviewAsync(w.School, w.Y2, w.Y1, Req(), default)).StatusCode);
            Assert.Equal(0, N(db, $"select count(*) from tb_academic_year_classes where academic_year_id={w.Y2}"));
            Assert.Equal(0, N(db, "select count(*) from tb_year_copy_log"));
            // a CLOSED SOURCE is fine
            db.Exec($"update tb_academic_years set closed_at=null where academic_year_id={w.Y2}");
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.Y1}");
            db.Exec($"update tb_academic_years set closed_at=now() where academic_year_id={w.Y1}");
            var fromClosed = await svc.ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.True(fromClosed.Status, fromClosed.Message);
            Assert.True(Created(fromClosed) > 0);
        }

        // ---------------------------------------------------------------- concurrency and tenants
        [DbFact]
        public async Task Four_simultaneous_copies_for_one_school_create_each_row_exactly_once()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS7000008");
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(1, results.Count(r => Created(r) > 0));          // the first through the lock does the work, the others find it done
            Assert.Equal(3, N(db, $"select count(*) from tb_timetables where academic_year_id={w.Y2}"));
            Assert.Equal(5, N(db, $"select count(*) from tb_academic_year_sections where academic_year_id={w.Y2}") + N(db, $"select count(*) from tb_academic_year_classes where academic_year_id={w.Y2}") );
            Assert.Equal(3, N(db, $"select count(*) from tb_class_teacher_assignments where academic_year_id={w.Y2}"));
            Assert.Equal(4, N(db, $"select count(*) from tb_year_copy_log where school_id='{w.School}'"));
        }

        [DbFact]
        public async Task Another_schools_years_and_rows_are_never_reachable_or_copied()
        {
            using var db = new TaxDb();
            var a = await BuildAsync(db, "ZZS7000009");
            var b = await BuildAsync(db, "ZZS7000010");
            var svc = Copy(db);
            Assert.Equal(404, (await svc.PreviewAsync(a.School, b.Y2, a.Y1, Req(), default)).StatusCode);   // the target belongs to B
            Assert.Equal(404, (await svc.PreviewAsync(a.School, a.Y2, b.Y1, Req(), default)).StatusCode);   // the source belongs to B
            Assert.Equal(404, (await svc.ApplyAsync(a.School, TaxDb.UserA, b.Y2, b.Y1, Req(), default)).StatusCode);   // both are B's
            Assert.Equal(404, (await svc.GetLogAsync(a.School, b.Y2, default)).StatusCode);
            Assert.Equal(0, N(db, $"select count(*) from tb_academic_year_classes where academic_year_id={b.Y2}"));
            // a legitimate copy for A leaves B alone and copies none of B's rows
            var bBefore = Fingerprint(db, b, b.Y2) + Fingerprint(db, b, b.Y1);
            Assert.True((await svc.ApplyAsync(a.School, TaxDb.UserA, a.Y2, a.Y1, Req(), default)).Status);
            Assert.Equal(bBefore, Fingerprint(db, b, b.Y2) + Fingerprint(db, b, b.Y1));
            Assert.Equal(0, N(db, $"select count(*) from tb_academic_year_classes where academic_year_id={a.Y2} and school_id <> '{a.School}'"));
            Assert.Equal(0, N(db, $"select count(*) from tb_year_copy_log where school_id='{b.School}'"));
        }

        [DbFact]
        public async Task The_database_refuses_a_log_row_that_names_one_year_twice_or_another_schools_year()
        {
            using var db = new TaxDb();
            var a = await BuildAsync(db, "ZZS7000011");
            var b = await BuildAsync(db, "ZZS7000012");
            string Ins(long s, long t, string school) => $"insert into tb_year_copy_log (school_id, source_year_id, target_year_id, parts, result, status) values ('{school}', {s}, {t}, '[]', '[]', 'COMPLETED')";
            Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, Assert.Throws<Npgsql.PostgresException>(() => db.Exec(Ins(a.Y1, a.Y1, a.School))).SqlState);
            Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, Assert.Throws<Npgsql.PostgresException>(() => db.Exec(Ins(a.Y1, b.Y2, a.School))).SqlState);
            await Task.CompletedTask;
        }
    }
}
