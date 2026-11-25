using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 2d (Docs/smss-st-2d-RUN-ME.sql): student documents. A school marks document types required; the requirement applies to students admitted on or after the day it was switched on
    /// (required_from). Per student and required type the status is SUBMITTED, WAIVED or PENDING, and PENDING is derived (never stored). A waiver needs a reason and records who. Voiding hides, nothing is
    /// deleted. Files are PDF or image up to 5 MB, signature-checked, in the private store, served only through an authorised endpoint. Documents never block anything.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the 2d script applied.
    /// </summary>
    public class StudentDocuments2dTests : IDisposable
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;
        private const long UserB = TaxDb.UserB;
        private const string Student = "GHS001ST001";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "smss2d-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { } }

        private LocalFileStorageService Storage() => new(Path.Combine(_root, "uploads"), "/uploads", Path.Combine(_root, "private"), NullLogger<LocalFileStorageService>.Instance);
        private StudentDocumentService Docs(TaxDb db) => new(new StudentDocumentRepository(db.Ctx()), Storage(), NullLogger<StudentDocumentService>.Instance);

        private static MemoryStream Pdf(int extra = 100) => new(Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string('x', extra)));
        private static MemoryStream Png() => new(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8 });
        private static readonly StudentDocumentCaller Manager = new(UserA, true, false);

        /// <summary>A required student document type for the school, switched on <paramref name="daysAgo"/> days ago.</summary>
        private static long RequiredType(TaxDb db, string school, string code, int daysAgo = 0, string appliesTo = "STUDENT", bool required = true, string name = "")
        {
            var from = required ? $"(current_date - {daysAgo})" : "null";
            return db.Scalar<long>($@"insert into tb_document_types (school_id, document_name, document_code, applies_to, is_required, required_from, status_id)
                values ('{school}', '{(name.Length > 0 ? name : code)}', '{code}', '{appliesTo}', {required.ToString().ToLower()}, {from}, (select sid from lut_status where sname='Active' and stype='general status')) returning document_type_id");
        }

        private static void AdmittedOn(TaxDb db, string student, string sqlDate) => db.Exec($"update tb_students set admission_date={sqlDate} where student_id='{student}'");

        // ------------------------------------------------------------------------------------------------ file validation
        [DbFact]
        public async Task Only_a_real_pdf_or_image_within_5_MB_is_accepted_and_it_lands_in_the_private_store_never_a_public_url()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T1");
            var svc = Docs(db);

            var exe = await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "virus.exe", 100, default);
            Assert.Equal(400, exe.StatusCode); Assert.Contains("Only PDF, PNG, JPG or WEBP", exe.Message);
            var fake = await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "photo.png", 100, default);   // a PDF pretending to be a PNG
            Assert.Equal(400, fake.StatusCode); Assert.Contains("not a valid", fake.Message);
            var empty = await svc.UploadAsync(A, Student, type, null, UserA, new MemoryStream(), "a.pdf", 0, default);
            Assert.Equal(400, empty.StatusCode);
            var big = await svc.UploadAsync(A, Student, type, null, UserA, new MemoryStream(new byte[10]), "big.pdf", DocumentRules.MaxBytes + 1, default);
            Assert.Equal(400, big.StatusCode); Assert.Contains("5 MB", big.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_documents where student_id='{Student}'"));

            var ok = await svc.UploadAsync(A, Student, type, "  scan  ", UserA, Pdf(), "../../Birth Certificate.pdf", 109, default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("SUBMITTED", ok.Data!.Status);
            Assert.Equal("Birth Certificate.pdf", ok.Data.FileName);   // only the file name part is kept
            var key = db.Scalar<string>($"select document_path from tb_student_documents where student_document_id={ok.Data.StudentDocumentId}");
            Assert.StartsWith($"{db.Scalar<string>($"select school_code from tb_schools where school_id='{A}'")}/students/GHS001ST001/documents/doc_", key);
            Assert.DoesNotContain("http", key); Assert.DoesNotContain("uploads", key);
            Assert.True(File.Exists(Path.Combine(_root, "private", key.Replace('/', Path.DirectorySeparatorChar))));   // the private root, not the public uploads folder
            Assert.False(Directory.Exists(Path.Combine(_root, "uploads")) && Directory.GetDirectories(Path.Combine(_root, "uploads"), "students", SearchOption.AllDirectories).Length > 0);
            Assert.True((await Docs(db).UploadAsync(A, Student, type, null, UserA, Png(), "a.png", 16, default)).Status);   // an image too (replaces the first)
        }

        // ------------------------------------------------------------------------------------------------ derived status, never stored
        [DbFact]
        public async Task A_required_type_is_pending_until_a_file_or_a_waiver_exists_and_pending_is_never_stored()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T2", daysAgo: 30);
            AdmittedOn(db, Student, "current_date - 5");   // admitted after the requirement started
            var svc = Docs(db);

            var first = (await svc.GetAsync(A, Student, Manager, default)).Data!;
            Assert.Equal("PENDING", first.Required.Single().Status);
            Assert.Equal(1, first.PendingCount);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_documents"));   // nothing stored for a pending document

            var up = await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "a.pdf", 109, default);
            Assert.Equal("SUBMITTED", (await svc.GetAsync(A, Student, Manager, default)).Data!.Required.Single().Status);

            Assert.True((await svc.VoidAsync(A, Student, up.Data!.StudentDocumentId, "wrong file", UserA, default)).Status);
            var afterVoid = (await svc.GetAsync(A, Student, Manager, default)).Data!;
            Assert.Equal("PENDING", afterVoid.Required.Single().Status);   // pending again
            Assert.Empty(afterVoid.Documents);                              // the voided row is hidden, not deleted
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_student_documents where voided_at is not null"));
            Assert.True(File.Exists(Path.Combine(_root, "private", db.Scalar<string>($"select document_path from tb_student_documents where student_document_id={up.Data.StudentDocumentId}").Replace('/', Path.DirectorySeparatorChar))));   // the file is kept

            Assert.True((await svc.WaiveAsync(A, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = type, Reason = "Original lost in a fire" }, UserA, default)).Status);
            Assert.Equal("WAIVED", (await svc.GetAsync(A, Student, Manager, default)).Data!.Required.Single().Status);
            Assert.Equal(0, (await svc.GetAsync(A, Student, Manager, default)).Data!.PendingCount);
        }

        [DbFact]
        public async Task A_new_upload_replaces_the_current_document_and_there_is_one_active_row_per_student_and_type()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T3");
            var svc = Docs(db);
            var one = (await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "one.pdf", 109, default)).Data!;
            var two = (await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "two.pdf", 109, default)).Data!;
            Assert.NotEqual(one.StudentDocumentId, two.StudentDocumentId);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_documents where student_id='{Student}' and document_type_id={type} and voided_at is null"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_documents where student_id='{Student}' and document_type_id={type}"));   // the first one is voided, not deleted
            // and the database itself refuses a second active row
            var ex = Assert.ThrowsAny<Exception>(() => db.Exec($@"insert into tb_student_documents (school_id, student_id, document_type_id, status, document_path, original_file_name, content_type, file_size)
                values ('{A}', '{Student}', {type}, 'SUBMITTED', 'k', 'f.pdf', 'application/pdf', 5)"));
            Assert.Contains("uq_tb_student_documents_active", ex.ToString());
        }

        // ------------------------------------------------------------------------------------------------ waivers
        [DbFact]
        public async Task A_waiver_needs_a_reason_records_who_and_when_and_cannot_be_stacked_on_a_file_or_another_waiver()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T4");
            var svc = Docs(db);
            foreach (var bad in new string?[] { null, "", "   " })
            {
                var r = await svc.WaiveAsync(A, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = type, Reason = bad }, UserA, default);
                Assert.Equal(400, r.StatusCode); Assert.Contains("reason is required", r.Message);
            }
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_documents"));

            var ok = await svc.WaiveAsync(A, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = type, Reason = "  Court order  " }, UserA, default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("WAIVED", ok.Data!.Status);
            Assert.Equal("Court order", ok.Data.WaiverReason);
            Assert.Equal(UserA, db.Scalar<long>($"select waived_by from tb_student_documents where student_document_id={ok.Data.StudentDocumentId}"));
            Assert.True(db.Scalar<bool>($"select waived_at is not null from tb_student_documents where student_document_id={ok.Data.StudentDocumentId}"));
            Assert.Equal(409, (await svc.WaiveAsync(A, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = type, Reason = "again" }, UserA, default)).StatusCode);

            var other = RequiredType(db, A, "T4B");
            Assert.True((await svc.UploadAsync(A, Student, other, null, UserA, Pdf(), "a.pdf", 109, default)).Status);
            var overFile = await svc.WaiveAsync(A, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = other, Reason = "x" }, UserA, default);
            Assert.Equal(409, overFile.StatusCode); Assert.Contains("Void it first", overFile.Message);
            // a later upload replaces a waiver
            Assert.True((await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "a.pdf", 109, default)).Status);
            Assert.Equal("SUBMITTED", (await svc.GetAsync(A, Student, Manager, default)).Data!.Documents.Single(d => d.DocumentTypeId == type).Status);
        }

        [DbFact]
        public void The_database_refuses_a_submitted_row_without_a_file_and_a_waived_row_without_a_reason()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T5");
            var noFile = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_student_documents (school_id, student_id, document_type_id, status) values ('{A}', '{Student}', {type}, 'SUBMITTED')"));
            Assert.Contains("chk_tb_student_documents_submitted", noFile.ToString());
            var noReason = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_student_documents (school_id, student_id, document_type_id, status, waiver_reason, waived_by, waived_at) values ('{A}', '{Student}', {type}, 'WAIVED', '  ', {UserA}, now())"));
            Assert.Contains("chk_tb_student_documents_waived", noReason.ToString());
            var overdue = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_student_documents (school_id, student_id, document_type_id, status) values ('{A}', '{Student}', {type}, 'OVERDUE')"));
            Assert.Contains("chk_tb_student_documents_status", overdue.ToString());   // only SUBMITTED and WAIVED are ever stored
        }

        // ------------------------------------------------------------------------------------------------ required_from
        [Fact]
        public void Required_from_is_set_when_switched_on_kept_while_on_and_cleared_when_off()
        {
            var today = new DateOnly(2026, 11, 25);
            var set = RequiredFromRules.Resolve(true, null, today);
            Assert.Equal(new DateTime(2026, 11, 25), set);
            Assert.Equal(new DateTime(2026, 10, 1), RequiredFromRules.Resolve(true, new DateTime(2026, 10, 1), today));   // editing a name does not move it
            Assert.Null(RequiredFromRules.Resolve(false, new DateTime(2026, 10, 1), today));
            Assert.Equal(today.ToDateTime(TimeOnly.MinValue), RequiredFromRules.Resolve(true, null, today));                // switched on again: starts again today
        }

        [DbFact]
        public async Task The_document_types_screen_sets_required_from_when_a_type_is_switched_on_and_clears_it_when_off()
        {
            using var db = new TaxDb();
            DocumentTypeService Svc() => new(new DocumentTypeRepository(db.Ctx()), NullLogger<DocumentTypeService>.Instance);   // a fresh context each time: the SQL edits below must not meet a stale tracked row
            var svc = Svc();
            var created = await svc.CreateAsync(A, new CreateDocumentTypeRequestDto { DocumentName = "Birth cert", DocumentCode = "BC2D", AppliesTo = "STUDENT", IsRequired = true }, default);
            Assert.True(created.Status, created.Message);
            Assert.Equal(SchoolClock.Today().ToDateTime(TimeOnly.MinValue), created.Data!.RequiredFrom);

            db.Exec($"update tb_document_types set required_from = current_date - 10 where document_type_id={created.Data.DocumentTypeId}");
            var renamed = await Svc().UpdateAsync(A, new UpdateDocumentTypeRequestDto { DocumentTypeId = created.Data.DocumentTypeId, DocumentName = "Birth certificate", DocumentCode = "BC2D", AppliesTo = "STUDENT", IsRequired = true }, default);
            Assert.Equal(SchoolClock.Today().AddDays(-10).ToDateTime(TimeOnly.MinValue), renamed.Data!.RequiredFrom);   // still required: the start date stays

            var off = await Svc().UpdateAsync(A, new UpdateDocumentTypeRequestDto { DocumentTypeId = created.Data.DocumentTypeId, DocumentName = "Birth certificate", DocumentCode = "BC2D", AppliesTo = "STUDENT", IsRequired = false }, default);
            Assert.Null(off.Data!.RequiredFrom);
            var on = await Svc().UpdateAsync(A, new UpdateDocumentTypeRequestDto { DocumentTypeId = created.Data.DocumentTypeId, DocumentName = "Birth certificate", DocumentCode = "BC2D", AppliesTo = "STUDENT", IsRequired = true }, default);
            Assert.Equal(SchoolClock.Today().ToDateTime(TimeOnly.MinValue), on.Data!.RequiredFrom);   // switched on again: the requirement starts again today
        }

        [DbFact]
        public void The_database_keeps_required_from_in_step_with_is_required()
        {
            using var db = new TaxDb();
            var a = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_document_types (school_id, document_name, document_code, applies_to, is_required, status_id) values ('{A}', 'x', 'CHK1', 'STUDENT', true, 1)"));
            Assert.Contains("chk_tb_document_types_required_from", a.ToString());
            var b = Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_document_types (school_id, document_name, document_code, applies_to, is_required, required_from, status_id) values ('{A}', 'x', 'CHK2', 'STUDENT', false, current_date, 1)"));
            Assert.Contains("chk_tb_document_types_required_from", b.ToString());
        }

        [DbFact]
        public async Task The_requirement_applies_only_to_students_admitted_on_or_after_the_required_from_date()
        {
            using var db = new TaxDb();
            RequiredType(db, A, "T6", daysAgo: 10);   // required since 10 days ago
            AdmittedOn(db, "GHS001ST001", "current_date - 10");   // admitted ON the day: flagged
            AdmittedOn(db, "GHS001ST002", "current_date - 11");   // admitted the day before: NOT flagged
            AdmittedOn(db, "GHS001ST003", "current_date");        // admitted after: flagged
            var svc = Docs(db);
            Assert.Equal(1, (await svc.GetAsync(A, "GHS001ST001", Manager, default)).Data!.Required.Count);
            Assert.Empty((await svc.GetAsync(A, "GHS001ST002", Manager, default)).Data!.Required);
            Assert.Equal(1, (await svc.GetAsync(A, "GHS001ST003", Manager, default)).Data!.PendingCount);

            var pending = (await svc.ListPendingAsync(A, null, null, null, default)).Data!;
            var ids = pending.Students.Select(s => s.StudentId).ToList();
            Assert.Contains("GHS001ST001", ids); Assert.Contains("GHS001ST003", ids); Assert.DoesNotContain("GHS001ST002", ids);
            Assert.Equal(pending.TotalStudents, (await svc.CountPendingAsync(A, default)).Data);
            Assert.All(pending.Students.Where(s => s.StudentId == "GHS001ST001"), s => Assert.Equal(new[] { "T6" }, s.Missing));

            // switched off, an EMPLOYEE-only type, an inactive type and another school's type never count
            db.Exec($"update tb_document_types set is_required=false, required_from=null where school_id='{A}' and document_code='T6'");
            RequiredType(db, A, "T7", appliesTo: "EMPLOYEE");
            db.Exec($"update tb_document_types set status_id=(select sid from lut_status where sname='Inactive' and stype='general status') where school_id='{A}' and document_code='T7'");
            RequiredType(db, B, "T8");
            Assert.Equal(0, (await svc.CountPendingAsync(A, default)).Data);
        }

        // ------------------------------------------------------------------------------------------------ other schools
        [DbFact]
        public async Task Another_schools_user_reaches_nothing_of_this_schools_students_documents_or_types()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T9");
            var uploaded = (await Docs(db).UploadAsync(A, Student, type, null, UserA, Pdf(), "a.pdf", 109, default)).Data!;
            var svc = Docs(db);
            var asB = new StudentDocumentCaller(UserB, true, false);

            Assert.Equal(404, (await svc.GetAsync(B, Student, asB, default)).StatusCode);
            Assert.Equal(404, (await svc.UploadAsync(B, Student, type, null, UserB, Pdf(), "a.pdf", 109, default)).StatusCode);
            Assert.Equal(404, (await svc.WaiveAsync(B, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = type, Reason = "x" }, UserB, default)).StatusCode);
            Assert.Equal(404, (await svc.VoidAsync(B, Student, uploaded.StudentDocumentId, null, UserB, default)).StatusCode);
            Assert.Equal(404, (await svc.OpenAsync(B, Student, uploaded.StudentDocumentId, default)).Failure!.StatusCode);
            Assert.DoesNotContain(Student, (await svc.ListPendingAsync(B, null, null, null, default)).Data!.Students.Select(s => s.StudentId));
            // a school's own student with another school's document type
            var typeB = RequiredType(db, B, "T9B");
            var wrongType = await svc.UploadAsync(A, Student, typeB, null, UserA, Pdf(), "a.pdf", 109, default);
            Assert.Equal(400, wrongType.StatusCode);
            // the document of this student cannot be fetched through another student's id either
            Assert.Equal(404, (await svc.OpenAsync(A, "GHS001ST002", uploaded.StudentDocumentId, default)).Failure!.StatusCode);
            Assert.Equal(404, (await svc.VoidAsync(A, "GHS001ST002", uploaded.StudentDocumentId, null, UserA, default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_documents where school_id='{B}'"));
        }

        [DbFact]
        public async Task A_download_gives_the_file_only_for_a_submitted_not_voided_row()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T10");
            var svc = Docs(db);
            var sub = (await svc.UploadAsync(A, Student, type, null, UserA, Pdf(), "scan.pdf", 109, default)).Data!;
            var (fail, content, contentType, name) = await svc.OpenAsync(A, Student, sub.StudentDocumentId, default);
            Assert.Null(fail); Assert.Equal("application/pdf", contentType); Assert.Equal("scan.pdf", name);
            using (content) Assert.StartsWith("%PDF-", new StreamReader(content!).ReadToEnd());
            await svc.VoidAsync(A, Student, sub.StudentDocumentId, null, UserA, default);
            Assert.Equal(404, (await svc.OpenAsync(A, Student, sub.StudentDocumentId, default)).Failure!.StatusCode);
            var waived = (await svc.WaiveAsync(A, Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = type, Reason = "x" }, UserA, default)).Data!;
            Assert.Equal(404, (await svc.OpenAsync(A, Student, waived.StudentDocumentId, default)).Failure!.StatusCode);   // a waiver has no file
            Assert.Equal(409, (await svc.VoidAsync(A, Student, sub.StudentDocumentId, null, UserA, default)).StatusCode);   // already voided
        }

        // ------------------------------------------------------------------------------------------------ who may do what
        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_three_manage_roles_may_upload_waive_void_download_and_list_and_a_Teacher_may_read_statuses_nobody_else(string role)
        {
            var manage = new[] { "School Admin", "Principal", "Admission Staff" }.Contains(role);
            var teacher = role == "Teacher";
            var svc = Fake.Of<IStudentDocumentService>(new() { ["OpenAsync"] = (new ApiResponse<StudentDocumentDto> { Status = false, StatusCode = 404, Message = "stand-in" }, (Stream?)null, (string?)null, (string?)null) });
            var c = Fake.WithUser(new StudentDocumentsController(svc), Fake.User(A, 5, role));
            var file = new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "file", "a.pdf");
            var writes = new (string Name, Func<Task<IActionResult>> Run)[]
            {
                ("UploadAsync", () => c.Upload(Student, file, 1, null, default)),
                ("WaiveAsync", () => c.Waive(Student, new WaiveStudentDocumentRequestDto { DocumentTypeId = 1, Reason = "x" }, default)),
                ("VoidAsync", () => c.Void(Student, 1, new VoidStudentDocumentRequestDto(), default)),
                ("ListPendingAsync", () => c.Pending(null, null, null, default)),
                ("CountPendingAsync", () => c.PendingCount(default)),
            };
            foreach (var (name, run) in writes)
            {
                var code = Fake.Code(await run());
                if (manage) Assert.NotEqual(403, code); else Assert.Equal(403, code);
                Assert.Equal(manage, Fake.ProxyOf(svc).Calls.Contains(name));
            }
            // the download: only the manage roles (a Teacher never gets a file; the fake has no file, so the manager gets a 404/500 from the stand-in, never 403)
            var download = Fake.Code(await c.Download(Student, 1, default));
            if (manage) Assert.NotEqual(403, download); else Assert.Equal(403, download);
            // the status list: the manage roles and a Teacher reach the service (which scopes a Teacher to their own section); everyone else is refused
            var read = Fake.Code(await c.Get(Student, default));
            if (manage || teacher) Assert.NotEqual(403, read); else Assert.Equal(403, read);
            Assert.Equal(manage || teacher, Fake.ProxyOf(svc).Calls.Contains("GetAsync"));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [DbFact]
        public async Task A_Teacher_reads_statuses_only_for_a_student_in_the_section_they_are_class_teacher_of_and_never_gets_file_details()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T11");
            AdmittedOn(db, Student, "current_date");
            var up = (await Docs(db).UploadAsync(A, Student, type, "private note", UserA, Pdf(), "secret-aadhaar.pdf", 109, default)).Data!;

            // a class-teacher assignment that covers the student's own section and year
            var emp = db.Scalar<string>($@"select a.employee_id from tb_class_teacher_assignments a join tb_student_enrollments e on e.school_id=a.school_id and e.academic_year_id=a.academic_year_id and e.class_section_id=a.section_id
                where a.school_id='{A}' and e.student_id='{Student}' and a.status_id=(select sid from lut_status where sname='Active' and stype='general status') limit 1");
            var teacherUser = db.Scalar<long>($"select user_id from tb_users where school_id='{A}' and org_user_id='{emp}'");
            var svc = Docs(db);
            var read = await svc.GetAsync(A, Student, new StudentDocumentCaller(teacherUser, false, true), default);
            Assert.True(read.Status, read.Message);
            Assert.False(read.Data!.CanManage);
            Assert.Equal("SUBMITTED", read.Data.Required.Single().Status);
            var d = read.Data.Documents.Single();
            Assert.Null(d.FileName); Assert.Null(d.Remarks); Assert.Null(d.ByName); Assert.Null(d.FileSize);   // status only

            // a teacher who is not this section's class teacher is refused
            var other = db.Scalar<long>($@"select u.user_id from tb_users u where u.school_id='{A}' and u.org_user_id like 'GHS001EM%' and u.org_user_id <> '{emp}'
                and not exists (select 1 from tb_class_teacher_assignments a join tb_student_enrollments e on e.school_id=a.school_id and e.academic_year_id=a.academic_year_id and e.class_section_id=a.section_id
                                where a.employee_id=u.org_user_id and e.student_id='{Student}') limit 1");
            Assert.Equal(403, (await svc.GetAsync(A, Student, new StudentDocumentCaller(other, false, true), default)).StatusCode);
            // an Accountant / Student / Guardian caller (neither manage nor teacher) is refused by the service too
            Assert.Equal(403, (await svc.GetAsync(A, Student, new StudentDocumentCaller(other, false, false), default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ carry-over at confirmation
        private static (long Application, long Section) AddedApplicationWithDocument(TaxDb db, long typeId, string key)
        {
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={TaxDb.YearA} and status_id=1");
            db.Exec($@"insert into tb_admissions (school_id, application_number, academic_year_id, applied_class_id, admission_type_id, applicant_first_name, date_of_birth, guardian_name, guardian_relationship,
                guardian_mobile_number, guardian_email, guardian_whatsapp_number, application_date, status_id)
                values ('{A}', 'ZZ2D-1', {TaxDb.YearA}, (select min(class_id) from tb_classes where school_id='{A}'), (select min(admission_type_id) from lut_admission_type), 'Carry', date '2018-01-01', 'G', 'FATHER',
                '9876543210', 'g@example.com', '+919876543210', current_date, (select sid from lut_status where sname='APPLICATION_SUBMITTED'))");
            var id = db.Scalar<long>("select admission_id from tb_admissions where application_number='ZZ2D-1'");
            db.Exec($@"insert into tb_admission_application_documents (school_id, admission_id, document_type_id, document_path, original_file_name, content_type, file_size, uploaded_by, status_id)
                values ('{A}', {id}, {typeId}, '{key}', 'birth.pdf', 'application/pdf', 109, {UserA}, (select sid from lut_status where sname='Active' and stype='general status'))");
            return (id, section);
        }

        private AdmissionService Admissions(TaxDb db, IStudentDocumentService? documents) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance, documents);

        /// <summary>Puts a real PDF in the private store at a known key, the way an application upload does.</summary>
        private string PutApplicationFile()
        {
            var key = "GHS-RKE-2026/admissions/1/documents/doc_seed.pdf";
            var path = Path.Combine(_root, "private", key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%PDF-1.4\nthe application's own file"));
            return key;
        }

        [DbFact]
        public async Task Confirming_an_application_carries_its_documents_to_the_student_with_a_copy_of_the_file_and_a_second_run_adds_nothing()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T12");
            var key = PutApplicationFile();
            var (id, section) = AddedApplicationWithDocument(db, type, key);
            var docs = Docs(db);
            var confirmed = await Admissions(db, docs).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.True(confirmed.Status, confirmed.Message);
            var student = confirmed.Data!.StudentId!;

            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_documents where student_id='{student}' and status='SUBMITTED' and voided_at is null and document_type_id={type}"));
            var carried = db.Scalar<string>($"select document_path from tb_student_documents where student_id='{student}'");
            Assert.NotEqual(key, carried);   // its own copy under the student's folder
            Assert.StartsWith($"{db.Scalar<string>($"select school_code from tb_schools where school_id='{A}'")}/students/{student}/documents/", carried);
            Assert.True(File.Exists(Path.Combine(_root, "private", carried.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(File.Exists(Path.Combine(_root, "private", key.Replace('/', Path.DirectorySeparatorChar))));   // the application keeps its file
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_documents where source_application_document_id is not null and student_id='{student}'"));
            Assert.Equal("birth.pdf", db.Scalar<string>($"select original_file_name from tb_student_documents where student_id='{student}'"));

            var again = await Docs(db).CarryOverFromApplicationAsync(A, id, student, UserA, default);
            Assert.Equal(0, again);   // the active row already exists / the source was already carried
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_documents where student_id='{student}'"));
        }

        [DbFact]
        public async Task A_missing_application_file_or_a_failing_carry_over_never_blocks_the_admission()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T13");
            var (id, section) = AddedApplicationWithDocument(db, type, "GHS-RKE-2026/admissions/1/documents/doc_gone.pdf");   // the file is not in the store
            var confirmed = await Admissions(db, Docs(db)).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.True(confirmed.Status, confirmed.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_student_documents where student_id='{confirmed.Data!.StudentId}'"));   // nothing carried, the student exists

            // a carry-over service that throws changes nothing about the admission either
            using var db2 = new TaxDb();
            var type2 = RequiredType(db2, A, "T13B");
            var (id2, section2) = AddedApplicationWithDocument(db2, type2, PutApplicationFile());
            var throwing = Fake.Of<IStudentDocumentService>(new() { ["CarryOverFromApplicationAsync"] = new InvalidOperationException("boom") });
            var ok = await Admissions(db2, throwing).ConfirmAdmissionAsync(A, id2, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = section2 }, default);
            Assert.True(ok.Status, ok.Message);
            Assert.Equal("ADMITTED", db2.Scalar<string>($"select s.sname from tb_admissions a join lut_status s on s.sid=a.status_id where a.admission_id={id2}"));
        }

        // ------------------------------------------------------------------------------------------------ dashboards
        [DbFact]
        public async Task The_School_Admin_dashboard_counts_students_with_pending_documents_and_says_unavailable_when_it_cannot()
        {
            using var db = new TaxDb();
            RequiredType(db, A, "T14", daysAgo: 400);   // every current student was admitted after that
            var c = db.Ctx();
            SchoolAdminDashboardService Make(IStudentDocumentService? docs) =>
                new(new SchoolAdminDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<SchoolAdminDashboardService>.Instance, docs);
            var expected = (await Docs(db).CountPendingAsync(A, default)).Data;
            Assert.True(expected > 0);
            var d = (await Make(Docs(db)).GetAsync(A, default)).Data!;
            Assert.Equal(expected, d.StudentsWithPendingDocuments);
            var down = (await Make(Fake.Of<IStudentDocumentService>()).GetAsync(A, default)).Data!;
            Assert.Null(down.StudentsWithPendingDocuments);   // never shown as 0
            Assert.NotEmpty(down.Employees.GetType().GetProperties());   // the rest of the dashboard still answers
        }

        // ------------------------------------------------------------------------------------------------ the migration
        private static string ScriptText()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Docs", "smss-st-2d-RUN-ME.sql"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!.FullName, "Docs", "smss-st-2d-RUN-ME.sql"));
        }

        [DbFact]
        public void The_script_is_safe_to_run_again_and_changes_nothing_on_the_second_run()
        {
            using var db = new TaxDb();
            var type = RequiredType(db, A, "T15", daysAgo: 3);
            db.Exec($"insert into tb_student_documents (school_id, student_id, document_type_id, status, waiver_reason, waived_by, waived_at) values ('{A}', '{Student}', {type}, 'WAIVED', 'r', {UserA}, now())");
            string Fp() => db.Scalar<string>("select md5(coalesce((select string_agg(t::text, '|' order by document_type_id) from tb_document_types t), '') || coalesce((select string_agg(d::text, '|' order by student_document_id) from tb_student_documents d), ''))");
            var before = Fp();
            var script = ScriptText();
            db.Exec(script);
            Assert.Equal(before, Fp());
            db.Exec(script);
            Assert.Equal(before, Fp());
        }
    }
}
