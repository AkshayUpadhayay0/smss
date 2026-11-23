using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S3: subjects per class per YEAR plus per-section exceptions, and the ONE rule for "what a section is taught"
    /// (vw_section_effective_subjects = class mapping - REMOVE + ADD). Proves the backfill changed nobody's answers, that every reader
    /// (timetable, homework, class tests, report marks, report schemes, class-subject list) follows the year and the override, and the save rules.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the S1, S2 and S3 RUN-ME scripts applied.
    /// </summary>
    public class AcademicSubjectsS3Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private static readonly string[] Backfilled = { "APS261013001", "GHS261103002", "LAS261103001" };

        private sealed class World
        {
            public string School = "";
            public long Y1, Y2, Cls, SecA, SecB, Maths, Sci, Art;
        }

        private static int ActiveId(TaxDb db) => db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");

        /// <summary>One class (sections A and B) offered in two years, three subjects. Year 1 (current): Maths + Science, B does not take Science, A also takes Art. Year 2: Maths + Art.</summary>
        private static async Task<World> BuildAsync(TaxDb db, string id)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Subjects test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', true, {Active}), ('{id}', '2027-28', '2027-04-01', '2028-03-31', false, {Active})");
            var w = new World { School = id };
            w.Y1 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2026-27'");
            w.Y2 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2027-28'");
            var classes = new ClassService(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
            var sections = new SectionService(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
            w.Cls = ((ClassResponse)(await classes.CreateAsync(id, new CreateClassRequest { ClassName = "Class 5", SequenceOrder = 5 })).Data!).ClassId;
            w.SecA = ((SectionResponse)(await sections.CreateAsync(id, new CreateSectionRequest { ClassId = w.Cls, SectionName = "A" })).Data!).SectionId;
            w.SecB = ((SectionResponse)(await sections.CreateAsync(id, new CreateSectionRequest { ClassId = w.Cls, SectionName = "B" })).Data!).SectionId;
            // the current year got the class and sections by itself (S2); year 2 is offered by hand
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) values ('{id}', {w.Y2}, {w.Cls}, {Active})");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) values ('{id}', {w.Y2}, {w.Cls}, {w.SecA}, {Active}), ('{id}', {w.Y2}, {w.Cls}, {w.SecB}, {Active})");
            foreach (var n in new[] { "Maths", "Science", "Art" })
                db.Exec($"insert into tb_subjects (school_id, subject_name, subject_code, status_id) values ('{id}', '{n}', '{n[..3].ToUpper()}', {Active})");
            long S(string n) => db.Scalar<long>($"select subject_id from tb_subjects where school_id='{id}' and subject_name='{n}'");
            (w.Maths, w.Sci, w.Art) = (S("Maths"), S("Science"), S("Art"));
            void Map(long year, long subject) => db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{id}', {year}, {w.Cls}, {subject}, {Active})");
            Map(w.Y1, w.Maths); Map(w.Y1, w.Sci); Map(w.Y2, w.Maths); Map(w.Y2, w.Art);
            void Over(long section, long subject, string action) => db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{id}', {w.Y1}, {w.Cls}, {section}, {subject}, '{action}', {Active})");
            Over(w.SecB, w.Sci, "REMOVE"); Over(w.SecA, w.Art, "ADD");
            return w;
        }

        private static SubjectSetupService Setup(TaxDb db) => new(new ClassSubjectRepository(db.Ctx()), NullLogger<SubjectSetupService>.Instance);
        private static ClassSubjectService Legacy(TaxDb db) => new(new ClassSubjectRepository(db.Ctx()), Setup(db), NullLogger<ClassSubjectService>.Instance);
        private static async Task<HashSet<long>> Taught(TaxDb db, World w, long year, long section) =>
            (await new TimetableRepository(db.Ctx()).GetSubjectsOfSectionAsync(w.School, year, section, ActiveId(db), default)).Select(s => s.SubjectId).ToHashSet();

        // ---------------------------------------------------------------- the backfill changed nobody's answers
        [DbFact]
        public void After_the_backfill_every_year_holds_the_same_mapping_and_every_section_is_taught_exactly_its_class_subjects()
        {
            using var db = new TaxDb();
            foreach (var school in Backfilled)
            {
                var years = db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{school}'");
                var pairs = db.Scalar<int>($"select count(distinct (class_id, subject_id)) from tb_class_subjects where school_id='{school}'");
                Assert.True(pairs > 0, school + " has mappings to compare");
                Assert.Equal(years * pairs, db.Scalar<int>($"select count(*) from tb_class_subjects where school_id='{school}'"));
                Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_section_subject_overrides where school_id='{school}'"));
                // no section deviates from its class: effective rows = sections x class subjects, per year
                var expected = db.Scalar<int>($@"select coalesce(sum(n),0)::int from (select (select count(*) from tb_class_subjects m where m.academic_year_id = o.academic_year_id and m.class_id = o.class_id) as n
                                                  from tb_academic_year_sections o where o.school_id='{school}' and o.status_id={Active}) q");
                Assert.Equal(expected, db.Scalar<int>($"select count(*) from vw_section_effective_subjects where school_id='{school}'"));
            }
        }

        // ---------------------------------------------------------------- the readers: two years with different mappings, and an override
        [DbFact]
        public async Task The_effective_subjects_follow_the_year_and_the_override()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000001");
            Assert.Equal(new[] { w.Maths, w.Sci, w.Art }.OrderBy(x => x), (await Taught(db, w, w.Y1, w.SecA)).OrderBy(x => x));   // class + extra
            Assert.Equal(new[] { w.Maths }, (await Taught(db, w, w.Y1, w.SecB)).ToArray());                                          // class - not taught here
            Assert.Equal(new[] { w.Maths, w.Art }.OrderBy(x => x), (await Taught(db, w, w.Y2, w.SecA)).OrderBy(x => x));            // year 2: its own mapping, no override
            Assert.Equal(new[] { w.Maths, w.Art }.OrderBy(x => x), (await Taught(db, w, w.Y2, w.SecB)).OrderBy(x => x));
            // an ADD of a subject the class already has, and a REMOVE of one it does not have, are harmless no-ops (never a duplicate row)
            db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{w.School}', {w.Y2}, {w.Cls}, {w.SecA}, {w.Maths}, 'ADD', {Active}), ('{w.School}', {w.Y2}, {w.Cls}, {w.SecB}, {w.Sci}, 'REMOVE', {Active})");
            Assert.Equal(2, (await Taught(db, w, w.Y2, w.SecA)).Count);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from vw_section_effective_subjects where academic_year_id={w.Y2} and section_id={w.SecA}"));
        }

        [DbFact]
        public async Task Timetable_homework_and_test_subject_checks_use_the_effective_subjects_of_that_year()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000002");
            var act = ActiveId(db);
            async Task<bool> Tt(long y, long sec, long sub) => (await new TimetableRepository(db.Ctx()).GetSubjectAsync(w.School, sub, y, sec, act, default))!.MappedToClass;
            async Task<bool> Hw(long y, long sec, long sub) => (await new HomeworkRepository(db.Ctx()).GetSubjectAsync(w.School, sub, y, sec, act, default))!.MappedToClass;
            async Task<bool> As(long y, long sec, long sub) => (await new AssessmentRepository(db.Ctx()).GetSubjectAsync(w.School, sub, y, sec, act, default))!.MappedToClass;
            foreach (var read in new Func<long, long, long, Task<bool>>[] { Tt, Hw, As })
            {
                Assert.True(await read(w.Y1, w.SecA, w.Sci));
                Assert.False(await read(w.Y1, w.SecB, w.Sci));   // not taught in B
                Assert.True(await read(w.Y1, w.SecA, w.Art));    // extra in A
                Assert.False(await read(w.Y1, w.SecB, w.Art));
                Assert.False(await read(w.Y2, w.SecA, w.Sci));   // year 2 has no Science
                Assert.True(await read(w.Y2, w.SecB, w.Art));    // year 2 has Art for the whole class
            }
        }

        [DbFact]
        public async Task Report_marks_and_report_schemes_follow_the_year()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000003");
            var act = ActiveId(db);
            var marks = new ReportMarksRepository(db.Ctx());
            Assert.True(await marks.IsMappedAsync(w.School, w.Y1, w.SecA, w.Art, default));
            Assert.False(await marks.IsMappedAsync(w.School, w.Y1, w.SecB, w.Sci, default));
            Assert.False(await marks.IsMappedAsync(w.School, w.Y2, w.SecA, w.Sci, default));
            var pairs = await marks.GetMappedPairsAsync(w.School, w.Y1, default);
            Assert.Contains(new smss_api_db_layer.@interface.MappedPair(w.SecA, w.Art), pairs);
            Assert.DoesNotContain(new smss_api_db_layer.@interface.MappedPair(w.SecB, w.Sci), pairs);
            Assert.Equal(4, pairs.Count);   // A: Maths, Science, Art; B: Maths

            // a report scheme is per class, so it reads the class mapping of ITS year
            var setup = new ReportSetupRepository(db.Ctx());
            var y1 = (await setup.MappedSubjectsAsync(w.School, w.Y1, new[] { w.Cls }, act, default)).Select(m => m.SubjectId).OrderBy(x => x).ToArray();
            var y2 = (await setup.MappedSubjectsAsync(w.School, w.Y2, new[] { w.Cls }, act, default)).Select(m => m.SubjectId).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { w.Maths, w.Sci }.OrderBy(x => x), y1);
            Assert.Equal(new[] { w.Maths, w.Art }.OrderBy(x => x), y2);
        }

        [DbFact]
        public async Task The_class_subject_list_is_per_year_and_defaults_to_the_current_year()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000004");
            var svc = Legacy(db);
            IEnumerable<long> Ids(smss_api_service_layer.dto.ApiResponse<object> r) => ((List<ClassSubjectResponse>)r.Data!).Select(x => x.SubjectId).OrderBy(x => x);
            Assert.Equal(new[] { w.Maths, w.Sci }.OrderBy(x => x), Ids(await svc.GetByClassAsync(w.School, w.Cls, w.Y1)));
            Assert.Equal(new[] { w.Maths, w.Art }.OrderBy(x => x), Ids(await svc.GetByClassAsync(w.School, w.Cls, w.Y2)));
            Assert.Equal(new[] { w.Maths, w.Sci }.OrderBy(x => x), Ids(await svc.GetByClassAsync(w.School, w.Cls, null)));   // Q9: the current year
            Assert.Equal(400, (await svc.GetByClassAsync(w.School, w.Cls, 999999)).StatusCode);                                // another school's / unknown year
        }

        // ---------------------------------------------------------------- the save
        [DbFact]
        public async Task Saving_replaces_the_year_set_and_the_exceptions_and_leaves_the_other_year_alone()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000005");
            var r = await Setup(db).SaveAsync(w.School, TaxDb.UserA, w.Y1, w.Cls, new SaveYearClassSubjectsRequest
            {
                SubjectIds = new() { w.Maths, w.Art },
                Sections = new() { new SectionSubjectOverrideRequest { SectionId = w.SecB, NotTaughtSubjectIds = new() { w.Art } } }
            }, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(new[] { w.Maths, w.Art }.OrderBy(x => x), (await Taught(db, w, w.Y1, w.SecA)).OrderBy(x => x));
            Assert.Equal(new[] { w.Maths }, (await Taught(db, w, w.Y1, w.SecB)).ToArray());
            // the old overrides were switched off (history kept), the new one is on
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_section_subject_overrides where academic_year_id={w.Y1} and status_id <> {Active}"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_section_subject_overrides where academic_year_id={w.Y1} and status_id = {Active}"));
            // year 2 untouched
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_class_subjects where academic_year_id={w.Y2}"));
            // a subject set to none is allowed
            Assert.True((await Setup(db).SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, new SaveYearClassSubjectsRequest { SubjectIds = new() }, default)).Status);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_class_subjects where academic_year_id={w.Y2}"));
        }

        [DbFact]
        public async Task A_closed_year_a_class_that_is_not_offered_and_bad_input_are_refused()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000006");
            var svc = Setup(db);
            SaveYearClassSubjectsRequest Req(params long[] ids) => new() { SubjectIds = ids.ToList() };

            db.Exec($"update tb_academic_years set is_current = false where academic_year_id = {w.Y1}");
            db.Exec($"update tb_academic_years set is_current = true where academic_year_id = {w.Y2}");
            db.Exec($"update tb_academic_years set closed_at = now() where academic_year_id = {w.Y1}");
            var closed = await svc.SaveAsync(w.School, TaxDb.UserA, w.Y1, w.Cls, Req(w.Maths), default);
            Assert.Equal(409, closed.StatusCode);
            Assert.Contains("closed", closed.Message);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_class_subjects where academic_year_id={w.Y1}"));
            // reads still work in a closed year
            Assert.True((await svc.GetAsync(w.School, w.Y1, w.Cls, default)).Data!.IsClosed);
            // the legacy endpoints are closed too
            Assert.Equal(409, (await Legacy(db).BulkSetAsync(w.School, new BulkSetClassSubjectsRequest { ClassId = w.Cls, SubjectIds = new() { w.Maths }, AcademicYearId = w.Y1 })).StatusCode);
            Assert.Equal(409, (await Legacy(db).CreateAsync(w.School, new CreateClassSubjectRequest { ClassId = w.Cls, SubjectId = w.Art, AcademicYearId = w.Y1 })).StatusCode);

            // class withdrawn from year 2 -> nothing can be mapped there
            db.Exec($"update tb_academic_year_classes set status_id = (select sid from lut_status where sname='Inactive' and stype='general status') where academic_year_id={w.Y2} and class_id={w.Cls}");
            Assert.Equal(409, (await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, Req(w.Maths), default)).StatusCode);
            db.Exec($"update tb_academic_year_classes set status_id = {Active} where academic_year_id={w.Y2} and class_id={w.Cls}");

            Assert.Equal(400, (await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, Req(999999), default)).StatusCode);   // unknown subject
            Assert.Equal(404, (await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, 999999, Req(w.Maths), default)).StatusCode);   // unknown class
            Assert.Equal(404, (await svc.SaveAsync(w.School, TaxDb.UserA, 999999, w.Cls, Req(w.Maths), default)).StatusCode);   // unknown year
            Assert.Equal(400, (await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, new() { SubjectIds = new() { w.Maths }, Sections = new() { new() { SectionId = w.SecA, ExtraSubjectIds = new() { w.Maths } } } }, default)).StatusCode);   // extra but the class has it
            Assert.Equal(400, (await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, new() { SubjectIds = new() { w.Maths }, Sections = new() { new() { SectionId = w.SecA, NotTaughtSubjectIds = new() { w.Art } } } }, default)).StatusCode);   // not taught but the class lacks it
            Assert.Equal(400, (await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, new() { SubjectIds = new() { w.Maths }, Sections = new() { new() { SectionId = 999999, ExtraSubjectIds = new() { w.Art } } } }, default)).StatusCode);   // not a section of this class
            // an Inactive subject cannot be newly added
            db.Exec($"update tb_subjects set status_id = (select sid from lut_status where sname='Inactive' and stype='general status') where subject_id = {w.Sci}");
            var inactive = await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, Req(w.Maths, w.Sci), default);
            Assert.Equal(400, inactive.StatusCode);
            Assert.Contains("Science", inactive.Message);
        }

        [DbFact]
        public async Task Another_schools_class_subject_or_year_is_never_reachable()
        {
            using var db = new TaxDb();
            var a = await BuildAsync(db, "ZZS3000007");
            var b = await BuildAsync(db, "ZZS3000008");
            var svc = Setup(db);
            Assert.Equal(404, (await svc.GetAsync(a.School, b.Y1, b.Cls, default)).StatusCode);
            Assert.Equal(404, (await svc.GetAsync(a.School, a.Y1, b.Cls, default)).StatusCode);
            Assert.Equal(404, (await svc.SaveAsync(a.School, TaxDb.UserA, b.Y1, a.Cls, new() { SubjectIds = new() { a.Maths } }, default)).StatusCode);
            Assert.Equal(400, (await svc.SaveAsync(a.School, TaxDb.UserA, a.Y1, a.Cls, new() { SubjectIds = new() { b.Maths } }, default)).StatusCode);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_class_subjects where academic_year_id={a.Y1}"));
        }

        // ---------------------------------------------------------------- removing something in use
        [DbFact]
        public async Task A_subject_used_by_a_timetable_slot_homework_or_a_test_cannot_be_removed_and_the_409_says_how_much()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000009");
            // one teacher, one period for the school
            var emp = TeacherOf(db, w.School);
            var schedule = db.Scalar<long>($"insert into tb_period_schedules (school_id, schedule_name, is_default, status_id) values ('{w.School}', 'Regular', true, {Active}) returning schedule_id");   // S4: a period is a slot of a schedule
            var period = db.Scalar<long>($"insert into tb_periods (school_id, schedule_id, period_number, slot_order, start_time, end_time, status_id) values ('{w.School}', {schedule}, 1, 1, '09:00', '09:45', {Active}) returning period_id");
            TtKit.Ensure(db, w.School, w.Y1, w.Cls, w.SecA);
            db.Exec($"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id{TtKit.Cols}) values ('{w.School}', {w.Y1}, {w.Cls}, {w.SecA}, 1, {period}, {w.Sci}, '{emp}', {Active}{TtKit.Vals(w.Y1, w.SecA)})");
            db.Exec($"insert into tb_homework (school_id, academic_year_id, class_id, section_id, subject_id, employee_id, title, assigned_date, due_date, status_id) values ('{w.School}', {w.Y1}, {w.Cls}, {w.SecA}, {w.Sci}, '{emp}', 'Ch 1', '2026-08-01', '2026-08-03', {Active}), ('{w.School}', {w.Y1}, {w.Cls}, {w.SecA}, {w.Sci}, '{emp}', 'Ch 2', '2026-08-02', '2026-08-04', {Active})");

            var svc = Setup(db);
            // taking Science away from the whole class: section A has 1 timetable slot and 2 homework
            var blocked = await svc.SaveAsync(w.School, TaxDb.UserA, w.Y1, w.Cls, new() { SubjectIds = new() { w.Maths } }, default);
            Assert.Equal(409, blocked.StatusCode);
            Assert.Contains("Science in Class 5 - A", blocked.Message);
            Assert.Contains("1 timetable slot", blocked.Message);
            Assert.Contains("2 homework", blocked.Message);
            Assert.Contains("Nothing was saved", blocked.Message);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_class_subjects where academic_year_id={w.Y1}"));   // nothing changed
            // the legacy endpoints answer the same
            Assert.Equal(409, (await Legacy(db).BulkSetAsync(w.School, new BulkSetClassSubjectsRequest { ClassId = w.Cls, SubjectIds = new() { w.Maths }, AcademicYearId = w.Y1 })).StatusCode);
            var rowId = db.Scalar<long>($"select class_subject_id from tb_class_subjects where academic_year_id={w.Y1} and subject_id={w.Sci}");
            Assert.Equal(409, (await Legacy(db).DeleteAsync(w.School, rowId)).StatusCode);
            // marking Science "not taught" in section A (where it is used) is refused too
            var over = await svc.SaveAsync(w.School, TaxDb.UserA, w.Y1, w.Cls, new() { SubjectIds = new() { w.Maths, w.Sci }, Sections = new() { new() { SectionId = w.SecA, NotTaughtSubjectIds = new() { w.Sci } } } }, default);
            Assert.Equal(409, over.StatusCode);
            // ... but Science can go from section B-only usage? B does not take it, so dropping the Art extra in A is fine (nothing uses it), and the same subject in year 2 is free
            Assert.True((await svc.SaveAsync(w.School, TaxDb.UserA, w.Y1, w.Cls, new() { SubjectIds = new() { w.Maths, w.Sci }, Sections = new() { new() { SectionId = w.SecB, NotTaughtSubjectIds = new() { w.Sci } } } }, default)).Status);
            Assert.True((await svc.SaveAsync(w.School, TaxDb.UserA, w.Y2, w.Cls, new() { SubjectIds = new() { w.Maths } }, default)).Status);
            // the in-use flags the screen shows
            var dto = (await svc.GetAsync(w.School, w.Y1, w.Cls, default)).Data!;
            Assert.Equal(3, dto.Subjects.Single(s => s.SubjectId == w.Sci).InUse);
            Assert.Equal(0, dto.Subjects.Single(s => s.SubjectId == w.Maths).InUse);
            Assert.Contains(w.Sci, dto.Sections.Single(s => s.SectionId == w.SecA).InUseSubjectIds);
            // once the homework and the slot are gone, the subject can go
            db.Exec($"update tb_homework set status_id = (select sid from lut_status where sname='Inactive' and stype='general status') where academic_year_id={w.Y1}");
            db.Exec($"delete from tb_timetable_entries where academic_year_id={w.Y1}");
            Assert.True((await svc.SaveAsync(w.School, TaxDb.UserA, w.Y1, w.Cls, new() { SubjectIds = new() { w.Maths } }, default)).Status);
        }

        /// <summary>A throwaway Teaching employee for the fresh school.</summary>
        private static string TeacherOf(TaxDb db, string school)
        {
            var id = "ZZT" + school[^5..];
            db.Exec($"insert into tb_employee_designations (school_id, designation_name, status_id) values ('{school}', 'Teacher', {Active})");
            db.Exec($"insert into tb_employee_departments (school_id, department_name, status_id) values ('{school}', 'Dept', {Active})");
            db.Exec($@"insert into tb_employees (employee_id, school_id, designation_id, department_id, employment_status_id, employee_type, first_name, last_name, date_of_birth, mobile_number, address, date_of_joining, status_id)
                       values ('{id}', '{school}', (select designation_id from tb_employee_designations where school_id='{school}'), (select department_id from tb_employee_departments where school_id='{school}'),
                               (select min(employment_status_id) from lut_employment_status), 'Teaching', 'T', 'Teacher', '1990-01-01', '9876543210', 'x', '2020-01-01', {Active})");
            return id;
        }

        // ---------------------------------------------------------------- the database itself
        [DbFact]
        public async Task The_database_refuses_a_mapping_for_a_class_the_year_does_not_offer_and_a_duplicate()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS3000010");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{w.School}', '2028-29', '2028-04-01', '2029-03-31', false, {Active})");
            var y3 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{w.School}' and year_name='2028-29'");
            var notOffered = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{w.School}', {y3}, {w.Cls}, {w.Maths}, {Active})"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, notOffered.SqlState);
            var dup = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{w.School}', {w.Y1}, {w.Cls}, {w.Maths}, {Active})"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, dup.SqlState);
            // an override cannot name an action other than ADD / REMOVE, and not a section that the year does not offer
            var bad = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{w.School}', {w.Y2}, {w.Cls}, {w.SecA}, {w.Art}, 'SWAP', {Active})"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, bad.SqlState);
            var other = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id) values ('{w.School}', {y3}, {w.Cls}, {w.SecA}, {w.Art}, 'ADD', {Active})"));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, other.SqlState);
            await Task.CompletedTask;
        }
    }
}
