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
        // The school's own Admission Type (managed on its Admission Type master screen) used for these students.
        // tb_admission_types has no code column, so it is found by name.
        public const string ExistingStudentTypeName = "Existing Student";

        private const string AdmissionNumberConstraint = "uq_tb_students_school_admno";
        private const string AadharConstraint = "uq_tb_students_school_aadhar";

        private readonly IAdmissionRepository _repo;
        private readonly ILogger<StudentOnboardingService> _logger;

        public StudentOnboardingService(IAdmissionRepository repo, ILogger<StudentOnboardingService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<StudentOnboardingResponseDto>> CreateDirectEntryAsync(string schoolId, DirectStudentEntryRequestDto req, CancellationToken ct)
        {
            try
            {
                // ---- the Existing Student admission type must already be set up (never auto-created here) ----
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError("'Active' general status missing from lut_status");

                var type = await _repo.GetAdmissionTypeByNameAsync(schoolId, ExistingStudentTypeName, ct);
                if (type == null)
                    return Result(400, $"Add an '{ExistingStudentTypeName}' Admission Type before onboarding existing students.", null);
                if (type.StatusId != activeId)
                    return Result(400, $"The '{ExistingStudentTypeName}' Admission Type is inactive. Activate it before onboarding existing students.", null);

                // ---- ids come from the client: school-owned ones must belong to THIS school ----
                if (!await _repo.AcademicYearBelongsToSchoolAsync(schoolId, req.AcademicYearId, ct))
                    return Result(400, "The selected academic year was not found for your school.", null);
                if (!await _repo.SectionBelongsToSchoolAsync(schoolId, req.ClassSectionId, ct))
                    return Result(400, "The selected class and section was not found for your school.", null);
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

                var suppliedNumber = Clean(req.AdmissionNumber);

                // A generated number can collide with a concurrent request: take the next free one and retry.
                // A number the school typed in is never silently changed.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    try
                    {
                        var now = DateTime.UtcNow;
                        var number = suppliedNumber ?? await NextAdmissionNumberAsync(schoolId, req.AdmissionDate.Year, ct);

                        var student = new TbStudent
                        {
                            SchoolId = schoolId,
                            AdmissionNumber = number,
                            FirstName = req.FirstName.Trim(),
                            MiddleName = Clean(req.MiddleName),
                            LastName = req.LastName.Trim(),
                            DateOfBirth = req.DateOfBirth.Date,
                            GenderId = req.GenderId,
                            BloodGroupId = req.BloodGroupId,
                            StudentCategoryId = req.StudentCategoryId,
                            AadharNumber = Clean(req.AadharNumber),
                            AdmissionTypeId = type.AdmissionTypeId,
                            AdmissionDate = req.AdmissionDate.Date,   // historical, not "now"
                            Email = Clean(req.Email),
                            MobileNumber = Clean(req.MobileNumber),
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        await _repo.AddStudentAsync(student, ct);
                        await _repo.SaveChangesAsync(ct);   // need student.StudentId for the dependent rows

                        var enrollment = new TbStudentEnrollment
                        {
                            SchoolId = schoolId,
                            StudentId = student.StudentId,
                            AcademicYearId = req.AcademicYearId,
                            ClassSectionId = req.ClassSectionId,
                            RollNumber = Clean(req.RollNumber),
                            EnrollmentDate = req.AdmissionDate.Date,
                            AdmissionTypeId = type.AdmissionTypeId,
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        var guardian = new TbGuardian
                        {
                            SchoolId = schoolId,
                            FirstName = req.GuardianName.Trim(),
                            Relationship = req.GuardianRelationship,
                            MobileNumber = req.GuardianMobileNumber,
                            Email = Clean(req.GuardianEmail),
                            StatusId = activeId.Value,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        await _repo.AddEnrollmentAsync(enrollment, ct);
                        await _repo.AddGuardianAsync(guardian, ct);
                        await _repo.SaveChangesAsync(ct);   // need enrollment.EnrollmentId and guardian.GuardianId

                        await _repo.AddStudentGuardianAsync(new TbStudentGuardian
                        {
                            StudentId = student.StudentId,
                            GuardianId = guardian.GuardianId,
                            Relationship = req.GuardianRelationship,
                            IsPrimary = true,
                            IsEmergencyContact = true,
                            CanPickupStudent = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        }, ct);
                        await _repo.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);

                        return Result(201, "Student onboarded successfully.", new StudentOnboardingResponseDto
                        {
                            StudentId = student.StudentId,
                            AdmissionNumber = student.AdmissionNumber,
                            FullName = string.Join(' ', new[] { student.FirstName, student.MiddleName, student.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))),
                            EnrollmentId = enrollment.EnrollmentId,
                            AadharNumberMasked = Mask(student.AadharNumber)
                        });
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == AadharConstraint)
                    {
                        return Result(409, "A student with this Aadhaar number already exists in this school.", null);
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == AdmissionNumberConstraint)
                    {
                        if (suppliedNumber != null)
                            return Result(409, $"Admission number '{suppliedNumber}' is already used by another student in this school.", null);
                        _logger.LogWarning(ex, "Generated admission number collided for school {SchoolId}; retrying.", schoolId);   // tx rolls back on dispose
                    }
                }
                return Result(409, "Could not allocate an admission number. Please try again.", null);
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
