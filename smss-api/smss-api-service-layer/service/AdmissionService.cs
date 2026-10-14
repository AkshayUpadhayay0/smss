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
    // Status-driven workflow: APPLICATION_SUBMITTED -> UNDER_REVIEW -> APPROVED -> ADMITTED, with REJECTED (from review)
    // and CANCELLED (from submitted / under review) as exits. Status names are resolved from lut_status, never ids.
    public class AdmissionService : IAdmissionService
    {
        private const string Submitted = "APPLICATION_SUBMITTED";
        private const string UnderReview = "UNDER_REVIEW";
        private const string Approved = "APPROVED";
        private const string Rejected = "REJECTED";
        private const string Admitted = "ADMITTED";
        private const string Cancelled = "CANCELLED";

        private readonly IAdmissionRepository _repo;
        private readonly INotificationService _notify;
        private readonly ILogger<AdmissionService> _logger;

        public AdmissionService(IAdmissionRepository repo, INotificationService notify, ILogger<AdmissionService> logger)
        {
            _repo = repo;
            _notify = notify;
            _logger = logger;
        }

        // ---------------- reads ----------------
        public async Task<ApiResponse<List<AdmissionResponseDto>>> GetAllAsync(string schoolId, CancellationToken ct)
        {
            try
            {
                var list = await _repo.GetAllAsync(schoolId, ct);
                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                var classes = await _repo.GetClassNamesAsync(schoolId, list.Select(a => a.AppliedClassId).Distinct().ToList(), ct);
                var types = await _repo.GetAdmissionTypeNamesAsync(schoolId, list.Select(a => a.AdmissionTypeId).Distinct().ToList(), ct);
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
        public async Task<ApiResponse<AdmissionDetailDto>> SubmitAsync(string schoolId, SubmitAdmissionRequestDto req, CancellationToken ct)
        {
            try
            {
                // Ids come from the client: every school-owned one must belong to THIS school
                if (!await _repo.AcademicYearBelongsToSchoolAsync(schoolId, req.AcademicYearId, ct))
                    return Result<AdmissionDetailDto>(400, "The selected academic year was not found for your school.", null);
                if (!await _repo.ClassBelongsToSchoolAsync(schoolId, req.AppliedClassId, ct))
                    return Result<AdmissionDetailDto>(400, "The selected class was not found for your school.", null);
                if (!await _repo.AdmissionTypeBelongsToSchoolAsync(schoolId, req.AdmissionTypeId, ct))
                    return Result<AdmissionDetailDto>(400, "The selected admission type was not found for your school.", null);
                if (req.GenderId.HasValue && !await _repo.GenderExistsAsync(req.GenderId.Value, ct))
                    return Result<AdmissionDetailDto>(400, "The selected gender is not valid.", null);
                if (req.BloodGroupId.HasValue && !await _repo.BloodGroupExistsAsync(req.BloodGroupId.Value, ct))
                    return Result<AdmissionDetailDto>(400, "The selected blood group is not valid.", null);
                if (req.StudentCategoryId.HasValue && !await _repo.StudentCategoryExistsAsync(req.StudentCategoryId.Value, ct))
                    return Result<AdmissionDetailDto>(400, "The selected student category is not valid.", null);
                if (req.DateOfBirth.Date >= DateTime.UtcNow.Date)
                    return Result<AdmissionDetailDto>(400, "Date of birth must be in the past.", null);

                var submittedId = await _repo.GetAdmissionStatusIdAsync(Submitted, ct);
                if (submittedId == null) return ConfigError<AdmissionDetailDto>($"Admission status '{Submitted}' missing from lut_status");

                var now = DateTime.UtcNow;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var prefix = $"ADM{now.Year}";
                    var next = await _repo.GetMaxApplicationSequenceAsync(schoolId, prefix, ct) + 1;

                    var entity = new TbAdmission
                    {
                        SchoolId = schoolId,
                        ApplicationNumber = $"{prefix}{next:D4}",
                        AcademicYearId = req.AcademicYearId,
                        AppliedClassId = req.AppliedClassId,
                        AdmissionTypeId = req.AdmissionTypeId,
                        ApplicantFirstName = req.ApplicantFirstName.Trim(),
                        ApplicantMiddleName = Clean(req.ApplicantMiddleName),
                        ApplicantLastName = req.ApplicantLastName.Trim(),
                        DateOfBirth = req.DateOfBirth.Date,
                        GenderId = req.GenderId,
                        BloodGroupId = req.BloodGroupId,
                        StudentCategoryId = req.StudentCategoryId,
                        AadharNumber = Clean(req.AadharNumber),
                        StudentNationId = Clean(req.StudentNationId),
                        Email = Clean(req.Email),
                        MobileNumber = Clean(req.MobileNumber),
                        AddressLine1 = Clean(req.AddressLine1),
                        AddressLine2 = Clean(req.AddressLine2),
                        CountryId = req.CountryId,
                        StateId = req.StateId,
                        DistrictId = req.DistrictId,
                        CityId = req.CityId,
                        Pincode = Clean(req.Pincode),
                        GuardianName = req.GuardianName.Trim(),
                        GuardianRelationship = req.GuardianRelationship,
                        GuardianMobileNumber = req.GuardianMobileNumber,
                        GuardianEmail = Clean(req.GuardianEmail),
                        ApplicationDate = now.Date,
                        StatusId = submittedId.Value,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    try
                    {
                        await _repo.AddAdmissionAsync(entity, ct);
                        await _repo.SaveChangesAsync(ct);
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
                        // two applications took the same number at once: pick the next free one and try again
                        _logger.LogWarning(ex, "Application number collision for school {SchoolId}; retrying.", schoolId);
                    }
                }
                return Result<AdmissionDetailDto>(409, "Could not allocate an application number. Please try again.", null);
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, "submitting admission"); }
        }

        // ---------------- transitions ----------------
        public Task<ApiResponse<AdmissionDetailDto>> StartReviewAsync(string schoolId, long admissionId, long reviewedByUserId, CancellationToken ct) =>
            TransitionAsync(schoolId, admissionId, new[] { Submitted }, UnderReview, a =>
            {
                a.ReviewedBy = reviewedByUserId;
                a.ReviewedAt = DateTime.UtcNow;
            }, "Application moved to review.", ct);

        public async Task<ApiResponse<AdmissionDetailDto>> ApproveAsync(string schoolId, long admissionId, long approvedByUserId, AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            var result = await TransitionAsync(schoolId, admissionId, new[] { UnderReview }, Approved, a =>
            {
                a.ApprovedBy = approvedByUserId;
                a.ApprovedAt = DateTime.UtcNow;
                a.DecisionRemarks = Clean(req.Remarks);
            }, "Application approved.", ct);

            if (result.Status && result.Data is { } d)
            {
                await NotifyAsync(schoolId, d.Email, d.GuardianEmail,
                    name => EmailTemplates.ApplicationApproved(d.ApplicantFullName, d.ApplicationNumber, name),
                    $"application approved {d.ApplicationNumber}", ct);
            }
            return result;
        }

        public Task<ApiResponse<AdmissionDetailDto>> RejectAsync(string schoolId, long admissionId, AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Remarks))
                return Task.FromResult(Result<AdmissionDetailDto>(400, "A reason is required to reject an application.", null));

            return TransitionAsync(schoolId, admissionId, new[] { UnderReview }, Rejected, a => a.RejectionReason = Clean(req.Remarks),
                "Application rejected.", ct);
        }

        public Task<ApiResponse<AdmissionDetailDto>> CancelAsync(string schoolId, long admissionId, AdmissionDecisionRequestDto req, CancellationToken ct) =>
            TransitionAsync(schoolId, admissionId, new[] { Submitted, UnderReview }, Cancelled, a => a.DecisionRemarks = Clean(req.Remarks),
                "Application cancelled.", ct);

        // ---------------- confirm admission ----------------
        // Creates the student, enrollment, guardian and guardian link and marks the application ADMITTED — all in one
        // transaction, so a failure part-way leaves nothing behind.
        public async Task<ApiResponse<AdmissionDetailDto>> ConfirmAdmissionAsync(string schoolId, long admissionId, ConfirmAdmissionRequestDto req, CancellationToken ct)
        {
            try
            {
                var admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);

                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                if (!statuses.TryGetValue(admission.StatusId, out var current) || current != Approved)
                    return Result<AdmissionDetailDto>(409, "Only an APPROVED application can be confirmed for admission.", null);

                if (!await _repo.SectionBelongsToSchoolAsync(schoolId, req.ClassSectionId, ct))
                    return Result<AdmissionDetailDto>(400, "The selected section was not found for your school.", null);

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
                    feeWarning = FeeAssignmentWarning(sectionClass?.ClassName ?? "this class", yearName);
                }

                // The id is known before anything is saved (count + 1), so the whole graph goes in ONE SaveChanges.
                // Two confirmations at the same moment can compute the same id; the primary key rejects the second one
                // and it simply tries again with a fresh count.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0)
                    {
                        _repo.ClearTracking();   // forget the rows of the failed attempt
                        admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                        if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);
                    }

                    await using var tx = await _repo.BeginTransactionAsync(ct);
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
                        var guardian = new TbGuardian
                        {
                            SchoolId = schoolId,
                            FirstName = admission.GuardianName,
                            Relationship = admission.GuardianRelationship,
                            MobileNumber = admission.GuardianMobileNumber,
                            Email = admission.GuardianEmail,
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        var link = new TbStudentGuardian
                        {
                            StudentId = studentId,
                            Guardian = guardian,          // GuardianId is filled in from the identity value on save
                            Relationship = admission.GuardianRelationship,
                            IsPrimary = true,
                            IsEmergencyContact = true,
                            CanPickupStudent = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        };

                        await _repo.AddStudentAsync(student, ct);
                        await _repo.AddEnrollmentAsync(enrollment, ct);
                        await _repo.AddGuardianAsync(guardian, ct);
                        await _repo.AddStudentGuardianAsync(link, ct);
                        if (feeStructure != null)
                        {
                            await _repo.AddStudentFeeStructureAsync(new TbStudentFeeStructure
                            {
                                StudentId = studentId,
                                FeeStructureId = feeStructure.FeeStructureId,
                                SchoolId = schoolId,
                                AcademicYearId = admission.AcademicYearId,
                                CreatedAt = now,
                                UpdatedAt = now
                            }, ct);
                        }

                        admission.StudentId = studentId;
                        admission.Enrollment = enrollment;   // EnrollmentId is filled in from the identity value on save
                        admission.StatusId = admittedId.Value;
                        admission.UpdatedAt = now;

                        await _repo.SaveChangesAsync(ct);   // one SaveChanges = one transaction for the whole graph
                        await tx.CommitAsync(ct);

                        await NotifyAsync(schoolId, student.Email, admission.GuardianEmail,
                            name => EmailTemplates.StudentIdAssigned(FullName(student), studentId, name),
                            $"student id assigned {studentId}", ct);

                        var detail = await ToDetailAsync(admission, ct);
                        detail.FeeStructureName = feeStructure?.StructureName;
                        if (feeWarning != null) detail.Warnings.Add(feeWarning);
                        return Result(200, "Admission confirmed. Student and enrollment created.", detail);
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

        // ---------------- helpers ----------------
        private static string FeeAssignmentWarning(string className, string yearName) =>
            $"No fee structure found for {className}, AY {yearName} — assign manually from the student's Fees tab.";

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
            Action<TbAdmission> apply, string successMessage, CancellationToken ct)
        {
            try
            {
                var admission = await _repo.GetByIdAsync(schoolId, admissionId, true, ct);
                if (admission == null) return Result<AdmissionDetailDto>(404, "Admission application not found.", null);

                var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
                var current = statuses.TryGetValue(admission.StatusId, out var n) ? n : "UNKNOWN";
                if (!allowedFrom.Contains(current))
                    return Result<AdmissionDetailDto>(409, $"Cannot move an application from '{current}' to '{toStatus}'.", null);

                var toId = await _repo.GetAdmissionStatusIdAsync(toStatus, ct);
                if (toId == null) return ConfigError<AdmissionDetailDto>($"Admission status '{toStatus}' missing from lut_status");

                apply(admission);
                admission.StatusId = toId.Value;
                admission.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);

                return Result(200, successMessage, await ToDetailAsync(admission, ct));
            }
            catch (Exception ex) { return Error<AdmissionDetailDto>(ex, $"moving admission to {toStatus}"); }
        }

        private async Task<AdmissionDetailDto> ToDetailAsync(TbAdmission a, CancellationToken ct)
        {
            var statuses = await _repo.GetAdmissionStatusNamesAsync(ct);
            var classes = await _repo.GetClassNamesAsync(a.SchoolId, new[] { a.AppliedClassId }, ct);
            var types = await _repo.GetAdmissionTypeNamesAsync(a.SchoolId, new[] { a.AdmissionTypeId }, ct);
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
            dto.ReviewedAt = a.ReviewedAt;
            dto.ApprovedAt = a.ApprovedAt;
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
