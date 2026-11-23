using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S4: bell schedules, breaks, and which schedule a class follows on which day of a year.
    /// Proves the backfill changed nobody's timetable, what the DATABASE refuses (overlap, second default, a lesson on a break), the schedule / slot rules,
    /// the class-and-day resolution, and that the timetable only accepts periods of the schedule the class follows.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the S1 to S4 RUN-ME scripts applied.
    /// </summary>
    public class AcademicSchedulesS4Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";
        private static readonly string[] Backfilled = { "APS261013001", "GHS261103002", "LAS261103001" };

        private static PeriodScheduleService Schedules(TaxDb db) => new(new PeriodRepository(db.Ctx()), NullLogger<PeriodScheduleService>.Instance);
        private static PeriodService Periods(TaxDb db) => new(new PeriodRepository(db.Ctx()), NullLogger<PeriodService>.Instance);
        private static TimetableService Timetable(TaxDb db) => new(new TimetableRepository(db.Ctx()), NullLogger<TimetableService>.Instance);
        private static TimeOnly T(string hhmm) => TimeOnly.Parse(hhmm);
        private static ScheduleSlotRequest Slot(string type, int? number, string start, string end, string? label = null) =>
            new() { SlotType = type, PeriodNumber = number, StartTime = T(start), EndTime = T(end), Label = label };
        private static void Expect(string sqlState, Action a) => Assert.Equal(sqlState, Assert.Throws<PostgresException>(a).SqlState);

        private sealed class World
        {
            public string School = "";
            public long Year, ClassA, ClassB, SecA, SecB, Maths, Regular, Short;
            public string Teacher = "", Teacher2 = "";
        }

        /// <summary>A school with one year (current), two classes with a section each, Maths mapped to both, two teachers, and two schedules: Regular (periods 1-2, a lunch break, period 3) and Short.</summary>
        private static async Task<World> BuildAsync(TaxDb db, string id)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Schedules test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");
            var w = new World { School = id };
            w.Year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}'");
            var classes = new ClassService(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
            var sections = new SectionService(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
            w.ClassA = ((ClassResponse)(await classes.CreateAsync(id, new CreateClassRequest { ClassName = "Class 1", SequenceOrder = 1 })).Data!).ClassId;
            w.ClassB = ((ClassResponse)(await classes.CreateAsync(id, new CreateClassRequest { ClassName = "Class 2", SequenceOrder = 2 })).Data!).ClassId;
            w.SecA = ((SectionResponse)(await sections.CreateAsync(id, new CreateSectionRequest { ClassId = w.ClassA, SectionName = "A" })).Data!).SectionId;
            w.SecB = ((SectionResponse)(await sections.CreateAsync(id, new CreateSectionRequest { ClassId = w.ClassB, SectionName = "A" })).Data!).SectionId;
            db.Exec($"insert into tb_subjects (school_id, subject_name, subject_code, status_id) values ('{id}', 'Maths', 'MAT', {Active})");
            w.Maths = db.Scalar<long>($"select subject_id from tb_subjects where school_id='{id}'");
            db.Exec($"insert into tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id) values ('{id}', {w.Year}, {w.ClassA}, {w.Maths}, {Active}), ('{id}', {w.Year}, {w.ClassB}, {w.Maths}, {Active})");
            db.Exec($"insert into tb_employee_designations (school_id, designation_name, status_id) values ('{id}', 'Teacher', {Active})");
            db.Exec($"insert into tb_employee_departments (school_id, department_name, status_id) values ('{id}', 'Dept', {Active})");
            string Emp(string n)
            {
                var e = "ZT" + id[^5..] + n;
                db.Exec($@"insert into tb_employees (employee_id, school_id, designation_id, department_id, employment_status_id, employee_type, first_name, last_name, date_of_birth, mobile_number, address, date_of_joining, status_id)
                           values ('{e}', '{id}', (select designation_id from tb_employee_designations where school_id='{id}'), (select department_id from tb_employee_departments where school_id='{id}'),
                                   (select min(employment_status_id) from lut_employment_status), 'Teaching', 'T', 'Teacher{n}', '1990-01-01', '9876543210', 'x', '2020-01-01', {Active})");
                return e;
            }
            (w.Teacher, w.Teacher2) = (Emp("1"), Emp("2"));

            var svc = Schedules(db);
            var regular = (await svc.CreateAsync(id, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "Regular" }, default)).Data!;   // the first schedule is the default
            Assert.True(regular.IsDefault);
            w.Regular = regular.ScheduleId;
            Assert.True((await svc.AddSlotAsync(id, w.Regular, Slot("PERIOD", 1, "09:00", "09:45"), default)).Status);
            Assert.True((await svc.AddSlotAsync(id, w.Regular, Slot("PERIOD", 2, "09:45", "10:30"), default)).Status);
            Assert.True((await svc.AddSlotAsync(id, w.Regular, Slot("BREAK", null, "10:30", "11:00", "Lunch"), default)).Status);
            Assert.True((await svc.AddSlotAsync(id, w.Regular, Slot("PERIOD", 3, "11:00", "11:45"), default)).Status);
            var shortS = (await svc.CreateAsync(id, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "Short" }, default)).Data!;
            Assert.False(shortS.IsDefault);
            w.Short = shortS.ScheduleId;
            Assert.True((await svc.AddSlotAsync(id, w.Short, Slot("PERIOD", 1, "08:00", "08:40"), default)).Status);
            Assert.True((await svc.AddSlotAsync(id, w.Short, Slot("PERIOD", 2, "08:40", "09:20"), default)).Status);
            return w;
        }

        private static long SlotId(TaxDb db, long schedule, int? number, string? type = null) =>
            db.Scalar<long>($"select period_id from tb_periods where schedule_id={schedule} and " + (number != null ? $"period_number={number}" : $"slot_type='{type}'"));

        private static AssignTimetableRequest Assign(World w, long section, int day, long period, string? teacher = null) =>
            new() { AcademicYearId = w.Year, SectionId = section, DayOfWeek = day, PeriodId = period, SubjectId = w.Maths, EmployeeId = teacher ?? w.Teacher };

        // ---------------------------------------------------------------- the backfill changed nobody's timetable
        [DbFact]
        public async Task After_the_backfill_every_school_has_one_default_schedule_Regular_and_every_timetable_entry_still_shows()
        {
            using var db = new TaxDb();
            foreach (var school in Backfilled)
            {
                Assert.Equal("Regular", db.Scalar<string>($"select schedule_name from tb_period_schedules where school_id='{school}' and is_default"));
                Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_period_schedules where school_id='{school}'"));
                var periods = db.Scalar<int>($"select count(*) from tb_periods where school_id='{school}'");
                Assert.True(periods > 0);
                Assert.Equal(periods, db.Scalar<int>($"select count(*) from tb_periods p join tb_period_schedules d on d.schedule_id=p.schedule_id and d.is_default where p.school_id='{school}' and p.slot_order=p.period_number and p.slot_type='PERIOD'"));
                Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_schedules where school_id='{school}'"));
                // the classic Periods endpoint still lists the same periods
                var listed = (await Periods(db).ListAsync(school, true, default)).Data!;
                Assert.Equal(periods, listed.Count);
                // a section's grid carries every one of its entries, in the same cells as the SQL says
                var year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{school}' and is_current");
                var perSection = db.Scalar<int>($"select coalesce(max(n),0)::int from (select count(*) n from tb_timetable_entries where school_id='{school}' and academic_year_id={year} and status_id={Active} group by section_id) q");
                var sectionId = db.Scalar<long>($"select section_id from tb_timetable_entries where school_id='{school}' and academic_year_id={year} and status_id={Active} group by section_id order by count(*) desc limit 1");
                var grid = (await Timetable(db).GetClassGridAsync(school, sectionId, year, default)).Data!;
                Assert.Equal(perSection, grid.EntryCount);
                Assert.Equal(perSection, grid.Rows.SelectMany(r => r.Cells).Count(c => c.Entry != null));
                Assert.All(grid.Rows, r => { Assert.Equal("PERIOD", r.Period.SlotType); Assert.Null(r.Period.ScheduleName); Assert.All(r.Cells, c => Assert.True(c.Applies)); });
            }
        }

        // ---------------------------------------------------------------- what the DATABASE refuses
        [DbFact]
        public async Task The_database_refuses_overlap_a_second_default_a_duplicate_number_and_a_lesson_on_a_break()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS4000001");
            string Ins(long schedule, string type, string number, string start, string end, string label = "null", string status = Active) =>
                $"insert into tb_periods (school_id, schedule_id, slot_type, period_number, slot_label, slot_order, start_time, end_time, status_id) values ('{w.School}', {schedule}, '{type}', {number}, {label}, 50, '{start}', '{end}', {status})";

            Expect(PostgresErrorCodes.ExclusionViolation, () => db.Exec(Ins(w.Regular, "PERIOD", "9", "09:30", "10:00")));          // overlaps period 1 and 2 of the same schedule
            Expect(PostgresErrorCodes.ExclusionViolation, () => db.Exec(Ins(w.Regular, "BREAK", "null", "10:45", "11:15", "'Tea'"))); // a break also occupies time
            db.Exec(Ins(w.Short, "PERIOD", "9", "09:30", "10:00"));                                                                    // the same clock time in ANOTHER schedule is fine
            db.Exec(Ins(w.Regular, "PERIOD", "8", "09:30", "10:00", status: Inactive));                                                // an inactive slot may overlap
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec(Ins(w.Regular, "PERIOD", "1", "14:00", "14:30")));                // period number unique per schedule
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Ins(w.Regular, "PERIOD", "null", "14:00", "14:30")));             // a PERIOD has a number
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Ins(w.Regular, "BREAK", "7", "14:00", "14:30", "'x'")));           // a BREAK has none
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Ins(w.Regular, "PERIOD", "7", "14:30", "14:00")));                 // end after start
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec($"insert into tb_period_schedules (school_id, schedule_name, is_default, status_id) values ('{w.School}', 'Another', true, {Active})"));   // one default per school
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec($"insert into tb_period_schedules (school_id, schedule_name, status_id) values ('{w.School}', 'Regular', {Active})"));   // names unique per school

            // a lesson on a break: refused; a lesson on a period: accepted; a period with a lesson cannot become a break
            var lunch = SlotId(db, w.Regular, null, "BREAK");
            string Entry(long period, int day) => $"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id) values ('{w.School}', {w.Year}, {w.ClassA}, {w.SecA}, {day}, {period}, {w.Maths}, '{w.Teacher}', {Active})";
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec(Entry(lunch, 1)));
            var p1 = SlotId(db, w.Regular, 1);
            db.Exec(Entry(p1, 1));
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec($"update tb_periods set slot_type='BREAK', period_number=null, slot_label='x' where period_id={p1}"));

            // assignments: one per (year, class, day); day 1..6; the class must be offered that year
            db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, {w.ClassA}, 6, {w.Short})");
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, {w.ClassA}, 6, {w.Regular})"));
            db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, null, 6, {w.Short})");
            Expect(PostgresErrorCodes.UniqueViolation, () => db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, null, 6, {w.Regular})"));   // NULL class is one slot too
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, {w.ClassB}, 7, {w.Short})"));
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, 999999, 1, {w.Short})"));
        }

        // ---------------------------------------------------------------- the service: schedules and slots
        [DbFact]
        public async Task Slots_are_validated_kept_in_clock_order_and_a_slot_in_use_is_protected()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS4000002");
            var svc = Schedules(db);

            // order follows the clock, whatever order they were added in (lunch was added third and sits between 2 and 3)
            var regular = (await svc.ListAsync(w.School, default)).Data!.Single(s => s.ScheduleId == w.Regular);
            Assert.Equal(new[] { "09:00", "09:45", "10:30", "11:00" }, regular.Slots.Select(s => s.StartTime.ToString("HH:mm")));
            Assert.Equal(new[] { 1, 2, 3, 4 }, regular.Slots.Select(s => s.SlotOrder));
            Assert.Equal("BREAK", regular.Slots[2].SlotType);
            // a new, EARLIER slot lands first
            var early = (await svc.AddSlotAsync(w.School, w.Regular, Slot("PERIOD", 9, "08:00", "08:45"), default)).Data!;
            Assert.Equal(9, early.Slots[0].PeriodNumber);

            // validation
            Assert.Equal(400, (await svc.AddSlotAsync(w.School, w.Regular, Slot("PERIOD", 5, "15:00", "14:00"), default)).StatusCode);       // end before start
            Assert.Equal(400, (await svc.AddSlotAsync(w.School, w.Regular, Slot("BREAK", null, "15:00", "15:30"), default)).StatusCode);     // a break needs a label
            Assert.Equal(400, (await svc.AddSlotAsync(w.School, w.Regular, Slot("PERIOD", null, "15:00", "15:30"), default)).StatusCode);    // a period needs a number
            Assert.Equal(400, (await svc.AddSlotAsync(w.School, w.Regular, Slot("RECESS", 5, "15:00", "15:30"), default)).StatusCode);       // unknown type
            var overlap = await svc.AddSlotAsync(w.School, w.Regular, Slot("PERIOD", 5, "10:00", "10:40"), default);
            Assert.Equal(409, overlap.StatusCode);
            Assert.Contains("overlap", overlap.Message);
            Assert.Equal(409, (await svc.AddSlotAsync(w.School, w.Regular, Slot("PERIOD", 1, "15:00", "15:30"), default)).StatusCode);       // number taken
            Assert.Equal(404, (await svc.AddSlotAsync(w.School, 999999, Slot("PERIOD", 5, "15:00", "15:30"), default)).StatusCode);
            Assert.Equal(404, (await svc.AddSlotAsync("ZZS4OTHER", w.Regular, Slot("PERIOD", 5, "15:00", "15:30"), default)).StatusCode);   // another school's schedule

            // a slot in use
            var p1 = SlotId(db, w.Regular, 1);
            db.Exec($"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id) values ('{w.School}', {w.Year}, {w.ClassA}, {w.SecA}, 1, {p1}, {w.Maths}, '{w.Teacher}', {Active})");
            Assert.Equal(409, (await svc.ToggleSlotAsync(w.School, w.Regular, p1, default)).StatusCode);
            Assert.Equal(409, (await svc.DeleteSlotAsync(w.School, w.Regular, p1, default)).StatusCode);
            var toBreak = await svc.UpdateSlotAsync(w.School, w.Regular, p1, Slot("BREAK", null, "09:00", "09:45", "Assembly"), default);
            Assert.Equal(409, toBreak.StatusCode);
            Assert.Contains("can't become a break", toBreak.Message);
            // times of a used slot may move
            Assert.True((await svc.UpdateSlotAsync(w.School, w.Regular, p1, Slot("PERIOD", 1, "09:00", "09:40"), default)).Status);
            // an unused slot can be deactivated, reactivated, deleted
            var lunch = SlotId(db, w.Regular, null, "BREAK");
            Assert.False((await svc.ToggleSlotAsync(w.School, w.Regular, lunch, default)).Data!.Slots.Single(s => s.PeriodId == lunch).IsActive);
            Assert.True((await svc.ToggleSlotAsync(w.School, w.Regular, lunch, default)).Data!.Slots.Single(s => s.PeriodId == lunch).IsActive);
            Assert.DoesNotContain(lunch, (await svc.DeleteSlotAsync(w.School, w.Regular, lunch, default)).Data!.Slots.Select(s => s.PeriodId));
        }

        [DbFact]
        public async Task Schedules_can_be_created_copied_renamed_and_made_default_and_one_in_use_cannot_be_deleted()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS4000003");
            var svc = Schedules(db);

            var copy = (await svc.CreateAsync(w.School, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "Winter", CopyFromScheduleId = w.Regular }, default)).Data!;
            Assert.Equal(4, copy.Slots.Count);
            Assert.Contains(copy.Slots, s => s.SlotType == "BREAK" && s.Label == "Lunch");
            Assert.Equal(409, (await svc.CreateAsync(w.School, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "winter" }, default)).StatusCode);   // names are unique ignoring case
            Assert.Equal(404, (await svc.CreateAsync(w.School, TaxDb.UserA, new CreateScheduleRequest { ScheduleName = "X", CopyFromScheduleId = 999999 }, default)).StatusCode);
            Assert.Equal("Winter 2026", (await svc.RenameAsync(w.School, TaxDb.UserA, copy.ScheduleId, new RenameScheduleRequest { ScheduleName = " Winter 2026 " }, default)).Data!.ScheduleName);
            Assert.Equal(409, (await svc.RenameAsync(w.School, TaxDb.UserA, copy.ScheduleId, new RenameScheduleRequest { ScheduleName = "Short" }, default)).StatusCode);

            // the default cannot be deleted; an assigned schedule cannot; one with entries cannot; an unused one can
            Assert.Equal(409, (await svc.DeleteAsync(w.School, w.Regular, default)).StatusCode);
            db.Exec($"insert into tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id) values ('{w.School}', {w.Year}, null, 6, {w.Short})");
            var assigned = await svc.DeleteAsync(w.School, w.Short, default);
            Assert.Equal(409, assigned.StatusCode);
            Assert.Contains("assigned", assigned.Message);
            db.Exec($"delete from tb_academic_year_schedules where school_id='{w.School}'");
            db.Exec($"insert into tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id, subject_id, employee_id, status_id) values ('{w.School}', {w.Year}, {w.ClassA}, {w.SecA}, 1, {SlotId(db, w.Short, 1)}, {w.Maths}, '{w.Teacher}', {Active})");
            Assert.Equal(409, (await svc.DeleteAsync(w.School, w.Short, default)).StatusCode);
            Assert.Equal(200, (await svc.DeleteAsync(w.School, copy.ScheduleId, default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_periods where schedule_id={copy.ScheduleId}"));

            // another default: the old one stops being default, and the new one is the only one
            db.Exec($"delete from tb_timetable_entries where school_id='{w.School}'");
            var made = (await svc.SetDefaultAsync(w.School, TaxDb.UserA, w.Short, default)).Data!;
            Assert.True(made.IsDefault);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_period_schedules where school_id='{w.School}' and is_default"));
            // and the classic Periods list now follows the new default
            Assert.Equal(2, (await Periods(db).ListAsync(w.School, true, default)).Data!.Count);
        }

        [DbFact]
        public async Task A_fresh_school_gets_its_Regular_schedule_by_adding_a_period_the_classic_way()
        {
            using var db = new TaxDb();
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('ZZS4000004', 'ZZS4000004-C', 'Fresh', {Active})");
            var r = await Periods(db).CreateAsync("ZZS4000004", new PeriodRequest { PeriodNumber = 1, StartTime = T("09:00"), EndTime = T("09:45") }, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal("Regular", db.Scalar<string>("select schedule_name from tb_period_schedules where school_id='ZZS4000004' and is_default"));
            Assert.Equal(409, (await Periods(db).CreateAsync("ZZS4000004", new PeriodRequest { PeriodNumber = 2, StartTime = T("09:30"), EndTime = T("10:00") }, default)).StatusCode);   // overlap, as before
            Assert.True((await Periods(db).CreateAsync("ZZS4000004", new PeriodRequest { PeriodNumber = 2, StartTime = T("09:45"), EndTime = T("10:30") }, default)).Status);
            Assert.Equal(new[] { 1, 2 }, (await Periods(db).ListAsync("ZZS4000004", true, default)).Data!.Select(p => p.PeriodNumber));
        }

        // ---------------------------------------------------------------- resolution and assignments
        [Fact]
        public void A_class_follows_the_most_specific_assignment_and_otherwise_the_default()
        {
            var rows = new List<(long? ClassId, short? Day, long ScheduleId)> { (null, 6, 30), (7, null, 20), (7, 6, 10), (null, null, 40) };
            Assert.Equal(10, ScheduleResolver.Resolve(rows, 7, 6, 99));   // this class, this day
            Assert.Equal(20, ScheduleResolver.Resolve(rows, 7, 2, 99));   // this class, any day
            Assert.Equal(30, ScheduleResolver.Resolve(rows, 8, 6, 99));   // any class, this day
            Assert.Equal(40, ScheduleResolver.Resolve(rows, 8, 2, 99));   // any class, any day
            Assert.Equal(99, ScheduleResolver.Resolve(new List<(long?, short?, long)>(), 8, 2, 99));   // nothing assigned: the default
            Assert.Null(ScheduleResolver.Resolve(new List<(long?, short?, long)>(), 8, 2, null));       // no schedule at all
        }

        [DbFact]
        public async Task Year_assignments_are_replaced_as_a_set_and_closed_years_unknown_classes_and_duplicates_are_refused()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS4000005");
            var svc = Schedules(db);
            ScheduleAssignmentRequest Row(long? cls, int? day, long schedule) => new() { ClassId = cls, DayOfWeek = day, ScheduleId = schedule };

            var saved = await svc.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { Row(null, 6, w.Short), Row(w.ClassB, null, w.Short) } }, default);
            Assert.True(saved.Status, saved.Message);
            Assert.Equal(2, saved.Data!.Rows.Count);
            Assert.Equal(w.Regular, saved.Data.DefaultScheduleId);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_academic_year_schedules where academic_year_id={w.Year}"));
            // replaced, not appended
            Assert.Single((await svc.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { Row(null, 6, w.Short) } }, default)).Data!.Rows);
            Assert.Empty((await svc.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() }, default)).Data!.Rows);

            Assert.Equal(400, (await svc.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { Row(null, 6, 999999) } }, default)).StatusCode);          // unknown schedule
            Assert.Equal(400, (await svc.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { Row(999999, 1, w.Short) } }, default)).StatusCode);       // class not offered
            Assert.Equal(400, (await svc.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { Row(null, 6, w.Short), Row(null, 6, w.Regular) } }, default)).StatusCode);   // twice
            Assert.Equal(404, (await svc.SaveYearAsync("ZZS4OTHER", TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() }, default)).StatusCode);                                  // another school's year

            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{w.School}', '2025-26', '2025-04-01', '2026-03-31', false, {Active})");
            var past = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{w.School}' and year_name='2025-26'");
            db.Exec($"update tb_academic_years set closed_at = now() where academic_year_id = {past}");
            var closed = await svc.SaveYearAsync(w.School, TaxDb.UserA, past, new SaveYearSchedulesRequest { Rows = new() }, default);
            Assert.Equal(409, closed.StatusCode);
            Assert.Contains("closed", closed.Message);
            Assert.True((await svc.GetYearAsync(w.School, past, default)).Data!.IsClosed);   // reads still work
        }

        // ---------------------------------------------------------------- the timetable follows the schedule
        [DbFact]
        public async Task The_timetable_accepts_only_periods_of_the_schedule_the_class_follows_and_never_a_break()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS4000006");
            var tt = Timetable(db);
            var sched = Schedules(db);
            var regular1 = SlotId(db, w.Regular, 1); var short1 = SlotId(db, w.Short, 1); var lunch = SlotId(db, w.Regular, null, "BREAK");

            // no assignment: everything follows the default (Regular)
            Assert.True((await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, 1, regular1), default)).Status);
            var onShort = await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, 2, short1), default);
            Assert.Equal(400, onShort.StatusCode);
            Assert.Contains("follows \"Regular\"", onShort.Message);
            var onBreak = await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, 2, lunch), default);
            Assert.Equal(400, onBreak.StatusCode);
            Assert.Contains("break", onBreak.Message);

            // Saturday is short for every class; class 2 follows the short schedule every day
            Assert.True((await sched.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() {
                new() { DayOfWeek = 6, ScheduleId = w.Short }, new() { ClassId = w.ClassB, ScheduleId = w.Short } } }, default)).Status);
            Assert.True((await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, 6, short1), default)).Status);                 // class 1 on Saturday: short
            Assert.Equal(400, (await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, 6, regular1, w.Teacher2), default)).StatusCode);   // ... not regular
            Assert.True((await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecB, 3, short1, w.Teacher2), default)).Status);     // class 2 on a weekday: short
            Assert.Equal(400, (await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecB, 3, regular1, w.Teacher2), default)).StatusCode);

            // the same teacher in the same period: still one place at a time (the unique index and the check)
            Assert.Equal(409, (await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecB, 6, short1, w.Teacher), default)).StatusCode);   // Teacher already has short1 on Saturday in class 1

            // the section grid: class 1 follows Regular Monday to Friday and Short on Saturday -> both schedules' slots, with the days each applies to
            var grid = (await tt.GetClassGridAsync(w.School, w.SecA, w.Year, default)).Data!;
            Assert.Contains(grid.Rows, r => r.Period.SlotType == "BREAK" && r.Period.Label == "Lunch" && r.Period.Days.SequenceEqual(new[] { 1, 2, 3, 4, 5 }));
            var short1Row = grid.Rows.Single(r => r.Period.PeriodId == short1);
            Assert.Equal(new[] { 6 }, short1Row.Period.Days);
            Assert.Equal("Short", short1Row.Period.ScheduleName);
            Assert.True(short1Row.Cells.Single(c => c.DayOfWeek == 6).Applies);
            Assert.False(short1Row.Cells.Single(c => c.DayOfWeek == 1).Applies);
            Assert.NotNull(short1Row.Cells.Single(c => c.DayOfWeek == 6).Entry);
            Assert.NotNull(grid.Rows.Single(r => r.Period.PeriodId == regular1).Cells.Single(c => c.DayOfWeek == 1).Entry);
            Assert.Equal(grid.Rows.Select(r => r.Period.StartTime).OrderBy(x => x), grid.Rows.Select(r => r.Period.StartTime));   // several schedules: in clock order
            // class 2 follows only Short: its grid has only Short's two periods
            var gridB = (await tt.GetClassGridAsync(w.School, w.SecB, w.Year, default)).Data!;
            Assert.Equal(new[] { w.Short }, gridB.Rows.Select(r => r.Period.ScheduleId).Distinct());
            Assert.Null(gridB.Rows[0].Period.ScheduleName);   // one schedule: no label needed
        }

        [DbFact]
        public async Task A_change_that_would_leave_a_lesson_on_the_wrong_schedule_is_refused_with_counts()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS4000007");
            var tt = Timetable(db);
            var sched = Schedules(db);
            Assert.True((await tt.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, 1, SlotId(db, w.Regular, 1)), default)).Status);   // class 1, Monday, Regular

            // class 1 -> Short every day would strand that Monday lesson
            var r = await sched.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { new() { ClassId = w.ClassA, ScheduleId = w.Short } } }, default);
            Assert.Equal(409, r.StatusCode);
            Assert.Contains("Nothing was saved", r.Message);
            Assert.Contains("1 on Class 1 Monday", r.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_schedules where academic_year_id={w.Year}"));
            // making Short the default would strand it too
            var d = await sched.SetDefaultAsync(w.School, TaxDb.UserA, w.Short, default);
            Assert.Equal(409, d.StatusCode);
            Assert.Equal(w.Regular, db.Scalar<long>($"select schedule_id from tb_period_schedules where school_id='{w.School}' and is_default"));
            // a Saturday-only assignment does not touch Monday's lesson
            Assert.True((await sched.SaveYearAsync(w.School, TaxDb.UserA, w.Year, new SaveYearSchedulesRequest { Rows = new() { new() { ClassId = w.ClassA, DayOfWeek = 6, ScheduleId = w.Short } } }, default)).Status);
        }
    }
}
