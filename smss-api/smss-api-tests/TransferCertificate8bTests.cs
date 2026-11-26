using System.Text.Json;
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
    /// Stage 8b: transfer certificates. [DbFact]: needs SMSS_TEST_PG and the template with the 8b migration (smss_tpl_8b).
    /// </summary>
    public class TransferCertificate8bTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private const string ActiveSql = "(select sid from lut_status where sname = 'Active' and stype = 'general status')";

        private static LeavingService Leaving(TaxDb db) => new(new LeavingRepository(db.Ctx()), NullLogger<LeavingService>.Instance);
        private static TransferCertificateService Tc(TaxDb db) => new(new TransferCertificateRepository(db.Ctx()), new LeavingRepository(db.Ctx()), NullLogger<TransferCertificateService>.Instance);

        /// <summary>A student of school A with a current-year enrolment and no unpaid dues, who has NOT left yet.</summary>
        private static string Clean(TaxDb db, int n = 0)
        {
            var id = db.Scalar<string>($@"select e.student_id from tb_student_enrollments e where e.school_id = {Q(A)} and e.academic_year_id = {TaxDb.YearA} and e.outcome is null order by e.student_id offset {n} limit 1");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where school_id = {Q(A)} and student_id = {Q(id)}");
            return id;
        }

        private static async Task<string> Left(TaxDb db, int n = 0, string type = "LEFT")
        {
            var s = Clean(db, n);
            var r = await Leaving(db).RecordAsync(A, UserA, new RecordLeavingRequest { StudentId = s, Type = type, Reason = "Family moved away", Confirm = true }, default);
            Assert.True(r.Status, r.Message);
            return s;
        }

        private static IssueTcRequest Req(string s, string? conduct = null, string? remarks = null, bool ovr = false, string? ovrReason = null, bool confirm = true) =>
            new() { StudentId = s, Conduct = conduct, Remarks = remarks, OverrideDues = ovr, OverrideReason = ovrReason, Confirm = confirm };

        private static long EnrolmentOf(TaxDb db, string student) => db.Scalar<long>($"select enrollment_id from tb_student_enrollments where school_id = {Q(A)} and student_id = {Q(student)} and academic_year_id = {TaxDb.YearA}");

        private static TcSnapshot SnapshotOf(TaxDb db, long id) => JsonSerializer.Deserialize<TcSnapshot>(db.Scalar<string>($"select snapshot::text from tb_transfer_certificates where certificate_id = {id}"))!;

        // ------------------------------------------------------------------------------------------------ who and for whom
        [DbFact]
        public async Task A_student_with_no_outcome_cannot_get_a_certificate_and_nothing_is_written()
        {
            using var db = new TaxDb(); var s = Clean(db);
            var check = (await Tc(db).CheckAsync(A, s, default)).Data!;
            Assert.False(check.CanIssue); Assert.Contains("no leaving recorded", check.Blockers[0]);
            var r = await Tc(db).IssueAsync(A, UserA, Req(s), default);
            Assert.Equal(409, r.StatusCode);
            db.Exec($"update tb_student_enrollments set outcome = 'PROMOTED', outcome_at = now() where school_id = {Q(A)} and student_id = {Q(s)}");
            Assert.Equal(409, (await Tc(db).IssueAsync(A, UserA, Req(s), default)).StatusCode);                                                   // PROMOTED is not a leaving either
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_transfer_certificates"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_transfer_certificate_sequences"));                                           // no number was taken
        }

        [DbFact]
        public async Task A_student_of_another_school_an_unknown_student_and_an_unknown_certificate_are_404()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var issued = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;
            Assert.Equal(404, (await Tc(db).CheckAsync(B, s, default)).StatusCode);
            Assert.Equal(404, (await Tc(db).IssueAsync(B, TaxDb.UserB, Req(s), default)).StatusCode);
            Assert.Equal(404, (await Tc(db).IssueAsync(A, UserA, Req("NOPE999"), default)).StatusCode);
            Assert.Equal(404, (await Tc(db).GetAsync(B, issued.CertificateId, default)).StatusCode);
            Assert.Equal(404, (await Tc(db).OriginalPdfAsync(B, TaxDb.UserB, issued.CertificateId, default)).StatusCode);
            Assert.Equal(404, (await Tc(db).DuplicatePdfAsync(B, TaxDb.UserB, issued.CertificateId, default)).StatusCode);
            Assert.Equal(404, (await Tc(db).VoidAsync(B, TaxDb.UserB, issued.CertificateId, new VoidTcRequest { Reason = "A proper long reason", Confirm = true }, default)).StatusCode);
            Assert.Empty((await Tc(db).ListAsync(B, null, null, default)).Data!);
            Assert.Equal("ISSUED", db.Scalar<string>($"select status from tb_transfer_certificates where certificate_id = {issued.CertificateId}"));
        }

        [DbFact]
        public async Task Only_a_School_Admin_or_a_Principal_may_issue_view_download_duplicate_or_void()
        {
            using var db = new TaxDb(); var s = await Left(db);
            int Code(IActionResult r) => Fake.Code(r);
            TransferCertificatesController C(params string[] roles) => Fake.WithUser(new TransferCertificatesController(Tc(db)), Fake.User(A, UserA, roles));
            foreach (var role in new[] { "Teacher", "Accountant", "Student", "Guardian", "Admission Staff", "Store Keeper" })
            {
                Assert.Equal(403, Code(await C(role).Check(s, default)));
                Assert.Equal(403, Code(await C(role).Issue(Req(s), default)));
                Assert.Equal(403, Code(await C(role).List(null, null, default)));
                Assert.Equal(403, Code(await C(role).Get(1, default)));
                Assert.Equal(403, Code(await C(role).Original(1, default)));
                Assert.Equal(403, Code(await C(role).Duplicate(1, default)));
                Assert.Equal(403, Code(await C(role).Void(1, new VoidTcRequest { Reason = "A proper long reason", Confirm = true }, default)));
            }
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_transfer_certificates"));
            Assert.Equal(200, Code(await C("School Admin").Issue(Req(s), default)));
            var id = db.Scalar<long>("select min(certificate_id) from tb_transfer_certificates");
            Assert.Equal(200, Code(await C("Principal").Get(id, default)));
            var pdf = await C("Principal").Original(id, default);
            var file = Assert.IsType<FileContentResult>(pdf); Assert.Equal("application/pdf", file.ContentType); Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
            Assert.IsType<FileContentResult>(await C("School Admin").Duplicate(id, default));
            Assert.Equal(200, Code(await C("Principal").Void(id, new VoidTcRequest { Reason = "Issued to the wrong student", Confirm = true }, default)));
        }

        // ------------------------------------------------------------------------------------------------ issuing and the number
        [DbFact]
        public async Task A_certificate_is_issued_with_the_number_school_code_TC_year_and_four_digits_the_snapshot_and_the_original_audit_row()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var r = await Tc(db).IssueAsync(A, UserA, Req(s, remarks: "Good student"), default);
            Assert.True(r.Status, r.Message);
            var code = db.Scalar<string>($"select school_code from tb_schools where school_id = {Q(A)}");
            Assert.Equal($"{code}/TC/2026-27/0001", r.Data!.CertificateNumber);
            Assert.Equal("ISSUED", r.Data.Status); Assert.Equal("Good", r.Data.Conduct); Assert.True(r.Data.FeesCleared); Assert.Equal(UserA, db.Scalar<long>($"select issued_by from tb_transfer_certificates where certificate_id = {r.Data.CertificateId}"));
            var snap = SnapshotOf(db, r.Data.CertificateId);
            Assert.Equal(r.Data.CertificateNumber, snap.CertificateNumber); Assert.Equal("Cleared", snap.FeeDues); Assert.Equal("Left", snap.Outcome); Assert.Equal("Family moved away", snap.LeavingReason);
            Assert.Equal("Good student", snap.Remarks); Assert.Equal("2026-27", snap.AcademicYear); Assert.False(string.IsNullOrWhiteSpace(snap.DateOfBirthInWords));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_transfer_certificate_copies where certificate_id = {r.Data.CertificateId} and kind = 'ORIGINAL' and copy_no = 0 and printed_by = {UserA}"));
            // one certificate per leaving
            var again = await Tc(db).IssueAsync(A, UserA, Req(s), default);
            Assert.Equal(409, again.StatusCode); Assert.Contains(r.Data.CertificateNumber, again.Message);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_transfer_certificates"));
        }

        [DbFact]
        public async Task A_passed_out_student_can_get_one_and_the_confirmation_and_the_lengths_are_checked()
        {
            using var db = new TaxDb(); var s = Clean(db);
            db.Exec($"update tb_student_enrollments set outcome = 'PASSED_OUT', outcome_at = now() where school_id = {Q(A)} and student_id = {Q(s)} and academic_year_id = {TaxDb.YearA}");
            Assert.Equal(400, (await Tc(db).IssueAsync(A, UserA, Req(s, confirm: false), default)).StatusCode);
            Assert.Equal(400, (await Tc(db).IssueAsync(A, UserA, Req(s, conduct: new string('x', 101)), default)).StatusCode);
            var r = await Tc(db).IssueAsync(A, UserA, Req(s, conduct: "Excellent"), default);
            Assert.True(r.Status, r.Message);
            var snap = SnapshotOf(db, r.Data!.CertificateId);
            Assert.Equal("Passed out", snap.Outcome); Assert.Contains("Passed out", snap.PromotionStatus); Assert.Equal("Excellent", snap.Conduct);
        }

        [DbFact]
        public async Task Numbers_stay_unique_and_in_sequence_when_several_certificates_are_issued_at_the_same_moment()
        {
            using var db = new TaxDb();
            var students = new List<string>();
            for (var i = 0; i < 8; i++) students.Add(await Left(db, i));
            var results = await Task.WhenAll(students.Select(s => Task.Run(() => Tc(db).IssueAsync(A, UserA, Req(s), default))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            var numbers = results.Select(r => r.Data!.CertificateNumber).ToList();
            Assert.Equal(8, numbers.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 8).Select(n => n.ToString("D4")), numbers.Select(n => n[^4..]).OrderBy(x => x));                   // 0001 .. 0008, none skipped, none twice
            Assert.Equal(8, db.Scalar<int>("select last_number from tb_transfer_certificate_sequences"));
        }

        [DbFact]
        public async Task A_void_number_is_never_reused_the_row_stays_and_a_reissue_takes_a_new_number()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var first = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;
            Assert.Equal(400, (await Tc(db).VoidAsync(A, UserA, first.CertificateId, new VoidTcRequest { Reason = "short", Confirm = true }, default)).StatusCode);
            Assert.Equal(400, (await Tc(db).VoidAsync(A, UserA, first.CertificateId, new VoidTcRequest { Reason = "A proper long reason", Confirm = false }, default)).StatusCode);
            var v = await Tc(db).VoidAsync(A, UserA, first.CertificateId, new VoidTcRequest { Reason = "Issued with a wrong date", Confirm = true }, default);
            Assert.True(v.Status, v.Message); Assert.Equal("VOID", v.Data!.Status); Assert.Equal("Issued with a wrong date", v.Data.VoidReason); Assert.NotNull(v.Data.VoidedAt);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_transfer_certificates where certificate_id = {first.CertificateId} and status = 'VOID'"));       // the row stays
            Assert.Equal(409, (await Tc(db).VoidAsync(A, UserA, first.CertificateId, new VoidTcRequest { Reason = "Issued with a wrong date", Confirm = true }, default)).StatusCode);
            var second = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;                                                                                // a reissue is allowed (the void one no longer counts)
            Assert.NotEqual(first.CertificateNumber, second.CertificateNumber);
            Assert.EndsWith("/0001", first.CertificateNumber); Assert.EndsWith("/0002", second.CertificateNumber);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_transfer_certificates where student_id = {Q(s)}"));
        }

        [DbFact]
        public async Task A_void_certificate_prints_nothing_and_cannot_be_duplicated()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var c = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;
            Assert.True((await Tc(db).VoidAsync(A, UserA, c.CertificateId, new VoidTcRequest { Reason = "Issued to the wrong student", Confirm = true }, default)).Status);
            var copiesBefore = db.Scalar<int>("select count(*) from tb_transfer_certificate_copies");
            var original = await Tc(db).OriginalPdfAsync(A, UserA, c.CertificateId, default); var dup = await Tc(db).DuplicatePdfAsync(A, UserA, c.CertificateId, default);
            Assert.Equal(409, original.StatusCode); Assert.Null(original.Content); Assert.Equal(409, dup.StatusCode); Assert.Null(dup.Content);
            Assert.Equal(copiesBefore, db.Scalar<int>("select count(*) from tb_transfer_certificate_copies"));
        }

        // ------------------------------------------------------------------------------------------------ the dues
        [DbFact]
        public async Task Unpaid_dues_need_the_override_with_ten_characters_it_is_stored_on_the_row_and_the_PDF_prints_only_Not_cleared()
        {
            using var db = new TaxDb(); var s = Clean(db);
            db.Exec($"update tb_student_fee_dues set amount_paid = 0, due_date = current_date - 1 where fee_due_id = (select min(fee_due_id) from tb_student_fee_dues where school_id = {Q(A)} and student_id = {Q(s)})");
            var leave = await Leaving(db).RecordAsync(A, UserA, new RecordLeavingRequest { StudentId = s, Type = "LEFT", Reason = "Moved", Confirm = true, OverrideDues = true, OverrideReason = "Will be paid in December" }, default);
            Assert.True(leave.Status, leave.Message);
            var check = (await Tc(db).CheckAsync(A, s, default)).Data!;
            Assert.True(check.RequiresOverride); Assert.True(check.Outstanding > 0); Assert.NotEmpty(check.Dues);
            Assert.Equal(400, (await Tc(db).IssueAsync(A, UserA, Req(s), default)).StatusCode);
            Assert.Equal(400, (await Tc(db).IssueAsync(A, UserA, Req(s, ovr: true, ovrReason: "too short"), default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_transfer_certificates"));
            var r = await Tc(db).IssueAsync(A, UserA, Req(s, ovr: true, ovrReason: "Principal approved in writing"), default);
            Assert.True(r.Status, r.Message); Assert.True(r.Data!.OverrideApplied); Assert.False(r.Data.FeesCleared);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_transfer_certificates where certificate_id = {r.Data.CertificateId} and override_applied and override_reason = 'Principal approved in writing' and outstanding_amount = {check.Outstanding} and not fees_cleared"));
            var lines = TransferCertificatePdf.Lines(SnapshotOf(db, r.Data.CertificateId), 0);
            Assert.Contains(lines, l => l.EndsWith("Fee dues: Not cleared"));
            Assert.DoesNotContain(lines, l => l.Contains("Principal approved") || l.Contains("₹") || l.Contains("Outstanding") || l.Contains("override", StringComparison.OrdinalIgnoreCase));   // neither the reason nor an amount is printed
        }

        // ------------------------------------------------------------------------------------------------ immutable, duplicates, audit
        [DbFact]
        public async Task A_later_edit_of_the_student_the_school_or_the_principal_cannot_change_an_issued_certificate_a_reprint_equals_the_first_print()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var c = (await Tc(db).IssueAsync(A, UserA, Req(s, conduct: "Very good", remarks: "Left for a new city"), default)).Data!;
            var firstPdf = (await Tc(db).OriginalPdfAsync(A, UserA, c.CertificateId, default)).Content!;
            var firstLines = TransferCertificatePdf.Lines(SnapshotOf(db, c.CertificateId), 0);
            var snapshotHash = db.Scalar<string>($"select md5(snapshot::text) from tb_transfer_certificates where certificate_id = {c.CertificateId}");
            // edit everything that is printed
            db.Exec($"update tb_students set first_name = 'Changed', last_name = 'Person', admission_number = 'ZZZ', date_of_birth = date '1990-01-01' where student_id = {Q(s)}");
            db.Exec($"update tb_schools set school_name = 'Renamed School', address_line1 = 'Another road', school_code = 'NEW-CODE' where school_id = {Q(A)}");
            db.Exec($"update tb_users set display_name = 'New Principal' where school_id = {Q(A)} and user_id in (select user_id from tb_user_roles ur join lut_roles r on r.role_id = ur.role_id where r.role_code = 'PRINCIPAL')");
            db.Exec($"update tb_student_enrollments set leaving_reason = 'Something else entirely' where school_id = {Q(A)} and student_id = {Q(s)} and academic_year_id = {TaxDb.YearA}");
            var secondPdf = (await Tc(db).OriginalPdfAsync(A, UserA, c.CertificateId, default)).Content!;
            var secondLines = TransferCertificatePdf.Lines(SnapshotOf(db, c.CertificateId), 0);
            Assert.Equal(firstLines, secondLines);                                                                                                   // text-level equality of the printed content
            Assert.Equal(snapshotHash, db.Scalar<string>($"select md5(snapshot::text) from tb_transfer_certificates where certificate_id = {c.CertificateId}"));
            Assert.DoesNotContain(secondLines, l => l.Contains("Changed") || l.Contains("Renamed") || l.Contains("New Principal") || l.Contains("Something else"));
            Assert.Equal(firstPdf.Length, secondPdf.Length);                                                                                         // and the files are the same size (same content, same layout)
            Assert.Equal(StripVolatile(firstPdf), StripVolatile(secondPdf));                                                                         // byte-level once the creation timestamps and ids are removed
        }

        /// <summary>A PDF carries its creation date and a document id; everything else must be identical for the same content.</summary>
        private static string StripVolatile(byte[] pdf)
        {
            var text = System.Text.Encoding.Latin1.GetString(pdf);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"/(CreationDate|ModDate)\s*\(D:[^)]*\)", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"/ID\s*\[[^\]]*\]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<xmp:(CreateDate|ModifyDate|MetadataDate)>[^<]*</xmp:\1>", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"uuid:[0-9a-fA-F\-]+", "");
            return text;
        }

        [DbFact]
        public async Task A_duplicate_keeps_the_number_is_marked_DUPLICATE_COPY_No_N_and_every_print_is_an_audit_row_with_who_and_when()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var c = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;
            var d1 = await Tc(db).DuplicatePdfAsync(A, UserA, c.CertificateId, default);
            var d2 = await Tc(db).DuplicatePdfAsync(A, 121, c.CertificateId, default);
            Assert.Equal(200, d1.StatusCode); Assert.Contains("DUPLICATE_1", d1.FileName); Assert.Contains("DUPLICATE_2", d2.FileName);
            await Tc(db).OriginalPdfAsync(A, UserA, c.CertificateId, default);                                                                       // a re-print of the original is audited too
            var snap = SnapshotOf(db, c.CertificateId);
            Assert.DoesNotContain(TransferCertificatePdf.Lines(snap, 0), l => l.Contains("DUPLICATE"));
            Assert.Contains("DUPLICATE COPY No. 1", TransferCertificatePdf.Lines(snap, 1)); Assert.Contains("DUPLICATE COPY No. 2", TransferCertificatePdf.Lines(snap, 2));
            Assert.All(new[] { 0, 1, 2 }, n => Assert.Contains("Certificate No. " + c.CertificateNumber, TransferCertificatePdf.Lines(snap, n)));   // the SAME number on every copy
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_transfer_certificates"));                                                        // no new certificate
            var copies = (await Tc(db).GetAsync(A, c.CertificateId, default)).Data!.Copies!;
            Assert.Equal(new[] { "ORIGINAL:0", "DUPLICATE:1", "DUPLICATE:2", "ORIGINAL:0" }, copies.Select(x => x.Kind + ":" + x.CopyNo));
            Assert.Equal(UserA, db.Scalar<long>($"select printed_by from tb_transfer_certificate_copies where certificate_id = {c.CertificateId} and kind = 'DUPLICATE' and copy_no = 1"));
            Assert.Equal(121, db.Scalar<long>($"select printed_by from tb_transfer_certificate_copies where certificate_id = {c.CertificateId} and kind = 'DUPLICATE' and copy_no = 2"));
            Assert.All(copies, x => Assert.True(x.PrintedAt > DateTime.UtcNow.AddMinutes(-5)));
            Assert.Equal(2, (await Tc(db).GetAsync(A, c.CertificateId, default)).Data!.Duplicates);
        }

        [DbFact]
        public async Task Duplicates_requested_at_the_same_moment_get_different_numbers()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var c = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;
            var files = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => Tc(db).DuplicatePdfAsync(A, UserA, c.CertificateId, default))));
            Assert.All(files, f => Assert.Equal(200, f.StatusCode));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, db.Scalar<string>($"select string_agg(copy_no::text, ',' order by copy_no) from tb_transfer_certificate_copies where certificate_id = {c.CertificateId} and kind = 'DUPLICATE'").Split(',').Select(int.Parse));
        }

        // ------------------------------------------------------------------------------------------------ the link to 8a
        [DbFact]
        public async Task The_leaving_correction_is_refused_while_a_certificate_stands_and_allowed_again_once_it_is_void()
        {
            using var db = new TaxDb(); var s = await Left(db);
            var c = (await Tc(db).IssueAsync(A, UserA, Req(s), default)).Data!;
            var check = (await Leaving(db).CheckCorrectionAsync(A, s, default)).Data!;
            Assert.False(check.CanCorrect); Assert.Contains(check.Blockers, b => b.Contains("transfer certificate"));
            var refused = await Leaving(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "Recorded by mistake", Confirm = true }, default);
            Assert.Equal(409, refused.StatusCode);
            Assert.Equal("LEFT", db.Scalar<string>($"select outcome from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}"));
            Assert.True((await Tc(db).VoidAsync(A, UserA, c.CertificateId, new VoidTcRequest { Reason = "The leaving was recorded in error", Confirm = true }, default)).Status);
            var ok = await Leaving(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "Recorded by mistake", Confirm = true }, default);
            Assert.True(ok.Status, ok.Message);
        }

        // ------------------------------------------------------------------------------------------------ the printed words
        [Fact]
        public void The_date_of_birth_in_words_reads_like_a_certificate()
        {
            Assert.Equal("Fifth March Two Thousand Twelve", NumberWords.DateInWords(new DateOnly(2012, 3, 5)));
            Assert.Equal("Twenty Third November One Thousand Nine Hundred Ninety Nine", NumberWords.DateInWords(new DateOnly(1999, 11, 23)));
            Assert.Equal("Thirty First December Two Thousand", NumberWords.DateInWords(new DateOnly(2000, 12, 31)));
            Assert.Equal("First January Two Thousand Five", NumberWords.DateInWords(new DateOnly(2005, 1, 1)));
        }

        [Fact]
        public void The_fields_have_the_principal_signature_line_with_the_name_as_text_and_the_marking_only_on_a_duplicate()
        {
            var s = new TcSnapshot { CertificateNumber = "X/TC/2026-27/0001", SchoolName = "S", StudentName = "A B", AdmissionNumber = "1", DateOfBirth = "05/03/2012", DateOfBirthInWords = "Fifth March Two Thousand Twelve", AdmissionDate = "01/04/2020",
                LastClass = "Class 5 - A", AcademicYear = "2026-27", Outcome = "Left", LeavingDate = "26/11/2026", PromotionStatus = "Not applicable", Conduct = "Good", FeeDues = "Cleared", IssueDate = "26/11/2026", PrincipalName = "Dr. Rao" };
            var lines = TransferCertificatePdf.Lines(s, 0);
            Assert.Equal("Principal: Dr. Rao", lines[^1]); Assert.Contains("Date of issue: 26/11/2026", lines);
            Assert.Contains(lines, l => l.Contains("Date of birth (in figures): 05/03/2012")); Assert.Contains(lines, l => l.Contains("Fee dues: Cleared")); Assert.Contains(lines, l => l.Contains("Conduct: Good"));
            Assert.DoesNotContain(lines, l => l.Contains("DUPLICATE")); Assert.Null(TransferCertificatePdf.CopyLabel(0)); Assert.Equal("DUPLICATE COPY No. 3", TransferCertificatePdf.CopyLabel(3));
        }
    }
}
