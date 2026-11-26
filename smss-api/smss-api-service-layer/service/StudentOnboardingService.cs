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
    // Writes a student, their enrollment and guardian straight in — the same rows ConfirmAdmissionAsync creates,
    // minus the admission record. Never touches tb_admissions.
    public class StudentOnboardingService : IStudentOnboardingService
    {
        // The global Admission Type (lut_admission_type, managed by the Super Admin) used for these students; found by name.
        public const string ExistingStudentTypeName = "Existing Student";

        private const string AdmissionNumberConstraint = "uq_tb_students_school_admno";
        private const string AadharConstraint = "uq_tb_students_school_aadhar";
        private const string StudentIdConstraint = "tb_students_pkey";

        private readonly IAdmissionRepository _repo;
        private readonly INotificationService _notify;
        private readonly IStudentFeeBillingService _billing;
        private readonly ILogger<StudentOnboardingService> _logger;

        public StudentOnboardingService(IAdmissionRepository repo, INotificationService notify, IStudentFeeBillingService billing, ILogger<StudentOnboardingService> logger)
        {
            _repo = repo;
            _notify = notify;
            _billing = billing;
            _logger = logger;
        }

        public async Task<ApiResponse<StudentOnboardingResponseDto>> CreateDirectEntryAsync(string schoolId, DirectStudentEntryRequestDto req, CancellationToken ct, long userId = 0)
        {
            try
            {
                // ---- the Existing Student admission type must already exist (a global master; never auto-created here) ----
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError("'Active' general status missing from lut_status");

                // ALWAYS Existing Student, found by code. The request has no admission type field, so nothing a client sends can change it.
                var (type, typeError) = await AdmissionTypeDefaults.ResolveAsync(_repo.GetAdmissionTypeByCodeAsync, AdmissionTypeCodes.ExistingStudent, ct);
                if (type == null) return Result(400, typeError!, null);

                // ---- ids come from the client: school-owned ones must belong to THIS school ----
                if (!await _repo.AcademicYearBelongsToSchoolAsync(schoolId, req.AcademicYearId, ct))
                    return Result(400, "The selected academic year was not found for your school.", null);
                if (await _repo.IsYearClosedAsync(schoolId, req.AcademicYearId, ct)) return Result(409, ClosedYear.Message, null);
                if (!await _repo.SectionBelongsToSchoolAsync(schoolId, req.ClassSectionId, ct))
                    return Result(400, "The selected class and section was not found for your school.", null);
                // the primary guardian's WhatsApp number and email are mandatory (the controller's model validation says the same; this keeps every caller honest)
                if (req.ExistingGuardianId == null)   // (an EXISTING primary guardian is checked against the guardian record, inside the transaction)
                {
                    var contact = PhoneNumbers.WhatsAppError(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, req.GuardianMobileNumber, "The guardian's")
                                  ?? PhoneNumbers.EmailError(req.GuardianEmail, "The guardian's");
                    if (contact != null) return Result(400, contact, null);
                    if (!req.GuardianConsent) return Result(400, GuardianConsentRules.Required, null);
                    if (string.IsNullOrWhiteSpace(req.GuardianName) || string.IsNullOrWhiteSpace(req.GuardianRelationship) || string.IsNullOrWhiteSpace(req.GuardianMobileNumber))
                        return Result(400, "The guardian's name, relationship and mobile number are required.", null);
                }
                var target = EnrollmentTargetRules.Refusal(await _repo.CheckEnrollmentTargetAsync(schoolId, req.AcademicYearId, req.ClassSectionId, Clean(req.RollNumber), ct), Clean(req.RollNumber));
                if (target is { } refused) return Result(refused.Code, refused.Message, null);
                // "Charge fees from": checked BEFORE the student is saved, so a refusal creates nothing
                if (req.ChargeFrom is DateOnly chargeFrom)
                {
                    var chargeError = await _billing.CheckChargeFromAsync(schoolId, req.AcademicYearId, chargeFrom, req.ConfirmBackdating, req.BackdatingReason, ct);
                    if (chargeError != null) return Result(400, chargeError, null);
                }
                if (req.GenderId.HasValue && !await _repo.GenderExistsAsync(req.GenderId.Value, ct))
                    return Result(400, "The selected gender is not valid.", null);
                if (req.BloodGroupId.HasValue && !await _repo.BloodGroupExistsAsync(req.BloodGroupId.Value, ct))
                    return Result(400, "The selected blood group is not valid.", null);
                if (req.StudentCategoryId.HasValue && !await _repo.StudentCategoryExistsAsync(req.StudentCategoryId.Value, ct))
                    return Result(400, "The selected student category is not valid.", null);

                var today = DateTime.UtcNow.Date;
                if (req.DateOfBirth.Date >= today) return Result(400, "Date of birth must be in the past.", null);
                if (req.AdmissionDate.Date > today) return Result(400, "Admission date cannot be in the future.", null);
                if (req.AdmissionDate.Date < req.DateOfBirth.Date) return Result(400, "Admission date cannot be before the date of birth.", null);

                // The student id ({short code}ST{nnn}) is built from the school's short code, so the school must have one
                var school = await _repo.GetSchoolIdentityAsync(schoolId, ct);
                if (school == null) return Result(404, "School not found.", null);
                if (string.IsNullOrWhiteSpace(school.ShortCode)) return Result(409, StudentIdHelper.NoShortCodeMessage, null);

                // Fee structure for this class + academic year (automatic assignment). Not finding one never blocks onboarding.
                var sectionClass = await _repo.GetSectionClassAsync(schoolId, req.ClassSectionId, ct);
                var feeStructure = sectionClass == null
                    ? null
                    : await _repo.FindActiveFeeStructureAsync(schoolId, sectionClass.ClassId, req.AcademicYearId, ct);
                string? feeWarning = null;
                if (feeStructure == null)
                {
                    var yearName = (await _repo.GetAcademicYearNamesAsync(schoolId, new[] { req.AcademicYearId }, ct)).GetValueOrDefault(req.AcademicYearId, "this academic year");
                    feeWarning = FeeAutoAssignment.NoStructureWarning(sectionClass?.ClassName ?? "this class", yearName);
                }

                var suppliedNumber = Clean(req.AdmissionNumber);

                // A generated admission number or student id can collide with a concurrent request: take the next free
                // one and retry. A number the school typed in is never silently changed.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    _repo.ClearTracking();   // nothing from a failed attempt may be saved again
                    await using var tx = await _repo.BeginTransactionAsync(ct);

                    // A sibling's parents are LINKED, never copied: a merged guardian is replaced by its survivor, an inactive one is refused, and the rows are locked FOR UPDATE in this transaction.
                    GuardianReuseResult? reuse = null;
                    var guardianChanges = new List<TbGuardianChange>();
                    var picks = (req.AdditionalGuardians ?? new()).Select(e => new GuardianPick(e.GuardianId, e.Relationship)).ToList();
                    if (req.ExistingGuardianId != null || picks.Count > 0)
                    {
                        reuse = await GuardianReuse.LoadAsync(_repo, schoolId, req.ExistingGuardianId, picks, activeId.Value, true, "saved", ct);
                        if (reuse.Error is { } reuseError) return Result(reuseError.Code, reuseError.Message, null);
                        if (reuse.Primary is { } existing)
                        {
                            // WhatsApp and email are needed only where the guardian record has none; what the admin entered is written to the shared record and logged
                            var wa = string.IsNullOrWhiteSpace(existing.WhatsAppNumber) ? PhoneNumbers.WhatsAppFor(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, existing.MobileNumber) : existing.WhatsAppNumber;
                            var em = string.IsNullOrWhiteSpace(existing.Email) ? Clean(req.GuardianEmail) : existing.Email;
                            var bad = (string.IsNullOrWhiteSpace(existing.WhatsAppNumber) ? PhoneNumbers.WhatsAppError(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, existing.MobileNumber, "The guardian's") : null)
                                      ?? (string.IsNullOrWhiteSpace(existing.Email) ? PhoneNumbers.EmailError(req.GuardianEmail, "The guardian's") : null);
                            if (bad != null) return Result(400, bad, null);
                            if (existing.ConsentGivenAt == null && !req.GuardianConsent) return Result(400, GuardianConsentRules.Required, null);   // consent already on record needs no new tick
                            guardianChanges.AddRange(GuardianReuse.ApplyContact(existing, wa, em, userId, schoolId, DateTime.UtcNow));
                            if (existing.ConsentGivenAt == null && userId > 0) { existing.ConsentGivenAt = DateTime.UtcNow; existing.ConsentRecordedBy = userId; existing.ConsentSource = GuardianConsentRules.SourceAdmissionForm; }
                        }
                    }
                    try
                    {
                        var now = DateTime.UtcNow;
                        var number = suppliedNumber ?? await NextAdmissionNumberAsync(schoolId, req.AdmissionDate.Year, ct);
                        var studentId = StudentIdHelper.Build(school.ShortCode!, await _repo.CountStudentsAsync(schoolId, ct) + 1);

                        var graph = StudentOnboardingEntities.Build(new OnboardingInput
                        {
                            SchoolId = schoolId,
                            StudentId = studentId,
                            AdmissionNumber = number,
                            FirstName = req.FirstName.Trim(),
                            MiddleName = Clean(req.MiddleName),
                            LastName = Clean(req.LastName),   // optional: a blank is stored as null
                            DateOfBirth = req.DateOfBirth,
                            GenderId = req.GenderId,
                            BloodGroupId = req.BloodGroupId,
                            StudentCategoryId = req.StudentCategoryId,
                            AadharNumber = Clean(req.AadharNumber),
                            AdmissionTypeId = type.AdmissionTypeId,
                            AdmissionDate = req.AdmissionDate,
                            Email = Clean(req.Email),
                            MobileNumber = Clean(req.MobileNumber),
                            ActiveStatusId = activeId.Value,
                            AcademicYearId = req.AcademicYearId,
                            ClassSectionId = req.ClassSectionId,
                            RollNumber = Clean(req.RollNumber),
                            Guardians = reuse?.Primary != null ? Array.Empty<OnboardingGuardianInput>() : new[] { new OnboardingGuardianInput { Name = req.GuardianName, Relationship = req.GuardianRelationship, Mobile = req.GuardianMobileNumber, WhatsApp = PhoneNumbers.WhatsAppFor(req.GuardianWhatsAppNumber, req.GuardianWhatsAppSameAsMobile, req.GuardianMobileNumber), Email = Clean(req.GuardianEmail) } },
                            Now = now
                        });
                        var student = graph.Student;
                        var enrollment = graph.Enrollment;
                        var guardian = graph.Guardians.FirstOrDefault();   // null when the primary guardian is an existing one
                        if (guardian != null && req.GuardianConsent && userId > 0) { guardian.ConsentGivenAt = now; guardian.ConsentRecordedBy = userId; guardian.ConsentSource = GuardianConsentRules.SourceAdmissionForm; }

                        await _repo.AddStudentAsync(student, ct);
                        await _repo.AddEnrollmentAsync(enrollment, ct);
                        if (guardian != null)
                        {
                            await _repo.AddGuardianAsync(guardian, ct);
                            await _repo.AddStudentGuardianAsync(graph.Links[0], ct);
                        }
                        if (reuse?.Primary is { } primaryGuardian)
                            await _repo.AddStudentGuardianAsync(new TbStudentGuardian { StudentId = studentId, GuardianId = primaryGuardian.GuardianId, Relationship = req.GuardianRelationship is "FATHER" or "MOTHER" or "GUARDIAN" ? req.GuardianRelationship : primaryGuardian.Relationship, IsPrimary = true, IsEmergencyContact = true, CanPickupStudent = true, CreatedAt = now, UpdatedAt = now }, ct);
                        foreach (var (extra, relationship) in reuse?.Extras ?? new())
                            await _repo.AddStudentGuardianAsync(new TbStudentGuardian { StudentId = studentId, GuardianId = extra.GuardianId, Relationship = relationship ?? extra.Relationship, IsPrimary = false, IsEmergencyContact = false, CanPickupStudent = true, CreatedAt = now, UpdatedAt = now }, ct);
                        if (guardianChanges.Count > 0) await _repo.AddGuardianChangesAsync(guardianChanges, ct);

                        await _repo.SaveChangesAsync(ct);   // one SaveChanges = one transaction for the whole graph
                        await tx.CommitAsync(ct);

                        // Only now that the student is safely saved: a problem assigning the fee structure must never undo or fail it
                        var assignedStructure = feeStructure;
                        if (feeStructure != null)
                        {
                            var syncOptions = req.ChargeFrom is DateOnly cf
                                ? new FeeSyncOptions { ChargeFrom = cf, Backdate = cf < SchoolClock.Today(), BackdatingReason = req.BackdatingReason?.Trim(), UserId = userId }
                                : null;
                            feeWarning = await FeeAutoAssignment.TryAssignAsync(_repo, _billing, req.FeeItems, _logger, schoolId, studentId, req.AcademicYearId, feeStructure.FeeStructureId, now, ct, syncOptions);
                            if (feeWarning != null) assignedStructure = null;
                        }

                        var fullName = string.Join(' ', new[] { student.FirstName, student.MiddleName, student.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        await _notify.SendAsync(student.Email, guardian?.Email ?? reuse?.Primary?.Email,
                            EmailTemplates.StudentIdAssigned(fullName, studentId, school.SchoolName),
                            $"student id assigned {studentId}", ct);

                        return Result(201, "Student onboarded successfully.", new StudentOnboardingResponseDto
                        {
                            StudentId = student.StudentId,
                            AdmissionNumber = student.AdmissionNumber,
                            FullName = fullName,
                            EnrollmentId = enrollment.EnrollmentId,
                            AadharNumberMasked = Mask(student.AadharNumber),
                            FeeStructureName = assignedStructure?.StructureName,
                            Warnings = feeWarning == null ? new List<string>() : new List<string> { feeWarning }
                        });
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == EnrollmentTargetRules.RollConstraint)
                    {
                        return Result(409, EnrollmentTargetRules.RollTaken(req.RollNumber, "this section", "this year"), null);   // two people took the same roll number at the same moment
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == AadharConstraint)
                    {
                        return Result(409, "A student with this Aadhaar number already exists in this school.", null);
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == StudentIdConstraint)
                    {
                        _logger.LogWarning(ex, "Generated student id collided for school {SchoolId}; retrying.", schoolId);   // tx rolls back on dispose
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == AdmissionNumberConstraint)
                    {
                        if (suppliedNumber != null)
                            return Result(409, $"Admission number '{suppliedNumber}' is already used by another student in this school.", null);
                        _logger.LogWarning(ex, "Generated admission number collided for school {SchoolId}; retrying.", schoolId);   // tx rolls back on dispose
                    }
                }
                return Result(409, "Could not allocate a student ID or admission number. Please try again.", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while onboarding existing student");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        // ADMNO{year}{4-digit seq}: the same pattern, and the same per-school running number, as Confirm Admission
        private async Task<string> NextAdmissionNumberAsync(string schoolId, int year, CancellationToken ct)
        {
            var prefix = $"ADMNO{year}";
            var next = await _repo.GetMaxAdmissionNumberSequenceAsync(schoolId, prefix, ct) + 1;
            return $"{prefix}{next:D4}";
        }

        private static string? Mask(string? aadhar) =>
            string.IsNullOrEmpty(aadhar) || aadhar.Length < 4 ? aadhar : $"XXXX XXXX {aadhar[^4..]}";

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string? ConstraintOf(DbUpdateException ex) =>
            (ex.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation } pg ? pg.ConstraintName : null;

        private static ApiResponse<StudentOnboardingResponseDto> Result(int code, string message, StudentOnboardingResponseDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<StudentOnboardingResponseDto> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result(500, "The system is not configured correctly. Please contact support.", null);
        }
    }
}
