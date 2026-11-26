using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 2a (Docs/smss-st-2a-RUN-ME.sql): the last name is optional; only the first name is mandatory. Every place that prints a student's name must cope with a missing
    /// last name without a stray space. [DbFact]: needs SMSS_TEST_PG and the smss_tpl_2a template.
    /// </summary>
    public class OptionalLastName2aTests
    {
        private const string A = TaxDb.SchoolA;
        private const long Y = TaxDb.YearA;
        private const string S = "GHS001ST001";

        private static void NoLastName(TaxDb db, string student = S, string? middle = null) =>
            db.Exec($"update tb_students set last_name = null, middle_name = {(middle == null ? "null" : $"'{middle}'")} where student_id='{student}'");

        private static string Expected(TaxDb db, string student) =>
            string.Join(' ', new[] { db.Scalar<string>($"select first_name from tb_students where student_id='{student}'"), db.Scalar<string>($"select middle_name from tb_students where student_id='{student}'") }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        private static void Clean(string name, string label)
        {
            Assert.False(string.IsNullOrWhiteSpace(name), label);
            Assert.Equal(name.Trim(), name);                        // no stray space at either end
            Assert.DoesNotContain("  ", name);                     // and none in the middle
            Assert.DoesNotContain("null", name, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------------------------------------ the database
        [DbFact]
        public void The_database_accepts_no_last_name_and_refuses_a_blank_one()
        {
            using var db = new TaxDb();
            NoLastName(db);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students where student_id='{S}' and last_name is null"));
            var ex = Assert.Throws<PostgresException>(() => db.Exec($"update tb_students set last_name = '   ' where student_id='{S}'"));
            Assert.Equal("23514", ex.SqlState);
            var ex2 = Assert.Throws<PostgresException>(() => db.Exec("update tb_admissions set applicant_last_name = '' where admission_id = (select min(admission_id) from tb_admissions)"));
            Assert.Equal("23514", ex2.SqlState);
            db.Exec("update tb_admissions set applicant_last_name = null where admission_id = (select min(admission_id) from tb_admissions)");   // an application may have none either
        }

        // ------------------------------------------------------------------------------------------------ every place that prints a name
        [DbFact]
        public async Task Lists_profiles_receipts_fee_views_and_reports_print_the_name_without_a_stray_space()
        {
            using var db = new TaxDb();
            NoLastName(db, S, "Kumar");
            var want = Expected(db, S);
            Assert.EndsWith("Kumar", want);

            var students = new StudentService(new StudentRepository(db.Ctx()), Fake.Of<IFileStorageService>(), NullLogger<StudentService>.Instance);
            var section = db.Scalar<long>($"select class_section_id from tb_student_enrollments where student_id='{S}' and academic_year_id={Y}");
            var list = (await students.GetStudentsAsync(A, Y, section, null, default)).Data!;
            Assert.Equal(want, list.Single(r => r.StudentId == S).FullName);
            var profile = (await students.GetStudentProfileAsync(A, S, false, default)).Data!;
            Assert.Equal(want, profile.FullName);

            var receipt = (await db.Pay(S, 10m)).Data!;
            Assert.Equal(want, receipt.Student.Name);
            var fees = (await Services.Fees(db).GetFeesAsync(A, S, default)).Data!;
            Assert.Equal(want, fees.StudentName);

            var classId = db.Scalar<long>($"select s.class_id from tb_sections s where s.section_id={section}");
            var report = (await db.Reports().GetClassReportAsync(A, classId, Y, null, true, default)).Data!;
            Clean(report.Students.Single(r => r.StudentId == S).FullName, "class fee report");
            Assert.Equal(want, report.Students.Single(r => r.StudentId == S).FullName);
        }

        [DbFact]
        public async Task The_inventory_recipient_label_and_search_find_a_student_without_a_last_name()
        {
            using var db = new TaxDb();
            NoLastName(db);
            var first = db.Scalar<string>($"select first_name from tb_students where student_id='{S}'");
            var repo = new InventoryRepository(db.Ctx());
            var active = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");
            var one = await repo.GetActiveRecipientAsync(A, "STUDENT", S, active, default);
            Assert.Equal(first, one!.Name);                                   // before the fix this was NULL
            var found = await repo.SearchRecipientsAsync(A, "STUDENT", first, active, 50, default);
            Assert.Contains(found, r => r.Id == S && r.Name == first);        // and the name search never found such a student
        }

        [DbFact]
        public async Task Direct_entry_with_only_a_first_name_is_stored_without_a_last_name()
        {
            using var db = new TaxDb();
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var onboarding = new StudentOnboardingService(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<StudentOnboardingService>.Instance);
            foreach (var (last, label) in new (string?, string)[] { (null, "omitted"), ("   ", "blank") })
            {
                var r = await onboarding.CreateDirectEntryAsync(A, new DirectStudentEntryRequestDto
                {
                    AdmissionDate = DateTime.UtcNow.Date, AcademicYearId = Y, ClassSectionId = section, FirstName = "Solo" + label, LastName = last, DateOfBirth = new DateTime(2018, 5, 5),
                    GuardianName = "G", GuardianRelationship = "FATHER", GuardianConsent = true, GuardianMobileNumber = "9876543210", GuardianEmail = "guardian@example.com", GuardianWhatsAppSameAsMobile = true, 
                }, default);
                Assert.True(r.Status, r.Message);
                Assert.Equal("Solo" + label, r.Data!.FullName);
                Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students where student_id='{r.Data.StudentId}' and last_name is null"));   // a blank became null
            }
        }

        [DbFact]
        public async Task An_application_without_a_last_name_is_confirmed_into_a_student_without_one()
        {
            using var db = new TaxDb();
            db.Exec($@"insert into tb_admissions (school_id, application_number, academic_year_id, applied_class_id, admission_type_id, applicant_first_name, applicant_last_name, date_of_birth,
                       guardian_name, guardian_relationship, guardian_mobile_number, guardian_email, guardian_whatsapp_number, application_date, status_id)
                       values ('{A}', 'ZZ2A-1', {Y}, (select min(class_id) from tb_classes where school_id='{A}'), (select min(admission_type_id) from lut_admission_type), 'Aarav', null, '2018-01-01',
                       'G', 'FATHER', '9876543210', 'guardian@example.com', '+919876543210', current_date, (select sid from lut_status where sname='APPLICATION_SUBMITTED'))");
            var id = db.Scalar<long>("select admission_id from tb_admissions where application_number='ZZ2A-1'");
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var svc = new AdmissionService(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);
            var detail = (await svc.GetByIdAsync(A, id, default)).Data!;
            Assert.Equal("Aarav", detail.ApplicantFullName);
            var confirmed = await svc.ConfirmAdmissionAsync(A, id, db.Scalar<long>("select min(user_id) from tb_users"), new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.True(confirmed.Status, confirmed.Message);
            var student = db.Scalar<string>($"select student_id from tb_admissions where admission_id={id}");
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students where student_id='{student}' and last_name is null and first_name='Aarav'"));
        }

        // ------------------------------------------------------------------------------------------------ Excel import
        private static byte[] Sheet(params string[][] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Students");
            var headers = new[] { "Admission number", "Full name", "Gender", "Date of birth", "Class", "Section", "Roll number", "Father name", "Father phone", "Primary guardian WhatsApp", "Primary guardian email", "Parent consent (Yes/No)" };
            for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++)
            {
                var cells = rows[r].Length == 9 ? rows[r].Concat(new[] { "SAME", "guardian@example.com" }).ToArray() : rows[r];   // a 9-column row gets the two guardian columns filled in
                for (var c = 0; c < cells.Length; c++) ws.Cell(r + 2, c + 1).Value = cells[c];
                ws.Cell(r + 2, 12).Value = "Yes";
            }
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }

        [DbFact]
        public async Task The_excel_import_accepts_a_one_word_name_as_a_first_name_and_imports_it()
        {
            using var db = new TaxDb();
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var cls = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={section}");
            var sec = db.Scalar<string>($"select section_name from tb_sections where section_id={section}");
            string[] Row(string name) => new[] { "", name, "Male", "05/05/2018", cls, sec, "", "Father", "9876543210" };
            var file = Sheet(Row("Asha"), Row("Bina Rawat"), Row("Chitra Devi Rawat"));
            var preview = (await svc.PreviewAsync(A, file, default)).Data!;
            Assert.Empty(preview.FileErrors);
            Assert.All(preview.Rows, r => Assert.Equal("OK", r.Status));        // no "enter the first and the last name" error any more
            Assert.True(preview.Summary.CanImport);
            var committed = await svc.CommitAsync(A, file, default);
            Assert.True(committed.Status, committed.Message);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}' and first_name='Asha' and last_name is null and middle_name is null"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}' and first_name='Bina' and last_name='Rawat' and middle_name is null"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}' and first_name='Chitra' and last_name='Rawat' and middle_name='Devi'"));
        }

        [DbFact]
        public async Task The_import_duplicate_warning_works_when_the_existing_student_has_no_last_name()
        {
            using var db = new TaxDb();
            NoLastName(db);
            var first = db.Scalar<string>($"select first_name from tb_students where student_id='{S}'");
            var dob = db.Scalar<string>($"select to_char(date_of_birth, 'DD/MM/YYYY') from tb_students where student_id='{S}'");
            var phone = db.Scalar<string>($"select g.mobile_number from tb_student_guardians sg join tb_guardians g on g.guardian_id=sg.guardian_id where sg.student_id='{S}' limit 1");
            var section = db.Scalar<long>($"select class_section_id from tb_student_enrollments where student_id='{S}' and academic_year_id={Y}");
            var cls = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={section}");
            var sec = db.Scalar<string>($"select section_name from tb_sections where section_id={section}");
            var file = Sheet(new[] { "", first, "Male", dob, cls, sec, "", "Father", phone });
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var row = (await svc.PreviewAsync(A, file, default)).Data!.Rows.Single();
            Assert.Contains(row.Messages, m => m.StartsWith("Looks like a student already in the school (" + first + ", " + S));   // no trailing space or "null" before the comma
        }

        // ------------------------------------------------------------------------------------------------ unit: the shared name helpers
        [Fact]
        public void The_full_name_helpers_skip_a_missing_last_name()
        {
            Assert.Equal("Asha", LeaveRules.FullName("Asha", null, null));
            Assert.Equal("Asha Devi", LeaveRules.FullName("Asha", "Devi", null));
            Assert.Equal("Asha Rawat", LeaveRules.FullName("Asha", null, "Rawat"));
            Assert.Equal("Asha", LeaveRules.FullName("Asha", " ", "  "));
        }
    }
}
