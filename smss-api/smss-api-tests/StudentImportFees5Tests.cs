using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Excel import of existing students assigns the class fee structure AFTER the import commits, with the same code as admission and the "Assign fees" repair. The preview names the classes with no
    /// structure; back-dating only with the tick, a confirmation and a reason (rows in tb_fee_backdating_log); a failure for one student never undoes the import. [DbFact]: needs SMSS_TEST_PG.
    /// </summary>
    public class StudentImportFees5Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";

        private static StudentImportService Svc(TaxDb db)
        {
            var c = db.Ctx();   // one context, as in the API
            var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(c), new StudentFeeRepository(c), NullLogger<StudentFeeBillingService>.Instance);
            return new StudentImportService(new StudentImportRepository(c), NullLogger<StudentImportService>.Instance, new FeeRepairRepository(c), new AdmissionRepository(c), billing);
        }

        /// <summary>A school with the current year 2026-27 (already started), Class 1 with an active structure (Tuition monthly 1000, due day 10 from April), Class 2 without one.</summary>
        private static void Build(TaxDb db, string s)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_short_code, school_status_id) values ('{s}', '{s}-C', 'Import fee test {s}', 'Z{s[^3..]}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{s}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");
            db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{s}', 'Class 1', 1, {Active}), ('{s}', 'Class 2', 2, {Active})");
            db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select '{s}', class_id, 'A', {Active} from tb_classes where school_id='{s}'");
            db.Exec($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, is_optional, isactive) values ('{s}', 'Tuition', 'TUI', false, true)");
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{s}', (select academic_year_id from tb_academic_years where school_id='{s}'), class_id, {Active} from tb_classes where school_id='{s}'");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select '{s}', (select academic_year_id from tb_academic_years where school_id='{s}'), class_id, section_id, {Active} from tb_sections where school_id='{s}'");
            var monthly = db.Scalar<long>("select frequency_id from lut_fee_frequency where frequency_code='MONTHLY'");
            var fs = db.Scalar<long>($"insert into tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive) values ('{s}', (select academic_year_id from tb_academic_years where school_id='{s}'), (select class_id from tb_classes where school_id='{s}' and class_name='Class 1'), 'Class 1 Fees', true) returning fee_structure_id");
            db.Exec($"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, due_day, effective_from) values ({fs}, (select fee_type_id from tb_fee_types where school_id='{s}'), {monthly}, 1000, 10, '2026-04-01')");
        }

        private static byte[] Sheet(params string[][] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Students");
            var headers = new[] { "Admission number", "Full name", "Gender", "Date of birth", "Class", "Section", "Roll number", "Father name", "Father phone", "Primary guardian WhatsApp", "Primary guardian email", "Parent consent (Yes/No)" };
            for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++)
            {
                var cells = rows[r].Concat(new[] { "SAME", "guardian@example.com" }).ToArray();
                for (var c = 0; c < cells.Length; c++) ws.Cell(r + 2, c + 1).Value = cells[c];
                ws.Cell(r + 2, 12).Value = "Yes";
            }
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }

        private static string[] Row(string name, string cls, string roll, string phone) => new[] { "", name, "Male", "05/05/2018", cls, "A", roll, "Father", phone };

        private static string IdOf(TaxDb db, string school, string firstName) => db.Scalar<string>($"select student_id from tb_students where school_id='{school}' and first_name='{firstName}'");

        [DbFact]
        public async Task Imported_students_get_their_class_structure_after_the_commit_and_the_preview_names_the_class_with_none()
        {
            using var db = new TaxDb(); Build(db, "ZIA");
            var file = Sheet(Row("Ann One", "Class 1", "1", "9876543201"), Row("Bob Two", "Class 1", "2", "9876543202"), Row("Cy Three", "Class 2", "1", "9876543203"));
            var svc = Svc(db);
            var preview = await svc.PreviewAsync("ZIA", file, default);
            Assert.True(preview.Status, preview.Message);
            var none = Assert.Single(preview.Data!.ClassesWithoutFeeStructure);
            Assert.Equal(("Class 2", 1), (none.ClassName, none.Students));
            Assert.True(preview.Data.YearStarted);
            Assert.Equal(new DateOnly(2026, 4, 1), preview.Data.YearStart);

            var r = await svc.CommitAsync("ZIA", file, default, TaxDb.UserA);
            Assert.True(r.Status, r.Message);
            Assert.Equal(3, r.Data!.Imported);
            Assert.Equal((2, 1, false), (r.Data.FeesAssigned, r.Data.FeesNoStructure, r.Data.FeesBackdated));
            Assert.Empty(r.Data.FeesFailed);
            Assert.Contains(r.Data.Notes, n => n.Contains("no fee structure"));
            var ann = IdOf(db, "ZIA", "Ann"); var cy = IdOf(db, "ZIA", "Cy");
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_fee_structure where school_id='ZIA' and student_id='{ann}'"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_fee_structure where school_id='ZIA' and student_id='{cy}'"));    // no structure for Class 2: no fees, the student is still imported
            Assert.True(db.Scalar<int>($"select count(*) from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{ann}'") > 0);
            // no tick: nothing is back-dated (no log, no due before today, nothing marked BACKDATED)
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_backdating_log where school_id='ZIA'"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.school_id='ZIA' and d.source='BACKDATED'"));
            Assert.True(db.Scalar<string>($"select min(d.due_date)::text from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{ann}'").CompareTo(SchoolClock.Today().ToString("yyyy-MM-dd")) >= 0);
        }

        [DbFact]
        public async Task With_the_tick_the_dues_are_charged_from_the_year_start_and_logged_and_without_a_confirmation_or_reason_nothing_is_imported()
        {
            using var db = new TaxDb(); Build(db, "ZIB");
            var file = Sheet(Row("Ann One", "Class 1", "1", "9876543201"), Row("Bob Two", "Class 1", "2", "9876543202"));
            var svc = Svc(db);
            var noConfirm = await svc.CommitAsync("ZIB", file, default, TaxDb.UserA, new ImportFeeOptions { ChargeFromYearStart = true, ConfirmBackdating = false, Reason = "Old students" });
            Assert.Equal(400, noConfirm.StatusCode);
            var noReason = await svc.CommitAsync("ZIB", file, default, TaxDb.UserA, new ImportFeeOptions { ChargeFromYearStart = true, ConfirmBackdating = true, Reason = "  " });
            Assert.Equal(400, noReason.StatusCode);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_students where school_id='ZIB'"));          // refused BEFORE anything was imported

            var r = await svc.CommitAsync("ZIB", file, default, TaxDb.UserA, new ImportFeeOptions { ChargeFromYearStart = true, ConfirmBackdating = true, Reason = "Students were already studying" });
            Assert.True(r.Status, r.Message);
            Assert.Equal((2, true), (r.Data!.FeesAssigned, r.Data.FeesBackdated));
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_fee_backdating_log where school_id='ZIB' and kind='STUDENT' and reason='Students were already studying' and created_by=" + TaxDb.UserA));
            var ann = IdOf(db, "ZIB", "Ann");
            Assert.Equal("2026-04-10", db.Scalar<string>($"select min(d.due_date)::text from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{ann}'"));
            Assert.True(db.Scalar<int>($"select count(*) from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{ann}' and d.source='BACKDATED'") > 0);
        }

        [DbFact]
        public async Task A_fee_failure_for_one_student_never_undoes_the_import_and_is_listed_without_technical_detail()
        {
            using var db = new TaxDb(); Build(db, "ZIC");
            db.Exec(@"create function zz_boom() returns trigger as $f$ begin
                        if exists (select 1 from tb_students where student_id = new.student_id and first_name = 'Boom') then raise exception 'boom: secret detail'; end if; return new; end $f$ language plpgsql;
                      create trigger zz_boom_t before insert on tb_student_fee_structure for each row execute function zz_boom();");
            var file = Sheet(Row("Ann One", "Class 1", "1", "9876543201"), Row("Boom Two", "Class 1", "2", "9876543202"));
            var r = await Svc(db).CommitAsync("ZIC", file, default, TaxDb.UserA);
            Assert.True(r.Status, r.Message);
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_students where school_id='ZIC'"));            // both imported
            Assert.Equal((2, 1, 1), (r.Data!.Imported, r.Data.FeesAssigned, r.Data.FeesFailed.Count));
            var f = r.Data.FeesFailed[0];
            Assert.Equal(IdOf(db, "ZIC", "Boom"), f.StudentId);
            Assert.Equal(FeeAutoAssignment.AssignFailedWarning, f.Reason);
            Assert.DoesNotContain("boom", f.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(r.Data.Notes, n => n.Contains("could not be assigned"));
        }

        [DbFact]
        public async Task The_import_never_touches_another_schools_fees_and_only_a_school_admin_can_commit()
        {
            using var db = new TaxDb(); Build(db, "ZID"); Build(db, "ZIE");
            db.Exec("insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id) " +
                    "values ('ZIEX1', 'ZIE', 'ZIEX1-N', 'Other', 'School', '2015-01-01', (select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION'), '2026-04-01', " + Active + ")");
            db.Exec("insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id) " +
                    "select 'ZIE', 'ZIEX1', y.academic_year_id, (select section_id from tb_sections where school_id='ZIE' limit 1), '2026-04-01', (select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION'), " + Active + " from tb_academic_years y where y.school_id='ZIE'");
            var r = await Svc(db).CommitAsync("ZID", Sheet(Row("Ann One", "Class 1", "1", "9876543201")), default, TaxDb.UserA);
            Assert.True(r.Status, r.Message);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_fee_structure where school_id='ZIE'"));   // the other school's student without fees was NOT given any
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_student_fee_structure where school_id='ZID'"));
            Assert.Equal(1, r.Data!.FeesAssigned);

            foreach (var role in new[] { "Teacher", "Accountant", "Principal", "Student", "Guardian", "Admission Staff", "Super Admin" })
            {
                var c = Fake.WithUser(new StudentImportController(Svc(db), null!), Fake.User("ZID", 5, role));
                var res = Assert.IsType<ObjectResult>(await c.Commit(null, true, true, "x", default));
                Assert.Equal(403, res.StatusCode);
            }
        }
    }
}
