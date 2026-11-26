using smss_api_db_layer.constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    // Two steps: an application is ADDED (status code APPLICATION_SUBMITTED) and then CONFIRMED (ADMITTED: the student is created), with REJECTED and CANCELLED (withdrawn)
    // as exits from the added state. The status codes are unchanged; StatusLabel gives the words people see. Status names are resolved from lut_status, never ids.
    public class AdmissionService : IAdmissionService
    {
        private const string Submitted = "APPLICATION_SUBMITTED";
        private const string Rejected = "REJECTED";
        private const string Admitted = "ADMITTED";
        private const string Cancelled = "CANCELLED";

        private readonly IAdmissionRepository _repo;
        private readonly INotificationService _notify;
        private readonly IStudentFeeBillingService _billing;
        private readonly ILogger<AdmissionService> _logger;
        private readonly IStudentDocumentService? _studentDocuments;
        private readonly IParentAccountService? _parentAccounts;

        public AdmissionService(IAdmissionRepository repo, INotificationService notify, IStudentFeeBillingService billing, ILogger<AdmissionService> logger, IStudentDocumentService? studentDocuments = null, IParentAccountService? parentAccounts = null)
        {
            _studentDocuments = studentDocuments;
            _parentAccounts = parentAccounts;
            _repo = repo;
            _notify = notify;
            _billing = billing;
            _logger = logger;
        }

        // ---------------- reads ----------------
        public async Task<ApiResponse<List<AdmissionResponseDto>>> GetAllAsync(string schoolId, string? status, long? classId, long? academicYearId, CancellationToken ct)
        {
            try
            {
                int? statusId = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    statusId = await _repo.GetAdmissionStatusIdAsync(status.Trim().ToUpperInvariant(), ct);
                    if (statusId == null) return Result<List<AdmissionResponseDto>>(400, "Unknown status. Use APPLICATION_SUBMITTED, REJECTED, ADMITTED or CANCELLED.", null);
                }
                var list = await _repo.GetAllAsync(schoolId, statusId, classId, academicYearId, ct);
                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                var classes = await _repo.GetClassNamesAsync(schoolId, list.Select(a => a.AppliedClassId).Distinct().ToList(), ct);
                var types = await _repo.GetAdmissionTypeNamesAsync(list.Select(a => a.AdmissionTypeId).Distinct().ToList(), ct);
                var years = await _repo.GetAcademicYearNamesAsync(schoolId, list.Select(a => a.AcademicYearId).Distinct().ToList(), ct);

                var dtos = list.Select(a => Fill(new AdmissionResponseDto(), a, statuses, classes, types, years)).ToList();
                return Result(200, "Successfully fetched", dtos);
            }
            catch (Exception ex) { return Error<List<AdmissionResponseDto>>(ex, "fetching admissions"); }
        }

        public async Task<ApiResponse<AdmissionDetailDto>> GetByIdAsync(string schoolId, long admissionId, CancellationToken ct)
        {
            try
            {
                var admission = await _repo.GetByIdAsync(schoolId, admissionId, false, ct);
                if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);
                return Result(200, "Successfully fetched", await ToDetailAsync(admission, ct));
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, "fetching admission"); }
        }

        // ---------------- submit ----------------
        private sealed record ReuseChoice(long? PrimaryId, List<(long Id, string? Relationship)> Extras);

        /// <summary>
        /// Existing guardians picked for the application (a sibling's parents): they must be this school's, active (a merged one is replaced by its survivor). For an existing PRIMARY guardian the
        /// guardian's name and mobile are copied onto the application for display, and WhatsApp and email are needed only where the guardian record has none. Null when nothing is reused.
        /// </summary>
        private async Task<(ReuseChoice? Choice, (int Code, string Message)? Error)> PrepareReuseAsync(string schoolId, SubmitAdmissionRequestDto req, CancellationToken ct)
        {
            var extras = (req.AdditionalGuardians ?? new()).Select(e => new GuardianPick(e.GuardianId, e.Relationship)).ToList();
            if (req.ExistingGuardianId == null && extras.Count == 0) return (null, null);
            var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
            if (activeId == null) return (null, (500, "The system is not configured correctly. Please contact support."));
            var load = await GuardianReuse.LoadAsync(_repo, schoolId, req.ExistingGuardianId, extras, activeId.Value, false, "saved", ct);
            if (load.Error is { } error) return (null, error);
            if (load.Primary is { } p)
            {
                req.ExistingGuardianId = p.GuardianId;   // a merged guardian is replaced by its survivor
                req.GuardianName = GuardianReuse.Display(p);
                req.GuardianMobileNumber = p.MobileNumber;
                if (req.GuardianRelationship is not ("FATHER" or "MOTHER" or "GUARDIAN")) req.GuardianRelationship = p.Relationship;
                var wa = !string.IsNullOrWhiteSpace(p.WhatsAppNumber) ? p.WhatsAppNumber : PhoneNumbers.WhatsAppFor(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, p.MobileNumber);
                var email = !string.IsNullOrWhiteSpace(p.Email) ? p.Email.Trim() : Clean(req.GuardianEmail);
                var bad = (string.IsNullOrWhiteSpace(p.WhatsAppNumber) ? PhoneNumbers.WhatsAppError(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, p.MobileNumber, "The guardian's") : null)
                          ?? (string.IsNullOrWhiteSpace(p.Email) ? PhoneNumbers.EmailError(req.GuardianEmail, "The guardian's") : null);
                if (bad != null) return (null, (400, bad));
                if (p.ConsentGivenAt == null && !req.GuardianConsent) return (null, (400, GuardianConsentRules.Required));   // an existing guardian with consent on record needs no new tick
                req.GuardianWhatsAppNumber = wa; req.GuardianWhatsAppSameAsMobile = false; req.GuardianEmail = email;   // what the application will show and Confirm will write where the record has none
            }
            return (new ReuseChoice(load.Primary?.GuardianId, load.Extras.Select(x => (x.Guardian.GuardianId, x.Relationship ?? x.Guardian.Relationship)).ToList()), null);
        }

        /// <summary>Makes the application's extra guardians equal to the picked ones (adds the new, drops the removed, updates a changed relationship).</summary>
        private async Task SyncExtrasAsync(string schoolId, long admissionId, ReuseChoice? choice, CancellationToken ct)
        {
            var wanted = choice?.Extras ?? new List<(long Id, string? Relationship)>();
            var current = await _repo.GetAdmissionGuardiansAsync(schoolId, admissionId, true, ct);
            foreach (var row in current.Where(c => wanted.All(w => w.Id != c.GuardianId))) _repo.RemoveAdmissionGuardian(row);
            foreach (var (id, rel) in wanted)
            {
                var row = current.FirstOrDefault(c => c.GuardianId == id);
                if (row != null) row.Relationship = rel;
                else await _repo.AddAdmissionGuardianAsync(new TbAdmissionGuardian { AdmissionId = admissionId, GuardianId = id, SchoolId = schoolId, Relationship = rel, CreatedAt = DateTime.UtcNow }, ct);
            }
        }

        /// <summary>Everything about a submitted / edited application that can be wrong without touching the database row: foreign ids must be this school's, the date plausible.</summary>
        /// <param name="keepTypeId">An edit may keep the application's current admission type even if it has since been deactivated.</param>
        private async Task<string?> ValidateRequestAsync(string schoolId, SubmitAdmissionRequestDto req, CancellationToken ct, long? keepTypeId = null)
        {
            // Ids come from the client: every school-owned one must belong to THIS school (admission type, gender, blood group, category are global)
            if (!await _repo.AcademicYearBelongsToSchoolAsync(schoolId, req.AcademicYearId, ct)) return "The selected academic year was not found for your school.";
            if (!await _repo.ClassBelongsToSchoolAsync(schoolId, req.AppliedClassId, ct)) return "The selected class was not found for your school.";
            if (req.AdmissionTypeId is not long typeId || !await _repo.AdmissionTypeUsableAsync(typeId, allowInactive: keepTypeId == typeId, ct)) return "The selected admission type is not available.";
            if (req.GenderId.HasValue && !await _repo.GenderExistsAsync(req.GenderId.Value, ct)) return "The selected gender is not valid.";
            if (req.BloodGroupId.HasValue && !await _repo.BloodGroupExistsAsync(req.BloodGroupId.Value, ct)) return "The selected blood group is not valid.";
            if (req.StudentCategoryId.HasValue && !await _repo.StudentCategoryExistsAsync(req.StudentCategoryId.Value, ct)) return "The selected student category is not valid.";
            if (req.DateOfBirth.Date >= DateTime.UtcNow.Date) return "Date of birth must be in the past.";
            // the primary guardian's WhatsApp number and email are mandatory (the controller's model validation says the same; this keeps every caller honest)
            // (an EXISTING primary guardian is checked against the guardian record by PrepareReuseAsync instead)
            if (req.ExistingGuardianId == null)
            {
                var contact = PhoneNumbers.WhatsAppError(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, req.GuardianMobileNumber, "The guardian's")
                              ?? PhoneNumbers.EmailError(req.GuardianEmail, "The guardian's");
                if (contact != null) return contact;
                if (!req.GuardianConsent) return GuardianConsentRules.Required;
                if (string.IsNullOrWhiteSpace(req.GuardianName) || string.IsNullOrWhiteSpace(req.GuardianRelationship) || string.IsNullOrWhiteSpace(req.GuardianMobileNumber)) return "The guardian's name, relationship and mobile number are required.";
            }
            return null;
        }

        /// <summary>Copies the applicant's details onto the row (never the number, date, status or decision trail).</summary>
        private static void ApplyDetails(TbAdmission e, SubmitAdmissionRequestDto req, long userId)
        {
            // the consent tick is recorded once, with who ticked it (the database keeps date and user together); an edit never removes it
            if (req.GuardianConsent && userId > 0 && e.GuardianConsentAt == null) { e.GuardianConsentAt = DateTime.UtcNow; e.GuardianConsentRecordedBy = userId; }
            e.AcademicYearId = req.AcademicYearId;
            e.AppliedClassId = req.AppliedClassId;
            e.AdmissionTypeId = req.AdmissionTypeId!.Value;   // always filled in by Submit / Update before this runs
            e.ApplicantFirstName = req.ApplicantFirstName.Trim();
            e.ApplicantMiddleName = Clean(req.ApplicantMiddleName);
            e.ApplicantLastName = Clean(req.ApplicantLastName);   // optional: a blank is stored as null
            e.DateOfBirth = req.DateOfBirth.Date;
            e.GenderId = req.GenderId;
            e.BloodGroupId = req.BloodGroupId;
            e.StudentCategoryId = req.StudentCategoryId;
            e.AadharNumber = Clean(req.AadharNumber);
            e.StudentNationId = Clean(req.StudentNationId);
            e.Email = Clean(req.Email);
            e.MobileNumber = Clean(req.MobileNumber);
            e.AddressLine1 = Clean(req.AddressLine1);
            e.AddressLine2 = Clean(req.AddressLine2);
            e.CountryId = req.CountryId;
            e.StateId = req.StateId;
            e.DistrictId = req.DistrictId;
            e.CityId = req.CityId;
            e.Pincode = Clean(req.Pincode);
            e.GuardianName = req.GuardianName.Trim();
            e.GuardianRelationship = req.GuardianRelationship;
            e.GuardianMobileNumber = req.GuardianMobileNumber;
            e.GuardianEmail = Clean(req.GuardianEmail);
            e.GuardianWhatsAppNumber = PhoneNumbers.WhatsAppFor(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, req.GuardianMobileNumber);
            e.PreviousSchool = Clean(req.PreviousSchool);
        }

        // ---------------- submit ----------------
        public async Task<ApiResponse<AdmissionDetailDto>> SubmitAsync(string schoolId, SubmitAdmissionRequestDto req, CancellationToken ct, long userId = 0)
        {
            try
            {
                if (req.AdmissionTypeId == null)
                {
                    var (def, defError) = await AdmissionTypeDefaults.ResolveAsync(_repo.GetAdmissionTypeByCodeAsync, AdmissionTypeCodes.NewAdmission, ct);
                    if (def == null) return Result<AdmissionDetailDto>(400, defError!, null);
                    req.AdmissionTypeId = def.AdmissionTypeId;
                }
                var (reuse, reuseError) = await PrepareReuseAsync(schoolId, req, ct);
                if (reuseError is { } re) return Result<AdmissionDetailDto>(re.Code, re.Message, null);
                var bad = await ValidateRequestAsync(schoolId, req, ct);
                if (bad != null) return Result<AdmissionDetailDto>(400, bad, null);

                var submittedId = await _repo.GetAdmissionStatusIdAsync(Submitted, ct);
                if (submittedId == null) return ConfigError<AdmissionDetailDto>($"Admission status '{Submitted}' missing from lut_status");

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    _repo.ClearTracking();   // nothing from a failed attempt may be saved again
                    var now = DateTime.UtcNow;
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    // numbers are handed out one at a time per school (the same advisory-lock pattern as fee receipt numbers); the unique index stays as the backstop
                    await _repo.LockApplicationNumberAsync(schoolId, ct);
                    var prefix = $"ADM{now.Year}";
                    var next = await _repo.GetMaxApplicationSequenceAsync(schoolId, prefix, ct) + 1;

                    var entity = new TbAdmission { SchoolId = schoolId, ApplicationNumber = $"{prefix}{next:D4}", ApplicationDate = now.Date, StatusId = submittedId.Value, CreatedAt = now, UpdatedAt = now };
                    ApplyDetails(entity, req, userId);
                    entity.GuardianId = reuse?.PrimaryId;

                    try
                    {
                        await _repo.AddAdmissionAsync(entity, ct);
                        await _repo.SaveChangesAsync(ct);
                        if (reuse != null && reuse.Extras.Count > 0)
                        {
                            await SyncExtrasAsync(schoolId, entity.AdmissionId, reuse, ct);
                            await _repo.SaveChangesAsync(ct);
                        }
                        await tx.CommitAsync(ct);
                        var detail = await ToDetailAsync(entity, ct);
                        await NotifyAsync(schoolId, entity.Email, entity.GuardianEmail,
                            name => EmailTemplates.ApplicationSubmitted(detail.ApplicantFullName, detail.ApplicationNumber, name),
                            $"application submitted {detail.ApplicationNumber}", ct);
                        return Result(201, "Admission application submitted.", detail);
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == "uq_tb_admissions_school_aadhar")
                    {
                        return Result<AdmissionDetailDto>(409, "An application with this Aadhaar number already exists for this school.", null);
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == "uq_tb_admissions_school_appno" && attempt < 2)
                    {
                        // can't happen while the lock is held; kept as the backstop
                        _logger.LogWarning(ex, "Application number collision for school {SchoolId}; retrying.", schoolId);
                    }
                }
                return Result<AdmissionDetailDto>(409, "Could not allocate an application number. Please try again.", null);
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, "submitting admission"); }
        }

        // ---------------- edit ----------------
        public async Task<ApiResponse<AdmissionDetailDto>> UpdateAsync(string schoolId, long admissionId, SubmitAdmissionRequestDto req, CancellationToken ct, long userId = 0)
        {
            try
            {
                var existing = await _repo.GetByIdAsync(schoolId, admissionId, false, ct);
                if (req.AdmissionTypeId == null)
                {
                    if (existing != null) req.AdmissionTypeId = existing.AdmissionTypeId;   // an edit that does not name a type keeps the current one
                    else
                    {
                        var (def, defError) = await AdmissionTypeDefaults.ResolveAsync(_repo.GetAdmissionTypeByCodeAsync, AdmissionTypeCodes.NewAdmission, ct);
                        if (def == null) return Result<AdmissionDetailDto>(400, defError!, null);
                        req.AdmissionTypeId = def.AdmissionTypeId;
                    }
                }
                var (reuse, reuseError) = await PrepareReuseAsync(schoolId, req, ct);
                if (reuseError is { } re) return Result<AdmissionDetailDto>(re.Code, re.Message, null);
                var bad = await ValidateRequestAsync(schoolId, req, ct, keepTypeId: existing?.AdmissionTypeId);
                if (bad != null) return Result<AdmissionDetailDto>(400, bad, null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockApplicationAsync(schoolId, admissionId, ct);   // an edit and a decision for one application queue up
                var admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);

                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                var current = statuses.TryGetValue(admission.StatusId, out var n) ? n : "UNKNOWN";
                if (current != Submitted)
                    return Result<AdmissionDetailDto>(409, $"This application is {StatusLabel(current)}, so it can no longer be edited. Only an application that has been added (not yet confirmed, rejected or withdrawn) can be changed.", null);

                var storedAadhaar = admission.AadharNumber;
                ApplyDetails(admission, req, userId);
                if (string.IsNullOrWhiteSpace(req.AadharNumber)) admission.AadharNumber = storedAadhaar;   // the form only ever sees it masked, so a blank box means keep it
                admission.UpdatedAt = DateTime.UtcNow;
                admission.GuardianId = reuse?.PrimaryId;   // the pick can be changed until the application is confirmed
                await SyncExtrasAsync(schoolId, admissionId, reuse, ct);
                try { await _repo.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ConstraintOf(ex) == "uq_tb_admissions_school_aadhar")
                {
                    return Result<AdmissionDetailDto>(409, "An application with this Aadhaar number already exists for this school.", null);
                }
                await tx.CommitAsync(ct);
                return Result(200, "Application updated.", await ToDetailAsync(admission, ct));
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, "editing admission"); }
        }

        // ---------------- transitions ----------------
        /// <summary>The refusal message for rejecting an application that is no longer in the added state.</summary>
        private static string? DecideRefusal(string current) => current switch
        {
            Rejected or Admitted or Cancelled => $"This application is already {StatusLabel(current)}, so it can't be rejected.",
            _ => null,
        };

        private static string? WithdrawRefusal(string current) => current switch
        {
            Rejected or Admitted => $"The application is already {StatusLabel(current)}, so it can't be withdrawn.",
            Cancelled => "This application was already withdrawn.",
            _ => null,
        };

        /// <summary>The words people see for a status code (the codes themselves never change).</summary>
        public static string StatusLabel(string status) => status switch
        {
            Submitted => "Application added", Rejected => "Rejected", Admitted => "Confirmed", Cancelled => "Withdrawn", _ => status,
        };

        public Task<ApiResponse<AdmissionDetailDto>> RejectAsync(string schoolId, long admissionId, long rejectedByUserId, AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Remarks))
                return Task.FromResult(Result<AdmissionDetailDto>(400, "A reason is required to reject an application.", null));

            return TransitionAsync(schoolId, admissionId, new[] { Submitted }, Rejected, a =>
            {
                a.RejectionReason = Clean(req.Remarks);
                a.RejectedBy = rejectedByUserId;
                a.RejectedAt = DateTime.UtcNow;
            }, "Application rejected.", ct, DecideRefusal);
        }

        public Task<ApiResponse<AdmissionDetailDto>> CancelAsync(string schoolId, long admissionId, AdmissionDecisionRequestDto req, CancellationToken ct) =>
            TransitionAsync(schoolId, admissionId, new[] { Submitted }, Cancelled, a => a.DecisionRemarks = Clean(req.Remarks),
                "Application withdrawn.", ct, WithdrawRefusal);

        // ---------------- confirm admission ----------------
        // Creates the student, enrollment, guardian and guardian link and marks the application ADMITTED — all in one
        // transaction, so a failure part-way leaves nothing behind.
        public async Task<ApiResponse<AdmissionDetailDto>> ConfirmAdmissionAsync(string schoolId, long admissionId, long confirmedByUserId, ConfirmAdmissionRequestDto req, CancellationToken ct)
        {
            try
            {
                var admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);

                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                if (!statuses.TryGetValue(admission.StatusId, out var current) || current != Submitted)
                    return Result<AdmissionDetailDto>(409, current == Admitted ? "This application is already confirmed." : $"This application is {StatusLabel(current)}, so it can't be confirmed. Only an added application can be confirmed.", null);

                // an application made before the guardian's WhatsApp number and email were collected stays editable: it must be completed before it can be confirmed
                // (an application with an EXISTING primary guardian is checked inside the transaction, on the locked guardian record)
                if (admission.GuardianId == null)
                {
                    var notComplete = MissingContactMessage(admission.GuardianWhatsAppNumber, admission.GuardianEmail);
                    if (notComplete != null) return Result<AdmissionDetailDto>(409, notComplete, null);
                }

                if (!await _repo.SectionBelongsToSchoolAsync(schoolId, req.ClassSectionId, ct))
                    return Result<AdmissionDetailDto>(400, "The selected section was not found for your school.", null);
                if (await _repo.IsYearClosedAsync(schoolId, admission.AcademicYearId, ct)) return Result<AdmissionDetailDto>(409, ClosedYear.Message, null);
                var target = EnrollmentTargetRules.Refusal(await _repo.CheckEnrollmentTargetAsync(schoolId, admission.AcademicYearId, req.ClassSectionId, Clean(req.RollNumber), ct), Clean(req.RollNumber));
                if (target is { } refused) return Result<AdmissionDetailDto>(refused.Code, refused.Message, null);

                var admittedId = await _repo.GetAdmissionStatusIdAsync(Admitted, ct);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (admittedId == null || activeId == null)
                    return ConfigError<AdmissionDetailDto>("'ADMITTED' admission status or 'Active' general status missing from lut_status");

                // The student id ({short code}ST{nnn}) is built from the school's short code, so the school must have one
                var school = await _repo.GetSchoolIdentityAsync(schoolId, ct);
                if (school == null) return Result<AdmissionDetailDto>(404, "School not found.", null);
                if (string.IsNullOrWhiteSpace(school.ShortCode))
                    return Result<AdmissionDetailDto>(409, StudentIdHelper.NoShortCodeMessage, null);

                // Fee structure for this class + academic year (automatic assignment). Not finding one never blocks the admission.
                var sectionClass = await _repo.GetSectionClassAsync(schoolId, req.ClassSectionId, ct);
                var feeStructure = sectionClass == null
                    ? null
                    : await _repo.FindActiveFeeStructureAsync(schoolId, sectionClass.ClassId, admission.AcademicYearId, ct);
                string? feeWarning = null;
                if (feeStructure == null)
                {
                    var yearName = (await _repo.GetAcademicYearNamesAsync(schoolId, new[] { admission.AcademicYearId }, ct)).GetValueOrDefault(admission.AcademicYearId, "this academic year");
                    feeWarning = FeeAutoAssignment.NoStructureWarning(sectionClass?.ClassName ?? "this class", yearName);
                }

                // The id is known before anything is saved (count + 1), so the whole graph goes in ONE SaveChanges.
                // Two confirmations at the same moment can compute the same id; the primary key rejects the second one
                // and it simply tries again with a fresh count.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    _repo.ClearTracking();   // forget the rows of a failed attempt
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    // two confirmations of one application queue up; the second finds it already ADMITTED and is refused, so one application can never create two students
                    await _repo.LockApplicationAsync(schoolId, admissionId, ct);
                    admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                    if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);
                    if (!statuses.TryGetValue(admission.StatusId, out var nowStatus) || nowStatus != Submitted)
                        return Result<AdmissionDetailDto>(409, nowStatus == Admitted ? "This application is already confirmed." : $"This application is {StatusLabel(nowStatus ?? "")}, so it can't be confirmed. Only an added application can be confirmed.", null);
                    // A sibling's parents are LINKED, never copied: resolve merged guardians to their survivor, refuse an inactive one, and lock the guardian rows FOR UPDATE (in this transaction)
                    // so two confirmations that share a guardian queue up and the second reads the first one's result.
                    TbGuardian? reusedPrimary = null;
                    var reusedExtras = new List<(TbGuardian Guardian, string? Relationship)>();
                    var guardianChanges = new List<TbGuardianChange>();
                    var extraRows = await _repo.GetAdmissionGuardiansAsync(schoolId, admissionId, false, ct);
                    if (admission.GuardianId != null || extraRows.Count > 0)
                    {
                        var load = await GuardianReuse.LoadAsync(_repo, schoolId, admission.GuardianId, extraRows.Select(r => new GuardianPick(r.GuardianId, r.Relationship)).ToList(), activeId.Value, true, "confirmed", ct);
                        if (load.Error is { } loadError) return Result<AdmissionDetailDto>(loadError.Code, loadError.Message, null);
                        reusedPrimary = load.Primary;
                        reusedExtras = load.Extras;
                        if (reusedPrimary != null)
                        {
                            // the Stage 2 rule is checked on the guardian RECORD (what it has, plus what the admin entered for it)
                            var notComplete = MissingContactMessage(string.IsNullOrWhiteSpace(reusedPrimary.WhatsAppNumber) ? admission.GuardianWhatsAppNumber : reusedPrimary.WhatsAppNumber,
                                                                   string.IsNullOrWhiteSpace(reusedPrimary.Email) ? admission.GuardianEmail : reusedPrimary.Email);
                            if (notComplete != null) return Result<AdmissionDetailDto>(409, notComplete, null);
                            guardianChanges.AddRange(GuardianReuse.ApplyContact(reusedPrimary, admission.GuardianWhatsAppNumber, admission.GuardianEmail, confirmedByUserId, schoolId, DateTime.UtcNow));
                            if (reusedPrimary.ConsentGivenAt == null && admission.GuardianConsentAt != null && admission.GuardianConsentRecordedBy != null)
                            { reusedPrimary.ConsentGivenAt = admission.GuardianConsentAt; reusedPrimary.ConsentRecordedBy = admission.GuardianConsentRecordedBy; reusedPrimary.ConsentSource = GuardianConsentRules.SourceAdmissionForm; }
                        }
                    }
                    try
                    {
                        var now = DateTime.UtcNow;
                        var prefix = $"ADMNO{now.Year}";
                        var studentId = StudentIdHelper.Build(school.ShortCode!, await _repo.CountStudentsAsync(schoolId, ct) + 1);

                        var student = new TbStudent
                        {
                            StudentId = studentId,
                            SchoolId = schoolId,
                            AdmissionNumber = $"{prefix}{await _repo.GetMaxAdmissionNumberSequenceAsync(schoolId, prefix, ct) + 1:D4}",
                            FirstName = admission.ApplicantFirstName,
                            MiddleName = admission.ApplicantMiddleName,
                            LastName = admission.ApplicantLastName,
                            DateOfBirth = admission.DateOfBirth,
                            GenderId = admission.GenderId,
                            BloodGroupId = admission.BloodGroupId,
                            StudentCategoryId = admission.StudentCategoryId,
                            AdmissionTypeId = admission.AdmissionTypeId,
                            AdmissionDate = now.Date,
                            Email = admission.Email,
                            MobileNumber = admission.MobileNumber,
                            AddressLine1 = admission.AddressLine1,
                            AddressLine2 = admission.AddressLine2,
                            CountryId = admission.CountryId,
                            StateId = admission.StateId,
                            DistrictId = admission.DistrictId,
                            CityId = admission.CityId,
                            Pincode = admission.Pincode,
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        var enrollment = new TbStudentEnrollment
                        {
                            SchoolId = schoolId,
                            StudentId = studentId,
                            AcademicYearId = admission.AcademicYearId,
                            ClassSectionId = req.ClassSectionId,
                            RollNumber = Clean(req.RollNumber),
                            EnrollmentDate = now.Date,
                            AdmissionTypeId = admission.AdmissionTypeId,
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        var guardian = reusedPrimary != null ? null : new TbGuardian
                        {
                            SchoolId = schoolId,
                            FirstName = admission.GuardianName,
                            Relationship = admission.GuardianRelationship,
                            MobileNumber = admission.GuardianMobileNumber,
                            Email = admission.GuardianEmail,
                            WhatsAppNumber = admission.GuardianWhatsAppNumber,
                            ConsentGivenAt = admission.GuardianConsentAt, ConsentRecordedBy = admission.GuardianConsentRecordedBy,
                            ConsentSource = admission.GuardianConsentAt != null && admission.GuardianConsentRecordedBy != null ? GuardianConsentRules.SourceAdmissionForm : null,
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        var link = new TbStudentGuardian
                        {
                            StudentId = studentId,
                            Guardian = guardian,          // a NEW guardian: GuardianId is filled in from the identity value on save (null when an existing guardian is linked instead)
                            GuardianId = reusedPrimary?.GuardianId ?? 0,
                            Relationship = admission.GuardianRelationship,
                            IsPrimary = true,
                            IsEmergencyContact = true,
                            CanPickupStudent = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        };

                        await _repo.AddStudentAsync(student, ct);
                        await _repo.AddEnrollmentAsync(enrollment, ct);
                        if (guardian != null) await _repo.AddGuardianAsync(guardian, ct);
                        await _repo.AddStudentGuardianAsync(link, ct);
                        foreach (var (extra, relationship) in reusedExtras)   // the other parents: linked, never primary
                            await _repo.AddStudentGuardianAsync(new TbStudentGuardian { StudentId = studentId, GuardianId = extra.GuardianId, Relationship = relationship ?? extra.Relationship, IsPrimary = false, IsEmergencyContact = false, CanPickupStudent = true, CreatedAt = now, UpdatedAt = now }, ct);
                        if (guardianChanges.Count > 0) await _repo.AddGuardianChangesAsync(guardianChanges, ct);

                        admission.StudentId = studentId;
                        admission.Enrollment = enrollment;   // EnrollmentId is filled in from the identity value on save
                        admission.StatusId = admittedId.Value;
                        admission.ConfirmedBy = confirmedByUserId;   // every confirmation records who and when (the database requires it)
                        admission.ConfirmedAt = now;
                        admission.UpdatedAt = now;

                        await _repo.SaveChangesAsync(ct);   // one SaveChanges = one transaction for the whole graph
                        await tx.CommitAsync(ct);

                        // Only now that the student is safely saved: a problem assigning the fee structure must never undo or fail the admission
                        var assignedStructure = feeStructure;
                        if (feeStructure != null)
                        {
                            feeWarning = await FeeAutoAssignment.TryAssignAsync(_repo, _billing, req.FeeItems, _logger, schoolId, studentId, admission.AcademicYearId, feeStructure.FeeStructureId, now, ct);
                            if (feeWarning != null) assignedStructure = null;
                        }

                        await NotifyAsync(schoolId, student.Email, admission.GuardianEmail,
                            name => EmailTemplates.StudentIdAssigned(FullName(student), studentId, name),
                            $"student id assigned {studentId}", ct);

                        // The application's documents go to the student. This runs only now that the admission is committed; a failure (or a missing file) is logged and never undoes or fails it.
                        if (_studentDocuments != null)
                        {
                            try { await _studentDocuments.CarryOverFromApplicationAsync(schoolId, admission.AdmissionId, studentId, confirmedByUserId, ct); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Could not carry the application documents over for {StudentId}", studentId); }
                        }

                        var detail = await ToDetailAsync(admission, ct);
                        // The admission is committed. A parent account (or the "child added" message) is made now, best effort: whatever happens is in the result and never fails or undoes the admission.
                        if (_parentAccounts != null)
                        {
                            try
                            {
                                var primaryGuardianId = reusedPrimary?.GuardianId ?? guardian?.GuardianId ?? 0;
                                if (primaryGuardianId > 0) detail.ParentAccount = await _parentAccounts.OnAdmissionConfirmedAsync(schoolId, primaryGuardianId, studentId, confirmedByUserId, ct);
                                foreach (var (extra, _) in reusedExtras.Where(e => e.Guardian.UserId != null))   // another parent who already has an account is told too; extras never get a new account here
                                    await _parentAccounts.OnAdmissionConfirmedAsync(schoolId, extra.GuardianId, studentId, confirmedByUserId, ct);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _logger.LogError("The parent account step after admission {AdmissionId} failed ({Error}); the admission is saved", admission.AdmissionId, ex.GetType().Name);
                                detail.ParentAccount = new ParentAccountResultDto { Outcome = "FAILED", Reason = "The parent account could not be created. The admission itself is saved; use Resend on the Parent details screen to try again.", CanResend = true };
                            }
                        }
                        detail.FeeStructureName = assignedStructure?.StructureName;
                        if (feeWarning != null) detail.Warnings.Add(feeWarning);
                        return Result(200, "Application confirmed. Student and enrollment created.", detail);
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == EnrollmentTargetRules.RollConstraint)
                    {
                        return Result<AdmissionDetailDto>(409, EnrollmentTargetRules.RollTaken(req.RollNumber, "this section", "this year"), null);   // two people took the same roll number at the same moment
                    }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < 2)
                    {
                        _logger.LogWarning(ex, "Student id / admission number collision for school {SchoolId}; retrying.", schoolId);   // tx rolls back on dispose
                    }
                }
                return Result<AdmissionDetailDto>(409, "Could not allocate a student ID — please try again.", null);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Result<AdmissionDetailDto>(409, "Student ID or admission number collision — please try again.", null);   // the transaction rolled back
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, "confirming admission"); }
        }

        /// <summary>The Stage 2 rule: the primary guardian needs a WhatsApp number and an email before the application can be confirmed. Null when both are there.</summary>
        private static string? MissingContactMessage(string? whatsApp, string? email)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(whatsApp)) missing.Add("WhatsApp number");
            if (PhoneNumbers.EmailError(email, "The guardian's") != null) missing.Add("email");
            return missing.Count == 0 ? null
                : $"The guardian's {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} missing. Edit the application to add {(missing.Count == 1 ? "it" : "them")}, then confirm it.";
        }

        // ---------------- helpers ----------------
        private static string FullName(TbStudent s) =>
            string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        // Own email, else the guardian's, else nothing — the shared resolver lives in INotificationService. Never throws.
        private async Task NotifyAsync(string schoolId, string? ownEmail, string? guardianEmail,
            Func<string, (string Subject, string Html)> template, string context, CancellationToken ct)
        {
            try
            {
                var school = await _repo.GetSchoolIdentityAsync(schoolId, ct);
                await _notify.SendAsync(ownEmail, guardianEmail, template(school?.SchoolName ?? "your school"), context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send '{Context}' notification", context);
            }
        }

        private async Task<ApiResponse<AdmissionDetailDto>> TransitionAsync(
            string schoolId, long admissionId, string[] allowedFrom, string toStatus,
            Action<TbAdmission> apply, string successMessage, CancellationToken ct, Func<string, string?>? refusal = null)
        {
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockApplicationAsync(schoolId, admissionId, ct);   // two decisions / withdrawals of one application queue up; the second sees the first one's result
                var admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);

                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                var current = statuses.TryGetValue(admission.StatusId, out var n) ? n : "UNKNOWN";
                if (!allowedFrom.Contains(current))
                    return Result<AdmissionDetailDto>(409, refusal?.Invoke(current) ?? $"Cannot move an application from '{current}' to '{toStatus}'.", null);

                var toId = await _repo.GetAdmissionStatusIdAsync(toStatus, ct);
                if (toId == null) return ConfigError<AdmissionDetailDto>($"Admission status '{toStatus}' missing from lut_status");

                apply(admission);
                admission.StatusId = toId.Value;
                admission.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Result(200, successMessage, await ToDetailAsync(admission, ct));
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, $"moving admission to {toStatus}"); }
        }

        private async Task<AdmissionDetailDto> ToDetailAsync(TbAdmission a, CancellationToken ct)
        {
            var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
            var classes = await _repo.GetClassNamesAsync(a.SchoolId, new[] { a.AppliedClassId }, ct);
            var types = await _repo.GetAdmissionTypeNamesAsync(new[] { a.AdmissionTypeId }, ct);
            var years = await _repo.GetAcademicYearNamesAsync(a.SchoolId, new[] { a.AcademicYearId }, ct);

            var dto = Fill(new AdmissionDetailDto(), a, statuses, classes, types, years);
            dto.ApplicantFirstName = a.ApplicantFirstName;
            dto.ApplicantMiddleName = a.ApplicantMiddleName;
            dto.ApplicantLastName = a.ApplicantLastName;
            dto.DateOfBirth = a.DateOfBirth;
            dto.GenderId = a.GenderId;
            dto.GenderName = a.GenderId.HasValue ? await _repo.GetGenderNameAsync(a.GenderId.Value, ct) : null;
            dto.BloodGroupId = a.BloodGroupId;
            dto.BloodGroupName = a.BloodGroupId.HasValue ? await _repo.GetBloodGroupNameAsync(a.BloodGroupId.Value, ct) : null;
            dto.StudentCategoryId = a.StudentCategoryId;
            dto.StudentCategoryName = a.StudentCategoryId.HasValue ? await _repo.GetStudentCategoryNameAsync(a.StudentCategoryId.Value, ct) : null;
            dto.StudentNationId = a.StudentNationId;
            dto.Email = a.Email;
            dto.MobileNumber = a.MobileNumber;
            dto.AddressLine1 = a.AddressLine1;
            dto.AddressLine2 = a.AddressLine2;
            dto.CountryId = a.CountryId;
            dto.StateId = a.StateId;
            dto.DistrictId = a.DistrictId;
            dto.CityId = a.CityId;
            dto.Pincode = a.Pincode;
            dto.GuardianName = a.GuardianName;
            dto.GuardianRelationship = a.GuardianRelationship;
            dto.GuardianMobileNumber = a.GuardianMobileNumber;
            dto.GuardianEmail = a.GuardianEmail;
            dto.GuardianWhatsAppNumber = a.GuardianWhatsAppNumber;
            dto.GuardianConsentRecorded = a.GuardianConsentAt != null;
            dto.GuardianId = a.GuardianId;
            if (a.StudentId != null) dto.PrimaryGuardianId = await _repo.GetPrimaryGuardianIdOfStudentAsync(a.SchoolId, a.StudentId, ct);   // the confirmed application's parent, for the Parent account card
            var extraRows = await _repo.GetAdmissionGuardiansAsync(a.SchoolId, a.AdmissionId, false, ct);
            var shown = await _repo.GetGuardiansReadOnlyAsync(a.SchoolId, extraRows.Select(r => r.GuardianId).Concat(a.GuardianId is long gid ? new[] { gid } : Array.Empty<long>()).ToList(), ct);
            GuardianPickDto Pick(TbGuardian g, string? relationship) => new() { GuardianId = g.GuardianId, Name = GuardianReuse.Display(g), Relationship = relationship ?? g.Relationship, MaskedMobile = PhoneNumbers.Mask(g.MobileNumber), HasWhatsApp = !string.IsNullOrWhiteSpace(g.WhatsAppNumber), HasEmail = !string.IsNullOrWhiteSpace(g.Email), ConsentGivenAt = g.ConsentGivenAt };
            if (a.GuardianId is long primaryId && shown.FirstOrDefault(g => g.GuardianId == primaryId) is { } primary) dto.ExistingGuardian = Pick(primary, a.GuardianRelationship);
            dto.AdditionalGuardians = extraRows.Select(r => shown.FirstOrDefault(g => g.GuardianId == r.GuardianId) is { } g ? Pick(g, r.Relationship) : null).Where(p => p != null).Select(p => p!).ToList();
            dto.PreviousSchool = a.PreviousSchool;
            dto.ReviewNotes = a.ReviewNotes;
            dto.ReviewedAt = a.ReviewedAt;
            dto.ApprovedAt = a.ApprovedAt;
            dto.ConfirmedAt = a.ConfirmedAt;
            dto.RejectedAt = a.RejectedAt;
            dto.DecisionRemarks = a.DecisionRemarks;
            dto.RejectionReason = a.RejectionReason;
            dto.CreatedAt = a.CreatedAt;
            dto.UpdatedAt = a.UpdatedAt;
            return dto;
        }

        private static T Fill<T>(T dto, TbAdmission a, Dictionary<int, string> statuses,
            Dictionary<long, string> classes, Dictionary<long, string> types, Dictionary<long, string> years) where T : AdmissionResponseDto
        {
            dto.AdmissionId = a.AdmissionId;
            dto.ApplicationNumber = a.ApplicationNumber;
            dto.ApplicantFullName = string.Join(' ', new[] { a.ApplicantFirstName, a.ApplicantMiddleName, a.ApplicantLastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            dto.StatusName = statuses.TryGetValue(a.StatusId, out var s) ? s : "UNKNOWN";
            dto.AadharNumberMasked = Mask(a.AadharNumber);
            dto.ApplicationDate = a.ApplicationDate;
            dto.StudentId = a.StudentId;
            dto.EnrollmentId = a.EnrollmentId;
            dto.AcademicYearId = a.AcademicYearId;
            dto.AcademicYearName = years.GetValueOrDefault(a.AcademicYearId);
            dto.AppliedClassId = a.AppliedClassId;
            dto.AppliedClassName = classes.GetValueOrDefault(a.AppliedClassId);
            dto.AdmissionTypeId = a.AdmissionTypeId;
            dto.AdmissionTypeName = types.GetValueOrDefault(a.AdmissionTypeId);
            return dto;
        }

        private static string? Mask(string? aadhar) =>
            string.IsNullOrEmpty(aadhar) || aadhar.Length < 4 ? aadhar : $"XXXX XXXX {aadhar[^4..]}";

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string? ConstraintOf(DbUpdateException ex) =>
            (ex.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation } pg ? pg.ConstraintName : null;

        private static bool IsUniqueViolation(DbUpdateException ex) => ConstraintOf(ex) != null;

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }

        private ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }
    }
}
