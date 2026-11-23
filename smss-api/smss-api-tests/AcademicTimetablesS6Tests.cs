using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S6: timetable VERSIONS (several per section and year, exactly one ACTIVE). Proves the backfill, that every reader sees only the ACTIVE timetable,
    /// activation / archive / delete rules, teacher double-booking among ACTIVE timetables (service message AND the database index), "apply to other sections",
    /// concurrent activation and concurrent bookings, closed year, tenant isolation, and what the database itself refuses.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the S1 to S6 RUN-ME scripts applied.
    /// </summary>
    public class AcademicTimetablesS6Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";
        private static readonly string[] Backfilled = { "APS261013001", "GHS261103002", "LAS261103001" };

        private static TimetableVersionService V(TaxDb db) => new(new TimetableRepository(db.Ctx()), NullLogger<TimetableVersionService>.Instance);
        private static TimetableService T(TaxDb db) => new(new TimetableRepository(db.Ctx()), NullLogger<TimetableService>.Instance);
        private static void Expect(string sqlState, Action a) => Assert.Equal(sqlState, Assert.Throws<PostgresException>(a).SqlState);
        private static TimeOnly Tm(string s) => TimeOnly.Parse(s);

        private sealed class World
        {
            public string School = "";
            public long Year, Class1, Class2, SecA, SecB, SecC, SecD, Maths, Science, Art, Regular, P1, P2, P3;
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
            return id;
        }

        /// <summary>Class 1 (sections A, B, C) and Class 2 (section D); Maths, Science, Art taught in both classes; schedule "Regular" with periods 1 to 3; three teachers. No timetable yet.</summary>
        private static async Task<World> BuildAsync(TaxDb db, string id)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Timetable test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");
            var w = new World { School = id, Year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}'") };
            var classes = new ClassService(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
            var sections = new SectionService(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
            long C(string n, int o) => ((ClassResponse)classes.CreateAsync(id, new CreateClassRequest { ClassName = n, SequenceOrder = o }).Result.Data!).ClassId;
            long S(long cls, string n) => ((SectionResponse)sections.CreateAsync(id, new CreateSectionRequest { ClassId = cls, SectionName = n }).Result.Data!).SectionId;
            (w.Class1, w.Class2) = (C("Class 1", 1), C("Class 2", 2));
            (w.SecA, w.SecB, w.SecC, w.SecD) = (S(w.Class1, "A"), S(w.Class1, "B"), S(w.Class1, "C"), S(w.Class2, "A"));
            foreach (var n in new[] { "Maths", "Science", "Art" }) db.Exec($"insert into tb_subjects (school_id, subject_name, subject_code, status_id) values ('{id}', '{n}', '{n[..3].ToUpper()}', {Active})");
            long Sub(string n) => db.Scalar<long>($"select subject_id from tb_subjects where school_id='{id}' and subject_name='{n}'");
            (w.Maths, w.Science, w.Art) = (Sub("Maths"), Sub("Science"), Sub("Art"));
            db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) select '{id}', {w.Year}, c, s, {Active} from (values ({w.Class1}), ({w.Class2})) a(c), (values ({w.Maths}), ({w.Science}), ({w.Art})) b(s)");
            (w.T1, w.T2, w.T3) = (Teacher(db, id, "1"), Teacher(db, id, "2"), Teacher(db, id, "3"));
            var sched = new PeriodScheduleService(new PeriodRepository(db.Ctx()), NullLogger<PeriodScheduleService>.Instance);
            w.Regular = sched.CreateAsync(id, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "Regular" }, default).Result.Data!.ScheduleId;
            foreach (var (n, a, b) in new[] { (1, "09:00", "09:45"), (2, "09:45", "10:30"), (3, "10:30", "11:15") })
                Assert.True((await sched.AddSlotAsync(id, w.Regular, new ScheduleSlotRequest { SlotType = "PERIOD", PeriodNumber = n, StartTime = Tm(a), EndTime = Tm(b) }, default)).Status);
            long P(int n) => db.Scalar<long>($"select period_id from tb_periods where schedule_id={w.Regular} and period_number={n}");
            (w.P1, w.P2, w.P3) = (P(1), P(2), P(3));
            return w;
        }

        private static AssignSlotRequest Slot(int day, long period, long subject, string? teacher) => new() { DayOfWeek = day, PeriodId = period, SubjectId = subject, EmployeeId = teacher };

        /// <summary>A timetable that is ACTIVE with the given slots (created through the services, so it is a real one).</summary>
        private static async Task<long> ActiveWith(TaxDb db, World w, long section, string name, params (int day, long period, long subject, string? teacher)[] slots)
        {
            var created = await V(db).CreateAsync(w.School, TaxDb.UserA, new CreateTimetableRequest { SectionId = section, TimetableName = name }, default);
            Assert.True(created.Status, created.Message);
            var id = created.Data!.TimetableId;
            foreach (var s in slots) Assert.True((await V(db).AssignAsync(w.School, TaxDb.UserA, id, Slot(s.day, s.period, s.subject, s.teacher), default)).Status);
            var act = await V(db).ActivateAsync(w.School, TaxDb.UserA, id, default);
            Assert.True(act.Status, act.Message);
            return id;
        }

        private static async Task<long> Draft(TaxDb db, World w, long section, string name, long? clone = null, params (int day, long period, long subject, string? teacher)[] slots)
        {
            var created = await V(db).CreateAsync(w.School, TaxDb.UserA, new CreateTimetableRequest { SectionId = section, TimetableName = name, CloneFromTimetableId = clone }, default);
            Assert.True(created.Status, created.Message);
            foreach (var s in slots) Assert.True((await V(db).AssignAsync(w.School, TaxDb.UserA, created.Data!.TimetableId, Slot(s.day, s.period, s.subject, s.teacher), default)).Status);
            return created.Data!.TimetableId;
        }

        // ---------------------------------------------------------------- the backfill
        [DbFact]
        public async Task After_the_backfill_every_section_with_entries_has_one_active_Regular_timetable_and_every_entry_sits_on_it()
        {
            using var db = new TaxDb();
            var expected = new Dictionary<string, int> { ["APS261013001"] = 26, ["GHS261103002"] = 540, ["LAS261103001"] = 541 };
            foreach (var school in Backfilled)
            {
                Assert.Equal(expected[school], db.Scalar<int>($"select count(*) from tb_timetable_entries where school_id='{school}'"));
                var pairs = db.Scalar<int>($"select count(distinct (academic_year_id, section_id)) from tb_timetable_entries where school_id='{school}'");
                Assert.Equal(pairs, db.Scalar<int>($"select count(*) from tb_timetables where school_id='{school}'"));
                Assert.Equal(pairs, db.Scalar<int>($"select count(*) from tb_timetables where school_id='{school}' and is_active and state='ACTIVE' and ever_active and timetable_name='Regular timetable'"));
                Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetable_entries e join tb_timetables t on t.timetable_id=e.timetable_id where e.school_id='{school}' and e.tt_is_active is distinct from t.is_active"));
                Assert.Equal(0, db.Scalar<int>($"select count(*) from (select academic_year_id, section_id from tb_timetables where school_id='{school}' and is_active group by 1,2 having count(*) <> 1) q"));
                // the section grid still carries every Active entry the SQL says
                var year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{school}' and is_current");
                var sec = db.Scalar<long>($"select section_id from tb_timetable_entries where school_id='{school}' and academic_year_id={year} and status_id={Active} group by section_id order by count(*) desc limit 1");
                var grid = (await T(db).GetClassGridAsync(school, sec, year, default)).Data!;
                Assert.Equal(db.Scalar<int>($"select count(*) from tb_timetable_entries where section_id={sec} and academic_year_id={year} and status_id={Active}"), grid.EntryCount);
                Assert.Equal("ACTIVE", grid.TimetableState);
            }
        }

        // ---------------------------------------------------------------- readers see only the ACTIVE timetable
        [DbFact]
        public async Task A_draft_never_appears_in_any_reader_until_it_is_activated()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000001");
            var active = await ActiveWith(db, w, w.SecA, "Term 1", (1, w.P1, w.Maths, w.T1));
            var draft = await Draft(db, w, w.SecA, "Term 2 draft", clone: active, slots: (2, w.P1, w.Science, w.T2));   // clone + a new lesson with T2

            var grid = (await T(db).GetClassGridAsync(w.School, w.SecA, w.Year, default)).Data!;
            Assert.Equal(1, grid.EntryCount);                                  // the section view: the active one only
            Assert.Equal("Term 1", grid.TimetableName);
            Assert.Equal(2, (await T(db).GetTimetableGridAsync(w.School, draft, default)).Data!.EntryCount);   // the editor of the draft sees the draft
            Assert.Equal(1, (await T(db).GetTeacherGridAsync(w.School, TaxDb.UserA, true, w.T1, w.Year, default)).Data!.EntryCount);
            Assert.Equal(0, (await T(db).GetTeacherGridAsync(w.School, TaxDb.UserA, true, w.T2, w.Year, default)).Data!.EntryCount);   // T2 only teaches in the draft
            // "does this teacher teach this section and subject" (homework, class tests, report marks) says no for the draft's lesson
            Assert.True(await T(db).IsTeachingSectionSubjectAsync(w.School, w.T1, w.SecA, w.Maths, w.Year, default));
            Assert.False(await T(db).IsTeachingSectionSubjectAsync(w.School, w.T2, w.SecA, w.Science, w.Year, default));
            var active0 = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");
            Assert.Empty(await new ReportMarksRepository(db.Ctx()).TeachingPairsAsync(w.School, w.T2, w.Year, active0, default));
            // and the original lookups / unassigned reads agree
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={active} and tt_is_active"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={draft} and tt_is_active"));

            // activate the draft: now the readers switch, the old lesson of T1 is gone from the live view, and T2's lesson is live
            var act = await V(db).ActivateAsync(w.School, TaxDb.UserA, draft, default);
            Assert.True(act.Status, act.Message);
            Assert.Equal("Term 1", act.Data!.PreviousTimetableName);
            Assert.Equal(2, (await T(db).GetClassGridAsync(w.School, w.SecA, w.Year, default)).Data!.EntryCount);
            Assert.Equal(1, (await T(db).GetTeacherGridAsync(w.School, TaxDb.UserA, true, w.T2, w.Year, default)).Data!.EntryCount);
            Assert.True(await T(db).IsTeachingSectionSubjectAsync(w.School, w.T2, w.SecA, w.Science, w.Year, default));
            Assert.Contains(w.SecA, (await new ReportMarksRepository(db.Ctx()).TeachingPairsAsync(w.School, w.T2, w.Year, active0, default)).Select(p => p.SectionId));
            // the database flipped the mirror of every entry of both timetables
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={active} and tt_is_active"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={draft} and tt_is_active"));
        }

        [DbFact]
        public async Task Activation_archives_the_old_one_keeps_the_history_and_only_a_draft_that_was_never_active_can_be_deleted()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000002");
            var first = await ActiveWith(db, w, w.SecA, "First", (1, w.P1, w.Maths, w.T1));
            var second = await Draft(db, w, w.SecA, "Second", slots: (1, w.P1, w.Maths, w.T1));
            Assert.True((await V(db).ActivateAsync(w.School, TaxDb.UserA, second, default)).Status);

            var list = (await V(db).ListAsync(w.School, w.Year, w.SecA, default)).Data!;
            Assert.Equal(new[] { "ACTIVE", "ARCHIVED" }, list.Select(t => t.State).OrderBy(x => x));
            var old = list.Single(t => t.TimetableId == first);
            Assert.Equal("ARCHIVED", old.State); Assert.True(old.EverActive); Assert.False(old.CanDelete);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetables where section_id={w.SecA} and is_active"));
            Assert.Equal(1, (await T(db).GetTimetableGridAsync(w.School, first, default)).Data!.EntryCount);   // the archived history is still readable

            Assert.Equal(409, (await V(db).DeleteAsync(w.School, first, default)).StatusCode);                 // ever active: kept
            Assert.Equal(409, (await V(db).DeleteAsync(w.School, second, default)).StatusCode);                // active
            Assert.Equal(409, (await V(db).ArchiveAsync(w.School, TaxDb.UserA, second, default)).StatusCode);  // the active one can't be archived by hand
            Assert.Equal(409, (await V(db).AssignAsync(w.School, TaxDb.UserA, first, Slot(2, w.P1, w.Maths, w.T1), default)).StatusCode);   // archived: not editable

            var throwaway = await Draft(db, w, w.SecA, "Throwaway", slots: (3, w.P1, w.Art, w.T2));
            Assert.Equal("ARCHIVED", (await V(db).ArchiveAsync(w.School, TaxDb.UserA, throwaway, default)).Data!.State);
            Assert.Equal("DRAFT", (await V(db).RestoreAsync(w.School, TaxDb.UserA, throwaway, default)).Data!.State);   // never active: restorable
            Assert.Equal(200, (await V(db).DeleteAsync(w.School, throwaway, default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={throwaway}"));
            // an archived one that was active is re-activated, not restored: and rolling back works
            Assert.Equal(409, (await V(db).RestoreAsync(w.School, TaxDb.UserA, first, default)).StatusCode);
            Assert.True((await V(db).ActivateAsync(w.School, TaxDb.UserA, first, default)).Status);
            Assert.Equal("ARCHIVED", (await V(db).ListAsync(w.School, w.Year, w.SecA, default)).Data!.Single(t => t.TimetableId == second).State);
            Assert.Equal(409, (await V(db).CreateAsync(w.School, TaxDb.UserA, new CreateTimetableRequest { SectionId = w.SecA, TimetableName = "first" }, default)).StatusCode);   // names are unique per section and year, ignoring case
        }

        // ---------------------------------------------------------------- teacher double-booking among ACTIVE timetables
        [DbFact]
        public async Task A_draft_may_double_book_but_activation_and_edits_of_an_active_timetable_name_the_teacher_day_period_and_other_section()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000003");
            await ActiveWith(db, w, w.SecA, "A live", (1, w.P1, w.Maths, w.T1));
            var drafB = await Draft(db, w, w.SecB, "B draft", slots: (1, w.P1, w.Science, w.T1));                  // drafts never block: T1 at Monday period 1 again

            var check = (await V(db).ValidateAsync(w.School, drafB, default)).Data!;
            Assert.False(check.CanActivate);
            var clash = check.Errors.Single(e => e.Code == "TEACHER_CLASH");
            Assert.Contains("Teacher1", clash.Message); Assert.Contains("Monday", clash.Message); Assert.Contains("period 1", clash.Message); Assert.Contains("Class 1 - A", clash.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetables where section_id={w.SecB} and is_active"));   // asking saved nothing

            var refused = await V(db).ActivateAsync(w.School, TaxDb.UserA, drafB, default);
            Assert.Equal(409, refused.StatusCode);
            Assert.Contains("Teacher1", refused.Message);
            Assert.Equal("DRAFT", db.Scalar<string>($"select state from tb_timetables where timetable_id={drafB}"));

            // fix the draft (another teacher), activate; then editing the ACTIVE one re-checks the touched slot
            Assert.True((await V(db).AssignAsync(w.School, TaxDb.UserA, drafB, Slot(1, w.P1, w.Science, w.T2), default)).Status);
            Assert.True((await V(db).ActivateAsync(w.School, TaxDb.UserA, drafB, default)).Status);
            var edit = await V(db).AssignAsync(w.School, TaxDb.UserA, drafB, Slot(1, w.P1, w.Science, w.T1), default);
            Assert.Equal(409, edit.StatusCode);
            Assert.Contains("Teacher1", edit.Message); Assert.Contains("Class 1 - A", edit.Message);
            Assert.Equal(w.T2, db.Scalar<string>($"select employee_id from tb_timetable_entries where timetable_id={drafB} and day_of_week=1 and status_id={Active}"));   // nothing changed

            // THE DATABASE alone refuses it too: two active timetables cannot book one teacher twice (the unique index on the mirrored flag)
            var sectionAActive = db.Scalar<long>($"select timetable_id from tb_timetables where section_id={w.SecA} and is_active");
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec($@"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id, timetable_id, tt_is_active)
                values ('{w.School}', {w.Year}, {w.Class1}, {w.SecB}, 1, {w.P1}, {w.Art}, '{w.T1}', {Active}, {drafB}, true)"));
            Assert.True(sectionAActive > 0);
        }

        [DbFact]
        public async Task Validate_lists_every_problem_without_saving_and_activation_with_only_warnings_is_allowed()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000004");
            var id = await Draft(db, w, w.SecA, "Check me", slots: new (int, long, long, string?)[] { (1, w.P1, w.Maths, w.T1), (2, w.P1, w.Science, null), (3, w.P2, w.Art, w.T2) });
            // after the lessons were placed: Art stops being taught in section A, T2 leaves, Saturday follows another schedule (an entry on it can't exist yet)
            db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{w.School}', {w.Year}, {w.Class1}, {w.SecA}, {w.Art}, 'REMOVE', {Active})");
            db.Exec($"update tb_employees set status_id={Inactive} where employee_id='{w.T2}'");
            var r = (await V(db).ValidateAsync(w.School, id, default)).Data!;
            Assert.False(r.CanActivate);
            Assert.Contains(r.Errors, e => e.Code == "SUBJECT_NOT_TAUGHT" && e.DayOfWeek == 3);
            Assert.Contains(r.Errors, e => e.Code == "TEACHER_NOT_ELIGIBLE" && e.DayOfWeek == 3);
            Assert.Contains(r.Warnings, e => e.Code == "NEEDS_TEACHER" && e.DayOfWeek == 2);
            Assert.Equal(1, r.SlotsWithoutTeacher);
            Assert.Equal(3, r.SlotCount);
            Assert.Equal(409, (await V(db).ActivateAsync(w.School, TaxDb.UserA, id, default)).StatusCode);
            // fix both errors: only the warning is left, and activation is allowed with the count
            Assert.True((await V(db).UnassignAsync(w.School, TaxDb.UserA, id, new UnassignSlotRequest { DayOfWeek = 3, PeriodId = w.P2 }, default)).Status);
            var ok = await V(db).ActivateAsync(w.School, TaxDb.UserA, id, default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal(1, ok.Data!.SlotsWithoutTeacher);
            Assert.Contains("1 slot", ok.Message);
        }

        // ---------------------------------------------------------------- apply to other sections
        [DbFact]
        public async Task Apply_to_other_sections_previews_first_copies_the_teacher_only_where_free_and_never_overwrites()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000005");
            var src = await ActiveWith(db, w, w.SecA, "Term 1", (1, w.P1, w.Maths, w.T1), (2, w.P1, w.Science, w.T2), (3, w.P1, w.Art, w.T3));
            db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{w.School}', {w.Year}, {w.Class1}, {w.SecC}, {w.Art}, 'REMOVE', {Active})");
            await Draft(db, w, w.SecC, "Existing");   // C already has a timetable (a different name: not in the way)
            var req = new ApplyToSectionsRequest { SectionIds = new() { w.SecB, w.SecC, w.SecD, w.SecA }, TimetableName = "Term 1" };

            var before = db.Scalar<int>($"select count(*) from tb_timetables where school_id='{w.School}'");
            var preview = (await V(db).PreviewApplyAsync(w.School, src, req, default)).Data!;
            Assert.True(preview.Preview);
            Assert.Equal(before, db.Scalar<int>($"select count(*) from tb_timetables where school_id='{w.School}'"));   // a preview saves nothing
            var b = preview.Sections.Single(s => s.SectionId == w.SecB);
            Assert.Equal(3, b.SlotsCopied);
            Assert.Equal(3, b.SlotsNeedingTeacher);                                  // all three teachers are busy in Class 1 - A's ACTIVE timetable at the same slots
            Assert.All(b.NeedsTeacher, n => Assert.Contains("busy with Class 1 - A", n.Reason));
            var c = preview.Sections.Single(s => s.SectionId == w.SecC);
            Assert.Equal(1, c.SlotsSkipped);                                         // Art is not taught in section C
            Assert.Contains("not taught", c.Skipped[0].Reason);
            Assert.NotNull(preview.Sections.Single(s => s.SectionId == w.SecD).SkipReason);   // another class
            Assert.NotNull(preview.Sections.Single(s => s.SectionId == w.SecA).SkipReason);   // the source itself

            var done = (await V(db).ApplyAsync(w.School, TaxDb.UserA, src, req, default)).Data!;
            var newB = done.Sections.Single(s => s.SectionId == w.SecB);
            Assert.True(newB.Created); Assert.NotNull(newB.TimetableId);
            var row = db.Scalar<string>($"select state || '|' || is_active::text || '|' || ever_active::text from tb_timetables where timetable_id={newB.TimetableId}");
            Assert.Equal("DRAFT|false|false", row);                                   // an independent DRAFT
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={newB.TimetableId} and needs_teacher and employee_id is null and not tt_is_active"));
            // the source is untouched
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={src} and status_id={Active} and employee_id is not null"));
            // never overwrite: the same request again skips what now exists
            var again = (await V(db).ApplyAsync(w.School, TaxDb.UserA, src, req, default)).Data!;
            Assert.Contains("already has a timetable", again.Sections.Single(s => s.SectionId == w.SecB).SkipReason);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetables where section_id={w.SecB}"));
            // activating the copy: allowed, with the warning count; then the teacher fills in
            var act = await V(db).ActivateAsync(w.School, TaxDb.UserA, newB.TimetableId!.Value, default);
            Assert.True(act.Status, act.Message);
            Assert.Equal(3, act.Data!.SlotsWithoutTeacher);
        }

        [DbFact]
        public async Task The_same_free_teacher_is_not_handed_to_two_new_timetables_in_one_request()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000006");
            var src = await Draft(db, w, w.SecA, "Plan", slots: (1, w.P1, w.Maths, w.T1));   // a DRAFT source: T1 is not busy anywhere
            var done = (await V(db).ApplyAsync(w.School, TaxDb.UserA, src, new ApplyToSectionsRequest { SectionIds = new() { w.SecB, w.SecC } }, default)).Data!;
            Assert.Equal(1, done.Sections.Count(s => s.SlotsNeedingTeacher == 0));       // the first section gets T1
            Assert.Equal(1, done.Sections.Count(s => s.SlotsNeedingTeacher == 1));       // the second is left without one
        }

        // ---------------------------------------------------------------- concurrency
        [DbFact]
        public async Task Six_simultaneous_activations_of_one_sections_timetables_leave_exactly_one_active()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000007");
            var ids = new List<long>();
            for (var i = 1; i <= 6; i++) ids.Add(await Draft(db, w, w.SecA, "Version " + i, slots: (i <= 6 ? i : 1, i <= 3 ? w.P1 : w.P2, w.Maths, (string?)null)));
            var results = await Task.WhenAll(ids.Select(id => Task.Run(() => V(db).ActivateAsync(w.School, TaxDb.UserA, id, default))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetables where section_id={w.SecA} and is_active"));
            Assert.Equal(5, db.Scalar<int>($"select count(*) from tb_timetables where section_id={w.SecA} and state='ARCHIVED' and ever_active"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_timetable_entries e join tb_timetables t on t.timetable_id=e.timetable_id where e.tt_is_active is distinct from t.is_active"));
        }

        [DbFact]
        public async Task Two_sections_activated_at_once_with_the_same_teacher_slot_let_exactly_one_through()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000008");
            var b = await Draft(db, w, w.SecB, "B", slots: (1, w.P1, w.Maths, w.T1));
            var c = await Draft(db, w, w.SecC, "C", slots: (1, w.P1, w.Maths, w.T1));
            var d = await Draft(db, w, w.SecD, "D", slots: (1, w.P1, w.Maths, w.T1));
            var results = await Task.WhenAll(new[] { b, c, d }.Select(id => Task.Run(() => V(db).ActivateAsync(w.School, TaxDb.UserA, id, default))));
            Assert.Equal(1, results.Count(r => r.Status));
            Assert.Equal(2, results.Count(r => r.StatusCode == 409));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetable_entries where employee_id='{w.T1}' and day_of_week=1 and period_id={w.P1} and tt_is_active and status_id={Active}"));
        }

        [DbFact]
        public async Task Six_simultaneous_bookings_of_one_teacher_into_active_timetables_let_exactly_one_through()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000009");
            var sections = new[] { w.SecA, w.SecB, w.SecC, w.SecD };
            var ids = new List<long>();
            foreach (var s in sections) ids.Add(await ActiveWith(db, w, s, "Live", (1, w.P1, w.Maths, (string?)null)));   // four ACTIVE timetables, none booking T3 yet
            var results = await Task.WhenAll(ids.Select(id => Task.Run(() => V(db).AssignAsync(w.School, TaxDb.UserA, id, Slot(2, w.P3, w.Maths, w.T3), default))));
            Assert.Equal(1, results.Count(r => r.Status));                                  // the first one in books T3 ...
            Assert.Equal(3, results.Count(r => r.StatusCode == 409));                       // ... the others are told who has them
            Assert.All(results.Where(r => !r.Status), r => Assert.Contains("Teacher3", r.Message));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetable_entries where employee_id='{w.T3}' and day_of_week=2 and period_id={w.P3} and tt_is_active and status_id={Active}"));
        }

        // ---------------------------------------------------------------- closed year and tenant isolation
        [DbFact]
        public async Task A_closed_year_refuses_every_timetable_write_but_reads_still_work()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000010");
            var live = await ActiveWith(db, w, w.SecA, "Live", (1, w.P1, w.Maths, w.T1));
            var draft = await Draft(db, w, w.SecA, "Draft", slots: (2, w.P1, w.Maths, w.T1));
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.Year}");
            db.Exec($"update tb_academic_years set closed_at=now() where academic_year_id={w.Year}");
            var codes = new[]
            {
                (await V(db).CreateAsync(w.School, TaxDb.UserA, new CreateTimetableRequest { AcademicYearId = w.Year, SectionId = w.SecA, TimetableName = "New" }, default)).StatusCode,
                (await V(db).RenameAsync(w.School, TaxDb.UserA, draft, new RenameTimetableRequest { TimetableName = "Renamed" }, default)).StatusCode,
                (await V(db).AssignAsync(w.School, TaxDb.UserA, draft, Slot(3, w.P1, w.Maths, w.T1), default)).StatusCode,
                (await V(db).UnassignAsync(w.School, TaxDb.UserA, draft, new UnassignSlotRequest { DayOfWeek = 2, PeriodId = w.P1 }, default)).StatusCode,
                (await V(db).ActivateAsync(w.School, TaxDb.UserA, draft, default)).StatusCode,
                (await V(db).ArchiveAsync(w.School, TaxDb.UserA, draft, default)).StatusCode,
                (await V(db).DeleteAsync(w.School, draft, default)).StatusCode,
                (await V(db).ApplyAsync(w.School, TaxDb.UserA, live, new ApplyToSectionsRequest { SectionIds = new() { w.SecB } }, default)).StatusCode,
                (await T(db).AssignAsync(w.School, TaxDb.UserA, new AssignTimetableRequest { AcademicYearId = w.Year, SectionId = w.SecA, DayOfWeek = 4, PeriodId = w.P1, SubjectId = w.Maths, EmployeeId = w.T1 }, default)).StatusCode,
            };
            Assert.All(codes, c => Assert.Equal(409, c));
            Assert.Equal(2, (await V(db).ListAsync(w.School, w.Year, w.SecA, default)).Data!.Count);          // reads
            Assert.True((await V(db).ValidateAsync(w.School, draft, default)).Status);
            Assert.Equal(1, (await T(db).GetClassGridAsync(w.School, w.SecA, w.Year, default)).Data!.EntryCount);
        }

        [DbFact]
        public async Task Another_schools_timetables_sections_and_teachers_are_never_reachable()
        {
            using var db = new TaxDb();
            var a = await BuildAsync(db, "ZZS6000011");
            var b = await BuildAsync(db, "ZZS6000012");
            var theirs = await ActiveWith(db, b, b.SecA, "Theirs", (1, b.P1, b.Maths, b.T1));
            var mine = await Draft(db, a, a.SecA, "Mine", slots: (1, a.P1, a.Maths, a.T1));
            var v = V(db);
            Assert.Equal(404, (await v.ValidateAsync(a.School, theirs, default)).StatusCode);
            Assert.Equal(404, (await v.ActivateAsync(a.School, TaxDb.UserA, theirs, default)).StatusCode);
            Assert.Equal(404, (await v.AssignAsync(a.School, TaxDb.UserA, theirs, Slot(2, a.P1, a.Maths, a.T1), default)).StatusCode);
            Assert.Equal(404, (await v.RenameAsync(a.School, TaxDb.UserA, theirs, new RenameTimetableRequest { TimetableName = "x" }, default)).StatusCode);
            Assert.Equal(404, (await v.DeleteAsync(a.School, theirs, default)).StatusCode);
            Assert.Equal(404, (await v.ArchiveAsync(a.School, TaxDb.UserA, theirs, default)).StatusCode);
            Assert.Equal(404, (await T(db).GetTimetableGridAsync(a.School, theirs, default)).StatusCode);
            Assert.Equal(404, (await v.ListAsync(a.School, a.Year, b.SecA, default)).StatusCode);
            Assert.Equal(404, (await v.CreateAsync(a.School, TaxDb.UserA, new CreateTimetableRequest { AcademicYearId = a.Year, SectionId = b.SecA, TimetableName = "x" }, default)).StatusCode);
            Assert.Equal(400, (await v.CreateAsync(a.School, TaxDb.UserA, new CreateTimetableRequest { AcademicYearId = b.Year, SectionId = a.SecA, TimetableName = "x" }, default)).StatusCode);
            // another school's teacher and period in an assign
            Assert.Equal(400, (await v.AssignAsync(a.School, TaxDb.UserA, mine, Slot(2, a.P1, a.Maths, b.T1), default)).StatusCode);
            Assert.Equal(400, (await v.AssignAsync(a.School, TaxDb.UserA, mine, Slot(2, b.P1, a.Maths, a.T1), default)).StatusCode);
            // a section of another school in an apply is skipped, not touched
            var r = (await v.ApplyAsync(a.School, TaxDb.UserA, mine, new ApplyToSectionsRequest { SectionIds = new() { b.SecB } }, default)).Data!;
            Assert.NotNull(r.Sections[0].SkipReason);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetables where section_id={b.SecB}"));
            // the other school's active timetable is untouched
            Assert.Equal("ACTIVE", db.Scalar<string>($"select state from tb_timetables where timetable_id={theirs}"));
        }

        // ---------------------------------------------------------------- the original endpoints and the database itself
        [DbFact]
        public async Task The_original_assign_and_unassign_work_on_the_active_timetable_creating_one_when_the_section_has_none()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000013");
            var t = T(db);
            var a = await t.AssignAsync(w.School, TaxDb.UserA, new AssignTimetableRequest { AcademicYearId = w.Year, SectionId = w.SecA, DayOfWeek = 1, PeriodId = w.P1, SubjectId = w.Maths, EmployeeId = w.T1 }, default);
            Assert.True(a.Status, a.Message);
            Assert.Equal("Regular timetable", db.Scalar<string>($"select timetable_name from tb_timetables where section_id={w.SecA} and is_active"));
            Assert.Equal(1, (await t.GetClassGridAsync(w.School, w.SecA, w.Year, default)).Data!.EntryCount);
            Assert.Equal(200, (await t.UnassignAsync(w.School, new UnassignTimetableRequest { AcademicYearId = w.Year, SectionId = w.SecA, DayOfWeek = 1, PeriodId = w.P1 }, default)).StatusCode);
            Assert.Equal(0, (await t.GetClassGridAsync(w.School, w.SecA, w.Year, default)).Data!.EntryCount);
            Assert.Equal(404, (await t.UnassignAsync(w.School, new UnassignTimetableRequest { AcademicYearId = w.Year, SectionId = w.SecB, DayOfWeek = 1, PeriodId = w.P1 }, default)).StatusCode);   // nothing there, no timetable invented
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetables where section_id={w.SecB}"));
        }

        [DbFact]
        public async Task The_database_refuses_a_wrong_mirror_two_active_timetables_for_a_section_and_inconsistent_states()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS6000014");
            var live = await ActiveWith(db, w, w.SecA, "Live", (1, w.P1, w.Maths, w.T1));
            var draft = await Draft(db, w, w.SecA, "Draft", slots: (2, w.P1, w.Maths, w.T2));
            string Entry(long tt, bool flag, string? emp = null, bool needs = false) =>
                $@"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id, timetable_id, tt_is_active, needs_teacher)
                   values ('{w.School}', {w.Year}, {w.Class1}, {w.SecA}, 5, {w.P3}, {w.Maths}, {(emp == null ? "null" : $"'{emp}'")}, {Active}, {tt}, {flag.ToString().ToLower()}, {needs.ToString().ToLower()})";
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec(Entry(draft, true)));            // a draft's entry cannot claim to be active
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec(Entry(live, false)));           // nor an active one claim to be inactive
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Entry(draft, false, emp: w.T1, needs: true)));   // "needs a teacher" with a teacher
            db.Exec(Entry(draft, false, emp: null, needs: true));                                          // a slot without a teacher is fine
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec($"update tb_timetables set state='ACTIVE', is_active=true, ever_active=true where timetable_id={draft}"));   // second active one for the section
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec($"update tb_timetables set state='ACTIVE' where timetable_id={draft}"));                                     // state and flag must agree
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec($"update tb_timetables set state='SLEEPING' where timetable_id={draft}"));
            // the cascade: the database flips the entries when the flag flips
            db.Exec($"update tb_timetables set state='ARCHIVED', is_active=false where timetable_id={live}");
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={live} and tt_is_active"));
            db.Exec($"update tb_timetables set state='ACTIVE', is_active=true, ever_active=true where timetable_id={live}");
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_timetable_entries where timetable_id={live} and tt_is_active"));
            // a timetable for a section the year does not offer
            db.Exec($"delete from tb_academic_year_sections where academic_year_id={w.Year} and section_id={w.SecC}");
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec($"insert into tb_timetables (school_id, academic_year_id, class_id, section_id, timetable_name, schedule_id) values ('{w.School}', {w.Year}, {w.Class1}, {w.SecC}, 'x', {w.Regular})"));
            // an entry whose own section differs from its timetable's
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec($@"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id, timetable_id, tt_is_active)
                values ('{w.School}', {w.Year}, {w.Class1}, {w.SecB}, 6, {w.P3}, {w.Maths}, '{w.T3}', {Active}, {draft}, false)"));
            await Task.CompletedTask;
        }
    }
}
