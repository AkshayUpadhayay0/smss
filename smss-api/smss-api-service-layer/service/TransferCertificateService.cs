using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.helper;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Transfer certificates (Stage 8b). A certificate can be issued only for a student whose latest enrolment ended with LEFT, TRANSFERRED_OUT or PASSED_OUT. The number is
    /// &lt;school code&gt;/TC/&lt;academic year&gt;/&lt;4 digits&gt;, taken under a per-school lock and never reused. What is printed is frozen into a snapshot at issue time and every later copy is drawn from it.
    /// Dues: the same check and override as leaving (a reason of at least 10 characters, stored on the row); the PDF prints only "Cleared" or "Not cleared".
    /// </summary>
    public class TransferCertificateService : ITransferCertificateService
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
        private static readonly string[] Eligible = { LeavingRules.Left, LeavingRules.TransferredOut, LeavingRules.PassedOut };
        private const string NotFound = "Student not found.";
        private const string CertNotFound = "Certificate not found.";
        private const string Failed = "The request could not be completed. Please try again.";

        private readonly ITransferCertificateRepository _repo;
        private readonly ILeavingRepository _leaving;
        private readonly ILogger<TransferCertificateService> _logger;
        public TransferCertificateService(ITransferCertificateRepository repo, ILeavingRepository leaving, ILogger<TransferCertificateService> logger) { _repo = repo; _leaving = leaving; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        private static string OutcomeLabel(string? o) => o switch { LeavingRules.Left => "Left", LeavingRules.TransferredOut => "Transferred out", LeavingRules.PassedOut => "Passed out", _ => "" };

        /// <summary>The date the student left: the recorded leaving date, else the day the outcome was written (school time).</summary>
        private static DateOnly LeavingDateOf(TcContext c) => c.LeavingDate ?? (c.OutcomeAt is { } t ? DateOnly.FromDateTime(t.ToUniversalTime().AddMinutes(330)) : SchoolClock.Today());

        private async Task<(TcCheckDto Dto, TcContext Ctx)?> BuildAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var c = await _repo.GetContextAsync(schoolId, studentId, ct);
            if (c == null) return null;
            var date = LeavingDateOf(c);
            var dto = new TcCheckDto
            {
                StudentId = studentId, StudentName = FullName(c), AdmissionNumber = c.AdmissionNumber, ClassName = c.ClassName, SectionName = c.SectionName, YearName = c.YearName, Outcome = c.Outcome,
                LeavingDate = date, ExistingCertificateId = c.ExistingCertificateId, ExistingCertificateNumber = c.ExistingCertificateNumber, PrincipalName = c.PrincipalName,
            };
            if (Array.IndexOf(Eligible, c.Outcome) < 0) dto.Blockers.Add($"{dto.StudentName} has no leaving recorded: record the leaving first. A certificate is only for a student who left, was transferred out or passed out.");
            if (c.ExistingCertificateId != null) dto.Blockers.Add($"Certificate {c.ExistingCertificateNumber} is already issued for this leaving: print a duplicate of it, or void it first.");
            var dues = await _leaving.ListUnpaidDuesAsync(schoolId, studentId, c.YearId, ct);
            foreach (var d in dues.Where(d => d.DueDate <= date))
            {
                var open = d.AmountDue - d.AmountPaid;
                dto.Outstanding += open;
                dto.Dues.Add(new LeavingDueDto { FeeDueId = d.FeeDueId, FeeType = d.FeeTypeName, DueDate = d.DueDate, AmountDue = d.AmountDue, AmountPaid = d.AmountPaid, Outstanding = open });
            }
            dto.RequiresOverride = dto.Outstanding > 0;
            dto.CanIssue = dto.Blockers.Count == 0;
            return (dto, c);
        }

        private static string FullName(TcContext c) => string.Join(' ', new[] { c.FirstName, c.MiddleName, c.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        private static string Fmt(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        /// <summary>Everything that will be printed, as text, as at this moment.</summary>
        private static TcSnapshot Snapshot(TcContext c, string number, DateOnly leaving, string conduct, string? remarks, bool cleared, DateOnly today)
        {
            var address = new List<string>();
            var l1 = string.Join(", ", new[] { c.AddressLine1, c.AddressLine2 }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (l1.Length > 0) address.Add(l1);
            var l2 = string.Join(", ", new[] { c.CityName, c.StateName }.Where(p => !string.IsNullOrWhiteSpace(p))) + (string.IsNullOrWhiteSpace(c.Pincode) ? "" : " - " + c.Pincode);
            if (l2.Trim().Length > 0) address.Add(l2.Trim());
            var contact = string.Join("  |  ", new[] { c.SchoolMobile == null ? null : "Phone: " + c.SchoolMobile, c.SchoolEmail, c.Website }.Where(p => !string.IsNullOrWhiteSpace(p)));
            var lastClass = c.ClassName == null ? "" : c.SectionName == null ? c.ClassName : $"{c.ClassName} - {c.SectionName}";
            var promotion = c.Outcome == LeavingRules.PassedOut ? "Passed out (completed the final class)"
                : c.EnteredBy == "PROMOTE" ? $"Promoted{(string.IsNullOrWhiteSpace(c.FromClassName) ? "" : " from " + c.FromClassName)} to {c.ClassName}; left during the academic year"
                : c.EnteredBy == "REPEAT" ? "Repeating the class; left during the academic year"
                : "Not applicable: left during the academic year";
            return new TcSnapshot
            {
                CertificateNumber = number, SchoolName = c.SchoolName, SchoolCode = c.SchoolCode, Board = string.IsNullOrWhiteSpace(c.BoardName) ? null : "Board: " + c.BoardName, SchoolAddress = address,
                SchoolContact = contact.Length == 0 ? null : contact, StudentName = FullName(c), AdmissionNumber = c.AdmissionNumber, Gender = c.Gender,
                DateOfBirth = Fmt(c.DateOfBirth), DateOfBirthInWords = NumberWords.DateInWords(c.DateOfBirth), FatherName = c.FatherName, MotherName = c.MotherName,
                GuardianName = c.GuardianName == null ? null : c.GuardianName + (string.IsNullOrWhiteSpace(c.GuardianRelationship) ? "" : " (" + Cap(c.GuardianRelationship) + ")"),
                AdmissionDate = c.AdmissionDate == null ? "" : Fmt(c.AdmissionDate.Value), LastClass = lastClass, AcademicYear = c.YearName, Outcome = OutcomeLabel(c.Outcome),
                LeavingDate = Fmt(leaving), LeavingReason = string.IsNullOrWhiteSpace(c.LeavingReason) ? null : c.LeavingReason, PromotionStatus = promotion, Conduct = conduct,
                FeeDues = cleared ? "Cleared" : "Not cleared", Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks, IssueDate = Fmt(today), PrincipalName = c.PrincipalName ?? "",
            };
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

        public async Task<ApiResponse<TcCheckDto>> CheckAsync(string? schoolId, string? studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TcCheckDto>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(studentId)) return Result<TcCheckDto>(400, "Enter a student id.", null);
            try
            {
                var b = await BuildAsync(schoolId, studentId.Trim(), ct);
                return b == null ? Result<TcCheckDto>(404, NotFound, null) : Result(200, "Successfully fetched", b.Value.Dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Certificate check failed for {StudentId}", studentId); return Result<TcCheckDto>(500, Failed, null); }
        }

        public async Task<ApiResponse<TcDto>> IssueAsync(string? schoolId, long userId, IssueTcRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TcDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<TcDto>(400, "The request is empty.", null);
            var conduct = string.IsNullOrWhiteSpace(request.Conduct) ? "Good" : request.Conduct.Trim();
            var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
            if (conduct.Length > 100 || (remarks?.Length ?? 0) > 500) return Result<TcDto>(400, "The conduct can be at most 100 characters and the remarks at most 500.", null);
            if (!request.Confirm) return Result<TcDto>(400, "Issuing a certificate uses up a certificate number: confirm first.", null);
            try
            {
                var b = await BuildAsync(schoolId, request.StudentId.Trim(), ct);
                if (b == null) return Result<TcDto>(404, NotFound, null);
                var (dto, ctx) = b.Value;
                if (!dto.CanIssue) return Result<TcDto>(409, dto.Blockers[0], null);
                var ovr = LeavingRules.OverrideProblem(dto.Outstanding, request.OverrideDues, request.OverrideReason);
                if (ovr != null) return Result<TcDto>(400, ovr, null);
                var overridden = dto.Outstanding > 0;
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var seq = await _repo.AllocateNumberAsync(schoolId, ctx.YearName, ct);
                var number = $"{ctx.SchoolCode}/TC/{ctx.YearName}/{seq:D4}";
                var snapshot = Snapshot(ctx, number, dto.LeavingDate, conduct, remarks, !overridden, SchoolClock.Today());
                long id;
                try
                {
                    id = await _repo.InsertAsync(new TcInsert(schoolId, dto.StudentId, ctx.EnrollmentId, number, ctx.YearName, seq, JsonSerializer.Serialize(snapshot, Json), conduct, remarks, !overridden, overridden,
                        overridden ? request.OverrideReason!.Trim() : null, dto.Outstanding, userId), ct);
                }
                catch (Exception ex) when (ex.InnerException?.Message.Contains("uq_tb_transfer_certificates_one_issued") == true || ex.Message.Contains("uq_tb_transfer_certificates_one_issued"))
                {
                    await tx.RollbackAsync(ct);
                    return Result<TcDto>(409, "A certificate was just issued for this leaving. Reload and print a duplicate of it.", null);
                }
                await tx.CommitAsync(ct);
                var row = await _repo.GetAsync(schoolId, id, ct);
                return Result(200, $"Certificate {number} is issued.", ToDto(row!, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Certificate issue failed for {StudentId}", request.StudentId); return Result<TcDto>(500, Failed, null); }
        }

        private static TcDto ToDto(TcRow r, List<TcCopyRow>? copies) => new()
        {
            CertificateId = r.CertificateId, CertificateNumber = r.CertificateNumber, StudentId = r.StudentId, StudentName = r.StudentName, ClassName = r.ClassName, SectionName = r.SectionName, Outcome = r.Outcome,
            Status = r.Status, Conduct = r.Conduct, Remarks = r.Remarks, FeesCleared = r.FeesCleared, OverrideApplied = r.OverrideApplied, OverrideReason = r.OverrideReason, OutstandingAmount = r.OutstandingAmount,
            IssuedAt = r.IssuedAt, IssuedBy = r.IssuedBy, VoidedAt = r.VoidedAt, VoidedBy = r.VoidedBy, VoidReason = r.VoidReason, Duplicates = r.Duplicates,
            Copies = copies?.Select(c => new TcCopyDto { Kind = c.Kind, CopyNo = c.CopyNo, PrintedAt = c.PrintedAt, PrintedBy = c.PrintedBy }).ToList(),
        };

        public async Task<ApiResponse<List<TcDto>>> ListAsync(string? schoolId, string? studentId, string? status, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<TcDto>>(400, TenantMessages.NoSchool, null);
            var st = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
            if (st != null && st is not ("ISSUED" or "VOID")) return Result<List<TcDto>>(400, "The status must be ISSUED or VOID.", null);
            try { return Result(200, "Successfully fetched", (await _repo.ListAsync(schoolId, string.IsNullOrWhiteSpace(studentId) ? null : studentId.Trim(), st, ct)).Select(r => ToDto(r, null)).ToList()); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Certificate list failed"); return Result<List<TcDto>>(500, Failed, null); }
        }

        public async Task<ApiResponse<TcDto>> GetAsync(string? schoolId, long certificateId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TcDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var r = await _repo.GetAsync(schoolId, certificateId, ct);
                return r == null ? Result<TcDto>(404, CertNotFound, null) : Result(200, "Successfully fetched", ToDto(r, await _repo.ListCopiesAsync(schoolId, certificateId, ct)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Certificate read failed"); return Result<TcDto>(500, Failed, null); }
        }

        private async Task<TcFileResult> PrintAsync(string? schoolId, long userId, long certificateId, bool duplicate, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return new TcFileResult { StatusCode = 400, Message = TenantMessages.NoSchool };
            try
            {
                var r = await _repo.GetAsync(schoolId, certificateId, ct);
                if (r == null) return new TcFileResult { StatusCode = 404, Message = CertNotFound };
                if (r.Status == "VOID") return new TcFileResult { StatusCode = 409, Message = $"Certificate {r.CertificateNumber} has been voided and cannot be printed." };
                var snapshot = JsonSerializer.Deserialize<TcSnapshot>(r.Snapshot, Json);
                if (snapshot == null) return new TcFileResult { StatusCode = 500, Message = Failed };
                int no;
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    no = await _repo.AddCopyAsync(schoolId, certificateId, duplicate, userId, ct);   // audited BEFORE the file is handed out
                    await tx.CommitAsync(ct);
                }
                var bytes = TransferCertificatePdf.Render(snapshot, duplicate ? no : 0);
                var safe = new string(r.CertificateNumber.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_').ToArray());
                return new TcFileResult { StatusCode = 200, Content = bytes, FileName = $"TC_{safe}{(duplicate ? "_DUPLICATE_" + no : "")}.pdf" };
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Certificate print failed"); return new TcFileResult { StatusCode = 500, Message = Failed }; }
        }

        public Task<TcFileResult> OriginalPdfAsync(string? schoolId, long userId, long certificateId, CancellationToken ct) => PrintAsync(schoolId, userId, certificateId, false, ct);
        public Task<TcFileResult> DuplicatePdfAsync(string? schoolId, long userId, long certificateId, CancellationToken ct) => PrintAsync(schoolId, userId, certificateId, true, ct);

        public async Task<ApiResponse<TcDto>> VoidAsync(string? schoolId, long userId, long certificateId, VoidTcRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TcDto>(400, TenantMessages.NoSchool, null);
            var reason = request?.Reason?.Trim();
            if (reason == null || reason.Length < LeavingRules.MinReason) return Result<TcDto>(400, $"Give a reason of at least {LeavingRules.MinReason} characters.", null);
            if (reason.Length > 500) return Result<TcDto>(400, "The reason can be at most 500 characters.", null);
            if (!request!.Confirm) return Result<TcDto>(400, "Voiding a certificate cannot be undone: confirm first.", null);
            try
            {
                var r = await _repo.GetAsync(schoolId, certificateId, ct);
                if (r == null) return Result<TcDto>(404, CertNotFound, null);
                if (r.Status == "VOID") return Result<TcDto>(409, $"Certificate {r.CertificateNumber} is already void.", null);
                if (!await _repo.VoidAsync(schoolId, certificateId, reason, userId, ct)) return Result<TcDto>(409, "This certificate was changed by someone else. Reload and check again.", null);
                var again = await _repo.GetAsync(schoolId, certificateId, ct);
                return Result(200, $"Certificate {r.CertificateNumber} is void. Its number is not reused.", ToDto(again!, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Certificate void failed"); return Result<TcDto>(500, Failed, null); }
        }
    }
}
