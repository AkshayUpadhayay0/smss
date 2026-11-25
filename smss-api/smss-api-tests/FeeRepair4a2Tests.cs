using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 4a-2 (no migration): "Assign fees for a year". Active students enrolled in the year with NO fee structure get the active structure of their class through the admission code
    /// (assignment, items, dues); a student who already has one is never touched; a class without a structure is left out and reported; optional-fee choices carry over from the previous year;
    /// a failure for one student never stops the others; a second run assigns nothing; the school comes from the token and another school's year is a 404. [DbFact]: needs SMSS_TEST_PG.
    /// </summary>
    public class FeeRepair4a2Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";

        private static FeeRepairService Svc(TaxDb db)
        {
            var c = db.Ctx();   // one context for all of it, as in the API (one scoped context per request)
            var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(c), new StudentFeeRepository(c), NullLogger<StudentFeeBillingService>.Instance);
            return new FeeRepairService(new FeeRepairRepository(c), new StudentFeeBillingRepository(c), new AdmissionRepository(c), billing, NullLogger<FeeRepairService>.Instance);
        }

        private sealed class World { public string School = "", Other = ""; public long Prev, Cur, OtherCur, C1, C2, Tuition, Bus; }

        private static void Student(TaxDb db, string school, string id, long classSectionYear, long year, bool active = true) =>
            db.Exec($@"insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id)
                       values ('{id}', '{school}', '{id}-N', 'Stu', '{id}', '2015-01-01', (select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION'), '2026-04-01', {(active ? Active : Inactive)});
                       insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id)
                       values ('{school}', '{id}', {year}, {classSectionYear}, '2026-04-01', (select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION'), {Active});");

        /// <summary>
        /// School: previous year 2025-26 and the current year 2026-27; Class 1 (section A) has an active structure in 2026-27 (Tuition monthly + the optional Bus), Class 2 has none.
        /// Students: S1 and S6 (Class 1, no fees yet; S6 chose the optional Bus last year), S2 (Class 1, already has the structure), S3 (Class 2, no structure to give), S4 (inactive).
        /// Another school has a student of its own without fees.
        /// </summary>
        private static World Build(TaxDb db, string id)
        {
            var w = new World { School = id, Other = id + "X" };
            foreach (var s in new[] { w.School, w.Other })
            {
                db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{s}', '{s}-C', 'Fee repair test {s}', {Active})");
                db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{s}', '2025-26', '2025-04-01', '2026-03-31', false, {Active}), ('{s}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");
                db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{s}', 'Class 1', 1, {Active}), ('{s}', 'Class 2', 2, {Active})");
                db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select '{s}', class_id, 'A', {Active} from tb_classes where school_id='{s}'");
                db.Exec($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, is_optional, isactive) values ('{s}', 'Tuition', 'TUI', false, true), ('{s}', 'Bus', 'BUS', true, true)");
                db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{s}', (select academic_year_id from tb_academic_years where school_id='{s}' and is_current), class_id, {Active} from tb_classes where school_id='{s}'");
                db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select '{s}', (select academic_year_id from tb_academic_years where school_id='{s}' and is_current), class_id, section_id, {Active} from tb_sections where school_id='{s}'");
            }
            long Y(string s, string n) => db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{s}' and year_name='{n}'");
            long Cl(string s, string n) => db.Scalar<long>($"select class_id from tb_classes where school_id='{s}' and class_name='{n}'");
            long Sec(string s, string n) => db.Scalar<long>($"select section_id from tb_sections where school_id='{s}' and class_id={Cl(s, n)}");
            long Ft(string s, string c) => db.Scalar<long>($"select fee_type_id from tb_fee_types where school_id='{s}' and fee_type_code='{c}'");
            (w.Prev, w.Cur, w.OtherCur, w.C1, w.C2) = (Y(w.School, "2025-26"), Y(w.School, "2026-27"), Y(w.Other, "2026-27"), Cl(w.School, "Class 1"), Cl(w.School, "Class 2"));
            (w.Tuition, w.Bus) = (Ft(w.School, "TUI"), Ft(w.School, "BUS"));
            var monthly = db.Scalar<long>("select frequency_id from lut_fee_frequency where frequency_code='MONTHLY'");
            foreach (var s in new[] { w.School, w.Other })
            {
                var fs = db.Scalar<long>($"insert into tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive) values ('{s}', {Y(s, "2026-27")}, {Cl(s, "Class 1")}, 'Class 1 Fees', true) returning fee_structure_id");
                db.Exec($"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, due_day, effective_from) values ({fs}, {Ft(s, "TUI")}, {monthly}, 1000, 10, '2026-04-01'), ({fs}, {Ft(s, "BUS")}, {monthly}, 300, 10, '2026-04-01')");
            }
            var sec1 = Sec(w.School, "Class 1"); var sec2 = Sec(w.School, "Class 2");
            Student(db, w.School, "ZR1", sec1, w.Cur); Student(db, w.School, "ZR6", sec1, w.Cur); Student(db, w.School, "ZR2", sec1, w.Cur);
            Student(db, w.School, "ZR3", sec2, w.Cur); Student(db, w.School, "ZR4", sec1, w.Cur, active: false);
            Student(db, w.Other, "ZRX", Sec(w.Other, "Class 1"), w.OtherCur);
            // ZR2 already has the structure; ZR6 chose the optional Bus last year
            var cls1 = db.Scalar<long>($"select fee_structure_id from tb_fee_structures where school_id='{w.School}' and class_id={w.C1}");
            db.Exec($"insert into tb_student_fee_structure (student_id, fee_structure_id, school_id, academic_year_id) values ('ZR2', {cls1}, '{w.School}', {w.Cur})");
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount, is_applicable) values ('ZR6', '{w.School}', {w.Prev}, {w.Bus}, {monthly}, 250, 250, true), ('ZR6', '{w.School}', {w.Prev}, {w.Tuition}, {monthly}, 900, 900, true)");
            return w;
        }

        private static string Fingerprint(TaxDb db, string school) => db.Scalar<string>($@"select md5(coalesce(string_agg(x, '|' order by x), '') ) from (
            select f::text x from tb_student_fee_structure f where school_id='{school}' union all select i::text from tb_student_fee_items i where school_id='{school}'
            union all select d::text from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.school_id='{school}') q");

        [DbFact]
        public async Task Students_without_fees_get_their_class_structure_and_nobody_else_is_touched()
        {
            using var db = new TaxDb();
            var w = Build(db, "ZRA");
            var other = Fingerprint(db, w.Other);
            var before2 = db.Scalar<string>("select f::text from tb_student_fee_structure f where student_id='ZR2'");
            var r = await Svc(db).RunAsync(w.School, w.Cur, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(2, r.Data!.Assigned);                // ZR1 and ZR6
            Assert.Equal(1, r.Data.SkippedNoStructure);       // ZR3: Class 2 has no structure
            Assert.Empty(r.Data.Failed);
            Assert.Equal(0, r.Data.Remaining);
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_student_fee_structure where school_id='{w.School}' and academic_year_id={w.Cur}"));   // ZR2 + the two new
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_fee_structure where student_id in ('ZR3','ZR4')"));                           // no structure to give / inactive student
            Assert.Equal(before2, db.Scalar<string>("select f::text from tb_student_fee_structure f where student_id='ZR2'"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_fee_items where student_id='ZR1' and academic_year_id={w.Cur}"));
            Assert.True(db.Scalar<int>($"select count(*) from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='ZR1' and i.academic_year_id={w.Cur}") > 0);
            Assert.Equal(other, Fingerprint(db, w.Other));    // another school is untouched
        }

        [DbFact]
        public async Task The_optional_fee_choice_of_last_year_carries_over_and_without_one_the_optional_fee_is_not_applied()
        {
            using var db = new TaxDb();
            var w = Build(db, "ZRB");
            Assert.True((await Svc(db).RunAsync(w.School, w.Cur, default)).Status);
            Assert.True(db.Scalar<bool>($"select is_applicable from tb_student_fee_items where student_id='ZR6' and academic_year_id={w.Cur} and fee_type_id={w.Bus}"));
            Assert.False(db.Scalar<bool>($"select is_applicable from tb_student_fee_items where student_id='ZR1' and academic_year_id={w.Cur} and fee_type_id={w.Bus}"));
            Assert.True(db.Scalar<bool>($"select is_applicable from tb_student_fee_items where student_id='ZR1' and academic_year_id={w.Cur} and fee_type_id={w.Tuition}"));   // a compulsory fee always applies
        }

        [DbFact]
        public async Task A_second_run_assigns_nothing_and_changes_nothing()
        {
            using var db = new TaxDb();
            var w = Build(db, "ZRC");
            Assert.Equal(2, (await Svc(db).RunAsync(w.School, w.Cur, default)).Data!.Assigned);
            var fp = Fingerprint(db, w.School);
            var again = await Svc(db).RunAsync(w.School, w.Cur, default);
            Assert.True(again.Status, again.Message);
            Assert.Equal(0, again.Data!.Assigned);
            Assert.Equal(1, again.Data.SkippedNoStructure);
            Assert.Equal(fp, Fingerprint(db, w.School));
        }

        [DbFact]
        public async Task The_preview_counts_the_students_and_names_the_classes_without_a_structure_and_saves_nothing()
        {
            using var db = new TaxDb();
            var w = Build(db, "ZRD");
            var fp = Fingerprint(db, w.School);
            var p = await Svc(db).PreviewAsync(w.School, w.Cur, default);
            Assert.True(p.Status, p.Message);
            Assert.Equal(3, p.Data!.MissingCount);      // ZR1, ZR6, ZR3 (ZR2 has fees, ZR4 is inactive)
            Assert.Equal(2, p.Data.WouldAssign);
            var no = Assert.Single(p.Data.ClassesWithoutStructure);
            Assert.Equal(("Class 2", 1), (no.ClassName, no.Students));
            Assert.Equal(fp, Fingerprint(db, w.School));
        }

        [DbFact]
        public async Task A_failure_for_one_student_does_not_stop_the_others_and_is_listed()
        {
            using var db = new TaxDb();
            var w = Build(db, "ZRE");
            db.Exec(@"create function zr_boom() returns trigger language plpgsql as $$ begin if new.student_id = 'ZR1' then raise exception 'boom'; end if; return new; end $$;
                      create trigger zr_boom before insert on tb_student_fee_items for each row execute function zr_boom();");
            var r = await Svc(db).RunAsync(w.School, w.Cur, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(1, r.Data!.Assigned);                // ZR6 still got its fees
            var f = Assert.Single(r.Data.Failed);
            Assert.Equal("ZR1", f.StudentId);
            Assert.False(string.IsNullOrWhiteSpace(f.Reason));
            Assert.DoesNotContain("boom", f.Reason);          // no technical detail reaches the screen
            Assert.True(db.Scalar<int>("select count(*) from tb_student_fee_items where student_id='ZR6'") > 2);
        }

        [DbFact]
        public async Task Another_schools_year_is_a_404_for_both_actions_and_a_closed_year_is_refused()
        {
            using var db = new TaxDb();
            var w = Build(db, "ZRF");
            var fp = Fingerprint(db, w.Other);
            Assert.Equal(404, (await Svc(db).PreviewAsync(w.School, w.OtherCur, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).RunAsync(w.School, w.OtherCur, default)).StatusCode);
            Assert.Equal(400, (await Svc(db).RunAsync(null, w.Cur, default)).StatusCode);
            Assert.Equal(fp, Fingerprint(db, w.Other));
            db.Exec($"update tb_academic_years set closed_at = now(), closed_by = {TaxDb.UserA} where academic_year_id = {w.Prev}");
            var closed = await Svc(db).RunAsync(w.School, w.Prev, default);
            Assert.Equal(400, closed.StatusCode);
            Assert.Contains("closed", closed.Message);
        }

        // ------------------------------------------------------------------------------------------------ who may do what
        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_School_Admin_may_preview_or_run_it(string role)
        {
            var svc = Fake.Of<IFeeRepairService>();
            var c = Fake.WithUser(new FeeRepairController(svc), Fake.User("S", 5, role));
            var preview = Fake.Code(await c.Missing(3, default));
            var run = Fake.Code(await c.Assign(3, default));
            if (role == "School Admin") { Assert.NotEqual(403, preview); Assert.NotEqual(403, run); } else { Assert.Equal(403, preview); Assert.Equal(403, run); }
            Assert.Equal(role == "School Admin", Fake.ProxyOf(svc).Calls.Contains("RunAsync"));
        }
    }
}
