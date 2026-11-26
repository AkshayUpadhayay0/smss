using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
    /// Stage 2b (Docs/smss-st-2b-RUN-ME.sql): the primary guardian's WhatsApp number and email are mandatory (application, Add Existing Student, Confirm Admission, Excel import), numbers are
    /// stored with the country code, and the school has a WhatsApp number (mandatory at registration and on edit) and a location only the Super Admin sets. [DbFact]: needs SMSS_TEST_PG and smss_tpl_2b.
    /// </summary>
    public class ContactNumbers2bTests
    {
        private const string A = TaxDb.SchoolA;
        private const long Y = TaxDb.YearA;

        // ------------------------------------------------------------------------------------------------ the number rules (no database)
        [Theory]
        [InlineData("9876543210", "+919876543210")]
        [InlineData(" 98765 43210 ", "+919876543210")]
        [InlineData("+91 98765-43210", "+919876543210")]
        [InlineData("919876543210", "+919876543210")]
        [InlineData("09876543210", "+919876543210")]
        [InlineData("+44 7911 123456", "+447911123456")]
        public void A_valid_number_is_stored_with_its_country_code(string raw, string stored) => Assert.Equal(stored, PhoneNumbers.Normalise(raw));

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("12345")] [InlineData("5876543210")] [InlineData("+91 5876543210")] [InlineData("+910000000000")]
        [InlineData("98765abcde")] [InlineData("+0123456789")] [InlineData("+1234567")] [InlineData("+1234567890123456")] [InlineData("98765432101")]
        public void A_blank_or_invalid_number_is_not_stored(string? raw) => Assert.Null(PhoneNumbers.Normalise(raw));

        [Fact]
        public void Same_as_mobile_uses_the_mobile_number_whatever_was_typed_in_the_whatsapp_box()
        {
            Assert.Equal("+919876543210", PhoneNumbers.WhatsAppFor("9000000000", true, "9876543210"));
            Assert.Equal("+919000000000", PhoneNumbers.WhatsAppFor("9000000000", false, "9876543210"));
            Assert.Null(PhoneNumbers.WhatsAppError(null, true, "9876543210", "The guardian's"));
            Assert.Contains("mobile number is not valid", PhoneNumbers.WhatsAppError(null, true, "123", "The guardian's"));
            Assert.Contains("is required", PhoneNumbers.WhatsAppError(null, false, "9876543210", "The guardian's"));
            Assert.Contains("is not valid", PhoneNumbers.WhatsAppError("12345", false, "9876543210", "The guardian's"));
        }

        [Theory]
        [InlineData("a@b.co", true)] [InlineData("  parent@example.com ", true)] [InlineData("", false)] [InlineData(null, false)] [InlineData("not-an-email", false)] [InlineData("a@", false)]
        public void The_email_is_checked_for_format_only(string? email, bool ok) => Assert.Equal(ok, PhoneNumbers.EmailError(email, "x") == null);

        private static List<ValidationResult> Validate(object o)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(o, new ValidationContext(o), results, validateAllProperties: true);
            return results;
        }

        [Fact]
        public void The_application_and_direct_entry_requests_refuse_a_missing_whatsapp_number_and_email_and_accept_the_tick()
        {
            var app = new SubmitAdmissionRequestDto
            {
                AcademicYearId = 1, AppliedClassId = 1, ApplicantFirstName = "A", DateOfBirth = new DateTime(2018, 1, 1), GuardianName = "G", GuardianRelationship = "MOTHER", GuardianConsent = true, GuardianMobileNumber = "9876543210",
            };
            var errors = Validate(app).Select(r => r.ErrorMessage).ToList();
            Assert.Contains(errors, m => m!.Contains("WhatsApp number is required"));
            Assert.Contains(errors, m => m!.Contains("email is required"));
            app.GuardianEmail = "g@example.com"; app.GuardianWhatsAppSameAsMobile = true;
            Assert.Empty(Validate(app));
            app.GuardianWhatsAppSameAsMobile = false; app.GuardianWhatsAppNumber = "+91 98765 43210";
            Assert.Empty(Validate(app));
            app.GuardianEmail = "nope";
            Assert.Contains(Validate(app), r => r.ErrorMessage!.Contains("not a valid email"));

            var direct = new DirectStudentEntryRequestDto
            {
                AdmissionDate = DateTime.UtcNow.Date, AcademicYearId = 1, ClassSectionId = 1, FirstName = "A", DateOfBirth = new DateTime(2018, 1, 1), GuardianName = "G", GuardianRelationship = "MOTHER", GuardianConsent = true, GuardianMobileNumber = "9876543210",
            };
            Assert.Equal(2, Validate(direct).Count);
            direct.GuardianEmail = "g@example.com"; direct.GuardianWhatsAppSameAsMobile = true;
            Assert.Empty(Validate(direct));
        }

        // ------------------------------------------------------------------------------------------------ application, edit, confirm
        private static AdmissionService Admissions(TaxDb db, INotificationService? notify = null) =>
            new(new AdmissionRepository(db.Ctx()), notify ?? Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);

        private static SubmitAdmissionRequestDto App(TaxDb db, string first = "Rhea", string? email = "g@example.com", string? whatsapp = null, bool same = true) => new()
        {
            AcademicYearId = Y, AppliedClassId = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{A}'"), AdmissionTypeId = db.Scalar<long>("select min(admission_type_id) from lut_admission_type"),
            ApplicantFirstName = first, DateOfBirth = new DateTime(2018, 3, 3), GuardianName = "Mother", GuardianRelationship = "MOTHER", GuardianConsent = true, GuardianMobileNumber = "9876543210",
            GuardianEmail = email, GuardianWhatsAppNumber = whatsapp, GuardianWhatsAppSameAsMobile = same,
        };

        [DbFact]
        public async Task A_new_application_stores_the_whatsapp_number_with_the_country_code_and_refuses_a_missing_one()
        {
            using var db = new TaxDb();
            var svc = Admissions(db);
            var ok = await svc.SubmitAsync(A, App(db), default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("+919876543210", ok.Data!.GuardianWhatsAppNumber);
            Assert.Equal("+919876543210", db.Scalar<string>($"select guardian_whatsapp_number from tb_admissions where admission_id={ok.Data.AdmissionId}"));

            var typed = await svc.SubmitAsync(A, App(db, "Typed", whatsapp: "09000000001", same: false), default);
            Assert.Equal("+919000000001", typed.Data!.GuardianWhatsAppNumber);

            Assert.Equal(400, (await svc.SubmitAsync(A, App(db, "NoWa", whatsapp: null, same: false), default)).StatusCode);
            Assert.Equal(400, (await svc.SubmitAsync(A, App(db, "BadWa", whatsapp: "12345", same: false), default)).StatusCode);
            var noMail = await svc.SubmitAsync(A, App(db, "NoMail", email: null), default);
            Assert.Equal(400, noMail.StatusCode); Assert.Contains("email is required", noMail.Message);
            Assert.Equal(400, (await svc.SubmitAsync(A, App(db, "BadMail", email: "x@"), default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_admissions where school_id='{A}' and applicant_first_name in ('NoWa','BadWa','NoMail','BadMail')"));
        }

        private static long OldApplication(TaxDb db, string number, string status = "APPLICATION_SUBMITTED")
        {
            // an application made before these two were collected
            db.Exec($@"insert into tb_admissions (school_id, application_number, academic_year_id, applied_class_id, admission_type_id, applicant_first_name, date_of_birth,
                       guardian_name, guardian_relationship, guardian_mobile_number, application_date, status_id)
                       values ('{A}', '{number}', {Y}, (select min(class_id) from tb_classes where school_id='{A}'), (select min(admission_type_id) from lut_admission_type), 'Old', '2018-01-01',
                       'G', 'FATHER', '9876543210', current_date, (select sid from lut_status where sname='{status}'))");
            return db.Scalar<long>($"select admission_id from tb_admissions where application_number='{number}'");
        }

        [DbFact]
        public async Task Confirming_an_application_without_the_guardians_whatsapp_number_and_email_is_refused_and_creates_nothing()
        {
            using var db = new TaxDb();
            var id = OldApplication(db, "ZZ2B-OLD");
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var refused = await Admissions(db).ConfirmAdmissionAsync(A, id, db.Scalar<long>("select min(user_id) from tb_users"), new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.Equal(409, refused.StatusCode);
            Assert.Contains("WhatsApp number and email are missing", refused.Message);
            Assert.Contains("Edit the application", refused.Message);
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));         // nothing created
        }

        [DbFact]
        public async Task An_added_application_without_them_stays_editable_and_once_completed_it_confirms_with_the_guardian_stored_and_no_login_created()
        {
            using var db = new TaxDb();
            var id = OldApplication(db, "ZZ2B-OPEN", "APPLICATION_SUBMITTED");
            var edit = App(db, "Old", email: "old.parent@example.com", whatsapp: "9876543210", same: false);
            var edited = await Admissions(db).UpdateAsync(A, id, edit, default);
            Assert.True(edited.Status, edited.Message);
            Assert.Equal("+919876543210", db.Scalar<string>($"select guardian_whatsapp_number from tb_admissions where admission_id={id}"));

            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var users = db.Scalar<long>("select count(*) from tb_users");
            var confirmed = await Admissions(db).ConfirmAdmissionAsync(A, id, db.Scalar<long>("select min(user_id) from tb_users"), new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.True(confirmed.Status, confirmed.Message);
            var student = db.Scalar<string>($"select student_id from tb_admissions where admission_id={id}");
            Assert.Equal("+919876543210", db.Scalar<string>($"select g.whatsapp_number from tb_student_guardians sg join tb_guardians g on g.guardian_id=sg.guardian_id where sg.student_id='{student}' and sg.is_primary"));
            Assert.Equal("old.parent@example.com", db.Scalar<string>($"select g.email from tb_student_guardians sg join tb_guardians g on g.guardian_id=sg.guardian_id where sg.student_id='{student}' and sg.is_primary"));
            Assert.Equal(users, db.Scalar<long>("select count(*) from tb_users"));      // confirming creates no parent login (and no login of any kind)
        }

        [DbFact]
        public async Task Confirming_with_only_one_of_the_two_missing_names_which()
        {
            using var db = new TaxDb();
            var id = OldApplication(db, "ZZ2B-HALF");
            db.Exec($"update tb_admissions set guardian_email='half@example.com' where admission_id={id}");
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var r = await Admissions(db).ConfirmAdmissionAsync(A, id, db.Scalar<long>("select min(user_id) from tb_users"), new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.Equal(409, r.StatusCode);
            Assert.Contains("WhatsApp number is missing", r.Message);
            Assert.DoesNotContain("email", r.Message);
        }

        // ------------------------------------------------------------------------------------------------ Add Existing Student
        private static DirectStudentEntryRequestDto Direct(long section, string first, string? email = "g@example.com", string? wa = null, bool same = true) => new()
        {
            AdmissionDate = DateTime.UtcNow.Date, AcademicYearId = Y, ClassSectionId = section, FirstName = first, DateOfBirth = new DateTime(2018, 5, 5), GuardianName = "G", GuardianRelationship = "FATHER",
            GuardianConsent = true, GuardianMobileNumber = "9876543210", GuardianEmail = email, GuardianWhatsAppNumber = wa, GuardianWhatsAppSameAsMobile = same,
        };

        [DbFact]
        public async Task Direct_entry_stores_the_guardians_whatsapp_and_email_and_refuses_when_either_is_missing()
        {
            using var db = new TaxDb();
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            var svc = new StudentOnboardingService(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<StudentOnboardingService>.Instance);
            var ok = await svc.CreateDirectEntryAsync(A, Direct(section, "Wa1", wa: "+44 7911 123456", same: false), default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("+447911123456", db.Scalar<string>($"select g.whatsapp_number from tb_student_guardians sg join tb_guardians g on g.guardian_id=sg.guardian_id where sg.student_id='{ok.Data!.StudentId}'"));
            var same = await svc.CreateDirectEntryAsync(A, Direct(section, "Wa2"), default);
            Assert.Equal("+919876543210", db.Scalar<string>($"select g.whatsapp_number from tb_student_guardians sg join tb_guardians g on g.guardian_id=sg.guardian_id where sg.student_id='{same.Data!.StudentId}'"));

            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            Assert.Equal(400, (await svc.CreateDirectEntryAsync(A, Direct(section, "NoWa", same: false), default)).StatusCode);
            Assert.Equal(400, (await svc.CreateDirectEntryAsync(A, Direct(section, "NoMail", email: null), default)).StatusCode);
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
        }

        // ------------------------------------------------------------------------------------------------ Excel import
        private static byte[] Sheet(bool withGuardianColumns, params string[][] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Students");
            var headers = new List<string> { "Admission number", "Full name", "Gender", "Date of birth", "Class", "Section", "Roll number", "Father name", "Father phone" };
            if (withGuardianColumns) headers.AddRange(new[] { "Primary guardian WhatsApp", "Primary guardian email", "Parent consent (Yes/No)" });
            for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++) { for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c]; if (withGuardianColumns) ws.Cell(r + 2, 12).Value = "Yes"; }
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }

        private static (string cls, string sec) SectionNames(TaxDb db)
        {
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            return (db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={section}"), db.Scalar<string>($"select section_name from tb_sections where section_id={section}"));
        }

        [DbFact]
        public async Task The_import_needs_the_primary_guardians_whatsapp_and_email_per_row_valid_rows_still_import_and_the_same_email_may_repeat()
        {
            using var db = new TaxDb();
            var (cls, sec) = SectionNames(db);
            string[] Row(string name, string phone, string wa, string email) => new[] { "", name, "Male", "05/05/2018", cls, sec, "", "Father", phone, wa, email };
            var file = Sheet(true,
                Row("Ok Same", "9876500001", "SAME", "family@example.com"),
                Row("Ok Typed", "9876500002", "+91 98765 00099", "family@example.com"),      // the same email on another row: allowed
                Row("No Whatsapp", "9876500003", "", "x@example.com"),
                Row("Bad Whatsapp", "9876500004", "12345", "x@example.com"),
                Row("No Email", "9876500005", "SAME", ""),
                Row("Bad Email", "9876500006", "SAME", "not-an-email"));
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var preview = (await svc.PreviewAsync(A, file, default)).Data!;
            Assert.Equal(new[] { "OK", "OK", "ERROR", "ERROR", "ERROR", "ERROR" }, preview.Rows.Select(r => r.Status));
            Assert.Contains(preview.Rows[2].Messages, m => m.Contains("WhatsApp is required"));
            Assert.Contains(preview.Rows[3].Messages, m => m.Contains("is not valid"));
            Assert.Contains(preview.Rows[4].Messages, m => m.Contains("email is required"));
            Assert.Contains(preview.Rows[5].Messages, m => m.Contains("not a valid email"));
            Assert.Equal("+919876500001", preview.Rows[0].GuardianWhatsApp);
            Assert.Equal(2, preview.Summary.Importable); Assert.Equal(4, preview.Summary.Errors); Assert.True(preview.Summary.CanImport);

            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var committed = await svc.CommitAsync(A, file, default);
            Assert.True(committed.Status, committed.Message);
            Assert.Equal(2, committed.Data!.Imported); Assert.Equal(4, committed.Data.SkippedRows);
            Assert.Equal(new[] { 4, 5, 6, 7 }, committed.Data.SkippedRowNumbers);
            Assert.Equal(students + 2, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal("+919876500001", db.Scalar<string>($"select g.whatsapp_number from tb_guardians g where g.school_id='{A}' and g.mobile_number='9876500001'"));
            Assert.Equal("+919876500099", db.Scalar<string>($"select g.whatsapp_number from tb_guardians g where g.school_id='{A}' and g.mobile_number='9876500002'"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_guardians where school_id='{A}' and email='family@example.com'"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_students where school_id='{A}' and first_name in ('No','Bad')"));
        }

        [DbFact]
        public async Task A_sheet_from_the_old_template_without_the_two_columns_gets_a_row_error_on_every_row_and_imports_nothing()
        {
            using var db = new TaxDb();
            var (cls, sec) = SectionNames(db);
            var file = Sheet(false, new[] { "", "Old Template", "Male", "05/05/2018", cls, sec, "", "Father", "9876500010" });
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var preview = (await svc.PreviewAsync(A, file, default)).Data!;
            Assert.Equal("ERROR", preview.Rows.Single().Status);
            Assert.False(preview.Summary.CanImport);
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            Assert.False((await svc.CommitAsync(A, file, default)).Status);
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
        }

        [DbFact]
        public async Task The_generated_template_has_the_two_new_columns_and_the_instructions_say_so()
        {
            using var db = new TaxDb();
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var template = await svc.TemplateAsync(A, default);
            Assert.NotNull(template.Content);
            using var wb = new XLWorkbook(new MemoryStream(template.Content!));
            var headers = wb.Worksheet("Students").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains("Primary guardian WhatsApp", headers); Assert.Contains("Primary guardian email", headers);
            var text = string.Join(" ", wb.Worksheets.SelectMany(w => w.CellsUsed()).Select(c => c.GetString()));
            Assert.Contains("SAME", text);
            Assert.DoesNotContain("a single error stops the whole import", text);
        }

        // ------------------------------------------------------------------------------------------------ the database checks
        [DbFact]
        public void The_database_refuses_a_whatsapp_number_without_a_country_code()
        {
            using var db = new TaxDb();
            Assert.Throws<PostgresException>(() => db.Exec($"update tb_schools set whatsapp_number='9876543210' where school_id='{A}'"));
            Assert.Throws<PostgresException>(() => db.Exec("update tb_guardians set whatsapp_number='9876543210' where guardian_id=(select min(guardian_id) from tb_guardians)"));
            Assert.Throws<PostgresException>(() => db.Exec("update tb_admissions set guardian_whatsapp_number='+0123' where admission_id=(select min(admission_id) from tb_admissions)"));
            db.Exec($"update tb_schools set whatsapp_number='+919876543210' where school_id='{A}'");
            Assert.Throws<PostgresException>(() => db.Exec($"update tb_schools set latitude=95, longitude=10 where school_id='{A}'"));
            Assert.Throws<PostgresException>(() => db.Exec($"update tb_schools set latitude=10 where school_id='{A}'"));             // one without the other
            db.Exec($"update tb_schools set latitude=28.613900, longitude=77.209000 where school_id='{A}'");
        }

        // ------------------------------------------------------------------------------------------------ the school
        private static SchoolRegistrationService Schools(TaxDb db) =>
            new(new SchoolRegistrationRepository(db.Ctx()), Fake.Of<IFileStorageService>(), Fake.Of<IEmailService>(), new ConfigurationBuilder().Build(),
                new PartnerSchoolCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<SchoolRegistrationService>.Instance);

        private static CreateSchoolRequest NewSchool(string code, string? wa = "+91 98765 00001", bool same = false, decimal? lat = null, decimal? lon = null) => new()
        {
            SchoolCode = code, SchoolName = "WA test " + code, MobileNumber = "9876500002", WhatsAppNumber = wa, WhatsAppSameAsMobile = same, Latitude = lat, Longitude = lon,
            Contacts = new() { new SchoolContactRequest { ContactType = "Principal", ContactName = "P", MobileNumber = "9876500003", Email = "p@example.com", IsPrimary = true } },
        };

        [Fact]
        public void The_school_requests_refuse_a_missing_whatsapp_number_and_a_half_or_out_of_range_location()
        {
            Assert.Contains(Validate(NewSchool("W1", wa: null)), r => r.ErrorMessage!.Contains("WhatsApp number is required"));
            Assert.Empty(Validate(NewSchool("W1", wa: null, same: true)));                                           // the tick uses the mobile number
            Assert.Contains(Validate(NewSchool("W1", wa: "123")), r => r.ErrorMessage!.Contains("not valid"));
            Assert.Contains(Validate(NewSchool("W1", lat: 12.5m)), r => r.ErrorMessage!.Contains("together"));
            Assert.Contains(Validate(NewSchool("W1", lat: 95m, lon: 10m)), r => r.ErrorMessage!.Contains("Latitude must be"));
            Assert.Contains(Validate(NewSchool("W1", lat: 10m, lon: 181m)), r => r.ErrorMessage!.Contains("Longitude must be"));
            Assert.Empty(Validate(NewSchool("W1", lat: 28.6139m, lon: 77.209m)));
            var mine = new UpdateMySchoolProfileRequest { SchoolName = "S", MobileNumber = "9876500002" };
            Assert.Contains(Validate(mine), r => r.ErrorMessage!.Contains("WhatsApp number is required"));          // mandatory on every edit
            mine.WhatsAppSameAsMobile = true;
            Assert.Empty(Validate(mine));
        }

        [DbFact]
        public async Task A_registered_school_stores_its_whatsapp_number_with_the_country_code_and_the_super_admin_can_set_and_change_its_location()
        {
            using var db = new TaxDb();
            var svc = Schools(db);
            var reg = await svc.RegisterSchoolAsync(NewSchool("W2B1", lat: 28.6139m, lon: 77.209m), default);
            Assert.True(reg.Status, reg.Message);
            var id = db.Scalar<string>("select school_id from tb_schools where school_code='W2B1'");
            Assert.Equal("+919876500001", db.Scalar<string>($"select whatsapp_number from tb_schools where school_id='{id}'"));
            Assert.Equal(28.6139m, db.Scalar<decimal>($"select latitude from tb_schools where school_id='{id}'"));
            var shown = (SchoolResponse)(await svc.GetMySchoolAsync(id, true)).Data!;
            Assert.Equal("+919876500001", shown.WhatsAppNumber); Assert.Equal(77.209m, shown.Longitude);

            var update = new UpdateSchoolRequest { SchoolName = "WA test W2B1", MobileNumber = "9876500002", WhatsAppSameAsMobile = true, Latitude = 19.076m, Longitude = 72.8777m };
            var changed = await svc.UpdateSchoolAsync(id, update);
            Assert.True(changed.Status, changed.Message);
            Assert.Equal("+919876500002", db.Scalar<string>($"select whatsapp_number from tb_schools where school_id='{id}'"));
            Assert.Equal(19.076m, db.Scalar<decimal>($"select latitude from tb_schools where school_id='{id}'"));
        }

        [DbFact]
        public async Task A_school_admins_own_edit_changes_the_whatsapp_number_but_never_the_location_even_when_the_body_carries_it()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_schools set latitude=28.613900, longitude=77.209000, whatsapp_number='+919876500001' where school_id='{A}'");
            var svc = Schools(db);
            // the request as it arrives: JSON. A body with latitude / longitude is read WITHOUT them (the request type has no such members)
            var req = JsonSerializer.Deserialize<UpdateMySchoolProfileRequest>(
                "{\"schoolName\":\"Green Hills School\",\"mobileNumber\":\"9876500201\",\"whatsAppNumber\":\"9000000000\",\"latitude\":1.5,\"longitude\":2.5}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Empty(Validate(req));
            Assert.Null(typeof(UpdateMySchoolProfileRequest).GetProperty("Latitude"));
            Assert.Null(typeof(UpdateMySchoolProfileRequest).GetProperty("Longitude"));
            var r = await svc.UpdateMySchoolAsync(A, req);
            Assert.True(r.Status, r.Message);
            Assert.Equal("+919000000000", db.Scalar<string>($"select whatsapp_number from tb_schools where school_id='{A}'"));
            Assert.Equal(28.6139m, db.Scalar<decimal>($"select latitude from tb_schools where school_id='{A}'"));      // unchanged
            Assert.Equal(77.209m, db.Scalar<decimal>($"select longitude from tb_schools where school_id='{A}'"));
        }

        [DbFact]
        public async Task A_school_without_a_whatsapp_number_is_only_prompted_the_dashboard_still_loads_and_the_prompt_goes_once_it_is_added()
        {
            using var db = new TaxDb();
            Assert.Null(db.Scalar<string>($"select whatsapp_number from tb_schools where school_id='{A}'"));         // existing schools have none
            var c = db.Ctx();
            var dash = new SchoolAdminDashboardService(new SchoolAdminDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<SchoolAdminDashboardService>.Instance);
            var first = await dash.GetAsync(A, default);
            Assert.True(first.Status, first.Message);                         // never a block
            Assert.True(first.Data!.WhatsAppMissing);
            var mySchool = await Schools(db).GetMySchoolAsync(A, true);
            Assert.True(mySchool.Status);                                      // the profile loads too, with no number
            Assert.Null(((SchoolResponse)mySchool.Data!).WhatsAppNumber);

            db.Exec($"update tb_schools set whatsapp_number='+919876500001' where school_id='{A}'");
            var c2 = db.Ctx();
            var after = await new SchoolAdminDashboardService(new SchoolAdminDashboardRepository(c2), new SchoolDashboardRepository(c2), NullLogger<SchoolAdminDashboardService>.Instance).GetAsync(A, default);
            Assert.False(after.Data!.WhatsAppMissing);
        }

        [Fact]
        public void The_public_school_profile_never_carries_the_whatsapp_number_or_the_location()
        {
            var publicProps = typeof(SchoolPublicProfileResponse).GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain("WhatsAppNumber", publicProps);
            Assert.DoesNotContain("Latitude", publicProps);
            Assert.DoesNotContain("Longitude", publicProps);
        }
    }
}
