using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S5: class teacher assignments WITH HISTORY. Proves the backfill kept every current teacher current, that reassigning closes the old row and opens
    /// the next (nothing deleted or edited in place), the Q3 confirm, eligibility, closed year, tenant isolation, concurrent changes, and what the database itself refuses.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the S1 to S5 RUN-ME scripts applied.
    /// </summary>
    public class AcademicClassTeachersS5Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";
        private static readonly string[] Backfilled = { "APS261013001", "GHS261103002", "LAS261103001" };

        private static ClassTeacherService Svc(TaxDb db) => new(new ClassTeacherRepository(db.Ctx()), NullLogger<ClassTeacherService>.Instance);
        private static void Expect(string sqlState, Action a) => Assert.Equal(sqlState, Assert.Throws<PostgresException>(a).SqlState);

        private sealed class World
        {
            public string School = "";
            public long Year, Class1, SecA, SecB;
            public string T1 = "", T2 = "", T3 = "";
        }

        private static string Teacher(TaxDb db, string school, string tag, string type = "Teaching", bool withLogin = true)
        {
            var id = "ZT" + school[^5..] + tag;
            if (db.Scalar<int>($"select count(*) from tb_employee_designations where school_id='{school}'") == 0)
            {
                db.Exec($"insert into tb_employee_designations (school_id, designation_name, status_id) values ('{school}', 'Teacher', {Active})");
                db.Exec($"insert into tb_employee_departments (school_id, department_name, status_id) values ('{school}', 'Dept', {Active})");
            }
            db.Exec($@"insert into tb_employees (employee_id, school_id, designation_id, department_id, employment_status_id, employee_type, first_name, last_name, date_of_birth, mobile_number, address, date_of_joining, status_id)
                       values ('{id}', '{school}', (select min(designation_id) from tb_employee_designations where school_id='{school}'), (select min(department_id) from tb_employee_departments where school_id='{school}'),
                               (select min(employment_status_id) from lut_employment_status), '{type}', 'T', 'Teacher{tag}', '1990-01-01', '9876543210', 'x', '2020-01-01', {Active})");
            if (withLogin)
            {
                db.Exec($"insert into tb_users (school_id, user_type, org_user_id, display_name, password_hash, is_first_login, status_id) values ('{school}', 'School Id', '{id}', 'Teacher {tag}', 'x', false, {Active})");
                db.Exec($"insert into tb_user_roles (user_id, role_id) select user_id, 4 from tb_users where org_user_id='{id}'");
            }
            return id;
        }

        private static async Task<World> BuildAsync(TaxDb db, string id)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Class teacher test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");
            var w = new World { School = id, Year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}'") };
            var classes = new ClassService(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
            var sections = new SectionService(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
            w.Class1 = ((ClassResponse)(await classes.CreateAsync(id, new CreateClassRequest { ClassName = "Class 1", SequenceOrder = 1 })).Data!).ClassId;
            w.SecA = ((SectionResponse)(await sections.CreateAsync(id, new CreateSectionRequest { ClassId = w.Class1, SectionName = "A" })).Data!).SectionId;
            w.SecB = ((SectionResponse)(await sections.CreateAsync(id, new CreateSectionRequest { ClassId = w.Class1, SectionName = "B" })).Data!).SectionId;
            (w.T1, w.T2, w.T3) = (Teacher(db, id, "1"), Teacher(db, id, "2"), Teacher(db, id, "3"));
            return w;
        }

        private static AssignClassTeacherRequest Assign(World w, long section, string teacher, string? reason = null, bool confirm = false) =>
            new() { AcademicYearId = w.Year, SectionId = section, EmployeeId = teacher, Reason = reason, ConfirmMultipleSections = confirm };

        // ---------------------------------------------------------------- the backfill kept every current teacher current
        [DbFact]
        public async Task After_the_backfill_every_current_teacher_is_still_current_with_a_start_date_and_no_end()
        {
            using var db = new TaxDb();
            foreach (var school in Backfilled)
            {
                var rows = db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where school_id='{school}'");
                if (rows == 0) continue;   // a school with no class teachers has nothing to carry over
                Assert.Equal(rows, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where school_id='{school}' and status_id={Active} and effective_to is null and effective_from is not null"));
                // the year the assignments were made in (a current year created later may have none yet)
                var year = db.Scalar<long>($"select academic_year_id from tb_class_teacher_assignments where school_id='{school}' group by academic_year_id order by count(*) desc limit 1");
                var list = (await Svc(db).ListAsync(school, year, default)).Data!;
                Assert.Equal(rows, list.Sections.Count(s => s.HasClassTeacher && s.EffectiveFrom != null));
                // the question Attendance asks still says yes for the current teacher of a section
                var (emp, sec) = (db.Scalar<string>($"select employee_id from tb_class_teacher_assignments where school_id='{school}' order by assignment_id limit 1"),
                                  db.Scalar<long>($"select section_id from tb_class_teacher_assignments where school_id='{school}' order by assignment_id limit 1"));
                Assert.True((await Svc(db).IsClassTeacherAsync(school, emp, sec, year, default)).Data!.IsClassTeacher);
            }
        }

        // ---------------------------------------------------------------- assign / reassign / remove with history
        [DbFact]
        public async Task Reassigning_closes_the_old_row_and_opens_the_next_and_nothing_is_deleted_or_edited_in_place()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS5000001");
            var svc = Svc(db);
            var first = await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T1, "start of year"), default);
            Assert.True(first.Status, first.Message);
            Assert.Null(first.Data!.EffectiveTo);
            Assert.Equal("start of year", first.Data.AssignReason);
            var firstId = first.Data.AssignmentId;

            Assert.Equal(first.Data.AssignmentId, (await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T1), default)).Data!.AssignmentId);   // same teacher again: nothing changes
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where school_id='{w.School}'"));

            var second = await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T2, "T1 is on leave"), default);
            Assert.True(second.Status, second.Message);
            Assert.NotEqual(firstId, second.Data!.AssignmentId);
            // the old row: closed, by whom, why; the new one: open
            var hist = (await svc.GetHistoryAsync(w.School, w.Year, w.SecA, default)).Data!;
            Assert.Equal(2, hist.Count);
            Assert.Equal(w.T2, hist[0].EmployeeId); Assert.Null(hist[0].EffectiveTo); Assert.Equal("Active", hist[0].StatusName);
            Assert.Equal(w.T1, hist[1].EmployeeId); Assert.NotNull(hist[1].EffectiveTo); Assert.Equal("Inactive", hist[1].StatusName);
            Assert.Equal("T1 is on leave", hist[1].EndReason);
            Assert.Equal(hist[1].EffectiveTo, hist[0].EffectiveFrom);                       // contiguous: the old ends the day the new starts
            Assert.NotNull(hist[1].EndedByName);
            // the old row is the SAME row (not deleted, not recreated)
            Assert.Equal(firstId, db.Scalar<long>($"select assignment_id from tb_class_teacher_assignments where employee_id='{w.T1}'"));
            Assert.Equal(w.T2, db.Scalar<string>($"select employee_id from tb_class_teacher_assignments where school_id='{w.School}' and effective_to is null"));

            // current readers see only the current teacher
            Assert.False((await svc.IsClassTeacherAsync(w.School, w.T1, w.SecA, w.Year, default)).Data!.IsClassTeacher);
            Assert.True((await svc.IsClassTeacherAsync(w.School, w.T2, w.SecA, w.Year, default)).Data!.IsClassTeacher);
            var marks = new ReportMarksRepository(db.Ctx());
            var active = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");
            Assert.Empty(await marks.ClassTeacherSectionIdsAsync(w.School, w.T1, w.Year, active, default));
            Assert.Contains(w.SecA, await marks.ClassTeacherSectionIdsAsync(w.School, w.T2, w.Year, active, default));
            var list = (await svc.ListAsync(w.School, w.Year, default)).Data!;
            Assert.Equal(w.T2, list.Sections.Single(s => s.SectionId == w.SecA).EmployeeId);
            Assert.Equal(1, list.UnassignedCount);

            // removing: closes the row, nobody is current, history keeps it
            var gone = await svc.UnassignAsync(w.School, TaxDb.UserA, new UnassignClassTeacherRequest { AcademicYearId = w.Year, SectionId = w.SecA, Reason = "post vacant" }, default);
            Assert.True(gone.Status, gone.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where school_id='{w.School}' and effective_to is null"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where school_id='{w.School}'"));
            Assert.Equal(404, (await svc.UnassignAsync(w.School, TaxDb.UserA, new UnassignClassTeacherRequest { AcademicYearId = w.Year, SectionId = w.SecA }, default)).StatusCode);
            // and a new teacher can be assigned after the vacancy, history still contiguous or gapped but never overlapping (the database would refuse)
            Assert.True((await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T3), default)).Status);
            Assert.Equal(3, (await svc.GetHistoryAsync(w.School, w.Year, w.SecA, default)).Data!.Count);
        }

        [DbFact]
        public async Task A_teacher_who_already_has_another_section_needs_a_confirm_that_names_those_sections()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS5000002");
            var svc = Svc(db);
            Assert.True((await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T1), default)).Status);
            var refused = await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecB, w.T1), default);
            Assert.Equal(409, refused.StatusCode);
            Assert.Contains("Class 1 - A", refused.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where section_id={w.SecB}"));
            var ok = await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecB, w.T1, "covers both", confirm: true), default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where employee_id='{w.T1}' and effective_to is null"));
        }

        [DbFact]
        public async Task Only_an_active_teaching_employee_of_the_same_school_with_a_teacher_login_can_be_assigned()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS5000003");
            var other = await BuildAsync(db, "ZZS5000004");
            var svc = Svc(db);
            var clerk = Teacher(db, w.School, "9", type: "Non-Teaching");
            var noLogin = Teacher(db, w.School, "8", withLogin: false);
            Assert.Equal(400, (await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, clerk), default)).StatusCode);
            Assert.Equal(400, (await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, noLogin), default)).StatusCode);
            Assert.Equal(400, (await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, other.T1), default)).StatusCode);        // another school's teacher
            db.Exec($"update tb_employees set status_id={Inactive} where employee_id='{w.T3}'");
            Assert.Equal(400, (await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T3), default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where school_id='{w.School}'"));
            // tenant isolation on the other reads / writes
            Assert.Equal(400, (await svc.AssignAsync(w.School, TaxDb.UserA, new AssignClassTeacherRequest { AcademicYearId = other.Year, SectionId = w.SecA, EmployeeId = w.T1 }, default)).StatusCode);
            Assert.Equal(400, (await svc.AssignAsync(w.School, TaxDb.UserA, new AssignClassTeacherRequest { AcademicYearId = w.Year, SectionId = other.SecA, EmployeeId = w.T1 }, default)).StatusCode);
            Assert.Equal(400, (await svc.GetHistoryAsync(w.School, w.Year, other.SecA, default)).StatusCode);
            Assert.True((await svc.AssignAsync(other.School, TaxDb.UserA, Assign(other, other.SecA, other.T1), default)).Status);
            Assert.Empty((await svc.GetHistoryAsync(w.School, w.Year, w.SecA, default)).Data!);
        }

        [DbFact]
        public async Task A_closed_year_refuses_assigning_and_removing_but_history_can_still_be_read()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS5000005");
            var svc = Svc(db);
            Assert.True((await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T1), default)).Status);
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.Year}");
            db.Exec($"update tb_academic_years set closed_at=now() where academic_year_id={w.Year}");
            var a = await svc.AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, w.T2), default);
            Assert.Equal(409, a.StatusCode);
            Assert.Contains("closed", a.Message);
            Assert.Equal(409, (await svc.UnassignAsync(w.School, TaxDb.UserA, new UnassignClassTeacherRequest { AcademicYearId = w.Year, SectionId = w.SecA }, default)).StatusCode);
            Assert.Single((await svc.GetHistoryAsync(w.School, w.Year, w.SecA, default)).Data!);
        }

        // ---------------------------------------------------------------- concurrency and the database itself
        [DbFact]
        public async Task Six_simultaneous_changes_to_one_section_leave_exactly_one_current_teacher_and_a_clean_history()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS5000006");
            var teachers = new List<string> { w.T1, w.T2, w.T3 };
            for (var i = 4; i <= 6; i++) teachers.Add(Teacher(db, w.School, i.ToString()));
            var results = await Task.WhenAll(teachers.Select(t => Task.Run(() => Svc(db).AssignAsync(w.School, TaxDb.UserA, Assign(w, w.SecA, t), default))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));   // they queue on the school lock; nobody gets an error
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where section_id={w.SecA} and effective_to is null"));
            Assert.Equal(6, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where section_id={w.SecA}"));
            Assert.Equal(5, db.Scalar<int>($"select count(*) from tb_class_teacher_assignments where section_id={w.SecA} and effective_to is not null and status_id={Inactive}"));
            // no two rows overlap (the database guard would have refused it, this proves it from the data)
            Assert.Equal(0, db.Scalar<int>(@"select count(*) from tb_class_teacher_assignments a join tb_class_teacher_assignments b on b.section_id=a.section_id and b.assignment_id>a.assignment_id
                and daterange(a.effective_from, coalesce(a.effective_to,'infinity'),'[)') && daterange(b.effective_from, coalesce(b.effective_to,'infinity'),'[)')"));
        }

        [DbFact]
        public async Task The_database_refuses_overlapping_history_a_current_row_with_an_end_and_a_section_the_year_does_not_offer()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZZS5000007");
            string Ins(string emp, long section, string from, string? to, string status) =>
                $"insert into tb_class_teacher_assignments (school_id, academic_year_id, class_id, section_id, employee_id, status_id, effective_from, effective_to) values ('{w.School}', {w.Year}, {w.Class1}, {section}, '{emp}', {status}, '{from}', {(to == null ? "null" : $"'{to}'")})";
            db.Exec(Ins(w.T1, w.SecA, "2026-06-01", "2026-07-01", Inactive));
            db.Exec(Ins(w.T2, w.SecA, "2026-07-01", null, Active));                                           // starts the day the other ended: valid (half-open)
            Expect(PostgresErrorCodes.ExclusionViolation, () => db.Exec(Ins(w.T3, w.SecA, "2026-06-15", "2026-06-20", Inactive)));   // inside the first row's days
            Expect(PostgresErrorCodes.ExclusionViolation, () => db.Exec(Ins(w.T3, w.SecA, "2026-05-01", "2026-06-02", Inactive)));   // overlaps its start
            // a second current row: refused by the one-active index (unique) and by the no-overlap rule (exclusion); Postgres reports whichever it meets first, so either is the right refusal
            var second = Assert.Throws<PostgresException>(() => db.Exec(Ins(w.T3, w.SecA, "2026-09-01", null, Active)));
            Assert.Contains(second.SqlState, new[] { PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.ExclusionViolation });
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Ins(w.T3, w.SecB, "2026-06-01", "2026-06-10", Active)));         // Active but with an end
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Ins(w.T3, w.SecB, "2026-06-10", null, Inactive)));               // closed status but no end
            Expect(PostgresErrorCodes.CheckViolation, () => db.Exec(Ins(w.T3, w.SecB, "2026-06-10", "2026-06-01", Inactive)));      // ends before it starts
            // a section that is not offered in that year
            db.Exec($"delete from tb_academic_year_sections where academic_year_id={w.Year} and section_id={w.SecB}");
            Expect(PostgresErrorCodes.ForeignKeyViolation, () => db.Exec(Ins(w.T3, w.SecB, "2026-06-10", null, Active)));
            await Task.CompletedTask;
        }
    }
}
