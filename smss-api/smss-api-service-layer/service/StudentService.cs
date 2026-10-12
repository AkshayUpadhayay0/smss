using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentService : IStudentService
    {
        private readonly IStudentRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly ILogger<StudentService> _logger;

        public StudentService(IStudentRepository repo, IFileStorageService storage, ILogger<StudentService> logger)
        {
            _repo = repo;
            _storage = storage;
            _logger = logger;
        }

        public async Task<ApiResponse<List<StudentListItemDto>>> GetStudentsAsync(string schoolId, long academicYearId, long classSectionId, CancellationToken ct)
        {
            try
            {
                // ids come from the query string: they must belong to THIS school
                if (!await _repo.AcademicYearBelongsToSchoolAsync(schoolId, academicYearId, ct))
                    return Result<List<StudentListItemDto>>(400, "The selected academic year was not found for your school.", null);
                if (!await _repo.SectionBelongsToSchoolAsync(schoolId, classSectionId, ct))
                    return Result<List<StudentListItemDto>>(400, "The selected section was not found for your school.", null);

                var rows = await _repo.GetEnrolledStudentsAsync(schoolId, academicYearId, classSectionId, ct);
                var statuses = await _repo.GetGeneralStatusNamesAsync(ct);
                var genders = await _repo.GetGenderNamesAsync(ct);

                var items = rows
                    .Select(r => new StudentListItemDto
                    {
                        StudentId = r.StudentId,
                        AdmissionNumber = r.AdmissionNumber,
                        FullName = FullName(r.FirstName, r.MiddleName, r.LastName),
                        RollNumber = r.RollNumber,
                        GenderName = r.GenderId.HasValue ? genders.GetValueOrDefault(r.GenderId.Value) : null,
                        StatusName = statuses.GetValueOrDefault(r.StatusId, "Unknown"),
                        EnrollmentId = r.EnrollmentId,
                        ProfilePhotoUrl = r.ProfilePhotoUrl
                    })
                    .OrderBy(i => i.RollNumber, RollNumberComparer.Instance)
                    .ThenBy(i => i.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Result(200, "Successfully fetched", items);
            }
            catch (Exception ex) { return Error<List<StudentListItemDto>>(ex, "fetching students"); }
        }

        public async Task<ApiResponse<StudentProfileDto>> GetStudentProfileAsync(string schoolId, long studentId, CancellationToken ct)
        {
            try
            {
                // 404 for another school's student too, so ids from other schools can't be probed
                var s = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (s == null) return Result<StudentProfileDto>(404, "Student not found.", null);

                var statuses = await _repo.GetGeneralStatusNamesAsync(ct);
                var genders = await _repo.GetGenderNamesAsync(ct);
                var enrollments = await _repo.GetEnrollmentsAsync(schoolId, studentId, ct);
                var guardians = await _repo.GetGuardiansAsync(schoolId, studentId, ct);

                var dto = new StudentProfileDto
                {
                    StudentId = s.StudentId,
                    AdmissionNumber = s.AdmissionNumber,
                    FullName = FullName(s.FirstName, s.MiddleName, s.LastName),
                    DateOfBirth = s.DateOfBirth,
                    GenderName = s.GenderId.HasValue ? genders.GetValueOrDefault(s.GenderId.Value) : null,
                    BloodGroupName = s.BloodGroupId.HasValue ? await _repo.GetBloodGroupNameAsync(s.BloodGroupId.Value, ct) : null,
                    StudentCategoryName = s.StudentCategoryId.HasValue ? await _repo.GetStudentCategoryNameAsync(s.StudentCategoryId.Value, ct) : null,
                    AdmissionTypeName = await _repo.GetAdmissionTypeNameAsync(schoolId, s.AdmissionTypeId, ct),
                    AdmissionDate = s.AdmissionDate,
                    Email = s.Email,
                    MobileNumber = s.MobileNumber,
                    StatusName = statuses.GetValueOrDefault(s.StatusId, "Unknown"),
                    AadharNumberMasked = Mask(s.AadharNumber),
                    ProfilePhotoUrl = s.ProfilePhotoUrl,
                    Guardians = guardians
                        .OrderByDescending(g => g.IsPrimary).ThenBy(g => g.FirstName)
                        .Select(g => new GuardianSummaryDto
                        {
                            GuardianId = g.GuardianId,
                            FullName = FullName(g.FirstName, null, g.LastName),
                            Relationship = g.Relationship,
                            MobileNumber = g.MobileNumber,
                            Email = g.Email,
                            IsPrimary = g.IsPrimary,
                            IsEmergencyContact = g.IsEmergencyContact,
                            CanPickupStudent = g.CanPickupStudent
                        }).ToList()
                };

                // Current enrollment. A student can later have several (one per academic year, e.g. after a promotion):
                // prefer the one in the school's current academic year, then an Active one, then the most recent year.
                var activeId = statuses.FirstOrDefault(kv => kv.Value == "Active").Key;
                var current = enrollments
                    .OrderByDescending(e => e.AcademicYearIsCurrent)
                    .ThenByDescending(e => e.StatusId == activeId)
                    .ThenByDescending(e => e.AcademicYearStart)
                    .ThenByDescending(e => e.EnrollmentId)
                    .FirstOrDefault();
                if (current != null)
                {
                    dto.EnrollmentId = current.EnrollmentId;
                    dto.AcademicYearName = current.AcademicYearName;
                    dto.ClassName = current.ClassName;
                    dto.SectionName = current.SectionName;
                    dto.RollNumber = current.RollNumber;
                    dto.EnrollmentStatusName = statuses.GetValueOrDefault(current.StatusId, "Unknown");
                }

                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<StudentProfileDto>(ex, "fetching student profile"); }
        }

        // ---------------- photo ----------------
        // Same rules and the same storage service as the school logo (ImageUploadValidator / IFileStorageService).
        public async Task<ApiResponse<StudentPhotoDto>> UploadPhotoAsync(string schoolId, long studentId, Stream content, string fileName, long length, CancellationToken ct)
        {
            string? savedUrl = null;
            var committed = false;
            try
            {
                var (image, error) = await ImageUploadValidator.ValidateAsync(content, fileName, length, "Photo", ct);
                if (image == null) return Result<StudentPhotoDto>(400, error!, null);
                using var _ = image;

                // 404 for another school's student too, so ids from other schools can't be probed
                var student = await _repo.GetStudentForUpdateAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentPhotoDto>(404, "Student not found.", null);

                var schoolCode = await _repo.GetSchoolCodeAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(schoolCode))
                    return Result<StudentPhotoDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedUrl = await _storage.SaveStudentPhotoAsync(schoolCode!, studentId, image.Content, image.Extension, ct);

                var oldUrl = student.ProfilePhotoUrl;
                student.ProfilePhotoUrl = savedUrl;
                student.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                committed = true;

                if (!string.IsNullOrEmpty(oldUrl)) _storage.Delete(oldUrl);   // replaced: remove the previous file

                return Result(200, "Photo uploaded successfully.", new StudentPhotoDto { StudentId = studentId, ProfilePhotoUrl = savedUrl });
            }
            catch (Exception ex)
            {
                if (savedUrl != null && !committed) _storage.Delete(savedUrl);   // no orphan file if the DB save failed
                return Error<StudentPhotoDto>(ex, "uploading student photo");
            }
        }

        public async Task<ApiResponse<StudentPhotoDto>> RemovePhotoAsync(string schoolId, long studentId, CancellationToken ct)
        {
            try
            {
                var student = await _repo.GetStudentForUpdateAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentPhotoDto>(404, "Student not found.", null);

                var oldUrl = student.ProfilePhotoUrl;
                if (string.IsNullOrEmpty(oldUrl))
                    return Result(200, "Student has no photo.", new StudentPhotoDto { StudentId = studentId, ProfilePhotoUrl = null });

                student.ProfilePhotoUrl = null;
                student.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);

                _storage.Delete(oldUrl);
                return Result(200, "Photo removed successfully.", new StudentPhotoDto { StudentId = studentId, ProfilePhotoUrl = null });
            }
            catch (Exception ex) { return Error<StudentPhotoDto>(ex, "removing student photo"); }
        }

        // ---------------- helpers ----------------
        private static string FullName(string first, string? middle, string? last) =>
            string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        // Same masking as AdmissionService / StudentOnboardingService
        private static string? Mask(string? aadhar) =>
            string.IsNullOrEmpty(aadhar) || aadhar.Length < 4 ? aadhar : $"XXXX XXXX {aadhar[^4..]}";

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }

        // Numeric roll numbers sort as numbers (2 before 10), others as text, blanks last
        private sealed class RollNumberComparer : IComparer<string?>
        {
            public static readonly RollNumberComparer Instance = new();

            public int Compare(string? a, string? b)
            {
                var aBlank = string.IsNullOrWhiteSpace(a);
                var bBlank = string.IsNullOrWhiteSpace(b);
                if (aBlank || bBlank) return aBlank == bBlank ? 0 : aBlank ? 1 : -1;

                var aNum = long.TryParse(a, out var an);
                var bNum = long.TryParse(b, out var bn);
                if (aNum && bNum) return an.CompareTo(bn);
                if (aNum != bNum) return aNum ? -1 : 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
