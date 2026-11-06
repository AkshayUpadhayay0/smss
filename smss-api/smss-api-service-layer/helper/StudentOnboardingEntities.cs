using smss_api_db_layer.entity;

namespace smss_api_service_layer.helper
{
    /// <summary>One guardian of a student being onboarded. The first one in the list is the primary guardian.</summary>
    public sealed class OnboardingGuardianInput
    {
        public string Name { get; init; } = null!;
        public string Relationship { get; init; } = null!;     // FATHER | MOTHER | GUARDIAN
        public string Mobile { get; init; } = null!;
        public string? Email { get; init; }
    }

    /// <summary>Everything the rows of one onboarded student are built from. Strings are already trimmed / null-for-blank.</summary>
    public sealed class OnboardingInput
    {
        public string SchoolId { get; init; } = null!;
        public string StudentId { get; init; } = null!;
        public string AdmissionNumber { get; init; } = null!;
        public string FirstName { get; init; } = null!;
        public string? MiddleName { get; init; }
        public string LastName { get; init; } = null!;
        public DateTime DateOfBirth { get; init; }
        public int? GenderId { get; init; }
        public int? BloodGroupId { get; init; }
        public int? StudentCategoryId { get; init; }
        public string? AadharNumber { get; init; }
        public long AdmissionTypeId { get; init; }
        public DateTime AdmissionDate { get; init; }
        public string? Email { get; init; }
        public string? MobileNumber { get; init; }
        public string? AddressLine1 { get; init; }
        public int ActiveStatusId { get; init; }
        public long AcademicYearId { get; init; }
        public long ClassSectionId { get; init; }
        public string? RollNumber { get; init; }
        public IReadOnlyList<OnboardingGuardianInput> Guardians { get; init; } = Array.Empty<OnboardingGuardianInput>();
        public DateTime Now { get; init; }
    }

    public sealed class OnboardingGraph
    {
        public TbStudent Student { get; init; } = null!;
        public TbStudentEnrollment Enrollment { get; init; } = null!;
        public List<TbGuardian> Guardians { get; init; } = new();
        public List<TbStudentGuardian> Links { get; init; } = new();
    }

    /// <summary>
    /// The one place that turns an onboarding input into a student, an enrollment, guardians and guardian links.
    /// Used by the single-student direct entry AND the Excel import, so the two can never drift apart.
    /// </summary>
    public static class StudentOnboardingEntities
    {
        public static OnboardingGraph Build(OnboardingInput i)
        {
            var student = new TbStudent
            {
                StudentId = i.StudentId,
                SchoolId = i.SchoolId,
                AdmissionNumber = i.AdmissionNumber,
                FirstName = i.FirstName,
                MiddleName = i.MiddleName,
                LastName = i.LastName,
                DateOfBirth = i.DateOfBirth.Date,
                GenderId = i.GenderId,
                BloodGroupId = i.BloodGroupId,
                StudentCategoryId = i.StudentCategoryId,
                AadharNumber = i.AadharNumber,
                AdmissionTypeId = i.AdmissionTypeId,
                AdmissionDate = i.AdmissionDate.Date,   // historical, not "now"
                Email = i.Email,
                MobileNumber = i.MobileNumber,
                AddressLine1 = i.AddressLine1,
                StatusId = i.ActiveStatusId,
                CreatedAt = i.Now,
                UpdatedAt = i.Now
            };
            var enrollment = new TbStudentEnrollment
            {
                SchoolId = i.SchoolId,
                StudentId = i.StudentId,
                AcademicYearId = i.AcademicYearId,
                ClassSectionId = i.ClassSectionId,
                RollNumber = i.RollNumber,
                EnrollmentDate = i.AdmissionDate.Date,
                AdmissionTypeId = i.AdmissionTypeId,
                StatusId = i.ActiveStatusId,
                CreatedAt = i.Now,
                UpdatedAt = i.Now
            };
            var graph = new OnboardingGraph { Student = student, Enrollment = enrollment };
            for (var n = 0; n < i.Guardians.Count; n++)
            {
                var g = i.Guardians[n];
                var primary = n == 0;
                var guardian = new TbGuardian
                {
                    SchoolId = i.SchoolId,
                    FirstName = g.Name.Trim(),
                    Relationship = g.Relationship,
                    MobileNumber = g.Mobile,
                    Email = g.Email,
                    StatusId = i.ActiveStatusId,
                    CreatedAt = i.Now,
                    UpdatedAt = i.Now
                };
                graph.Guardians.Add(guardian);
                graph.Links.Add(new TbStudentGuardian
                {
                    StudentId = i.StudentId,
                    Guardian = guardian,          // GuardianId is filled in from the identity value on save
                    Relationship = g.Relationship,
                    IsPrimary = primary,
                    IsEmergencyContact = primary,
                    CanPickupStudent = true,
                    CreatedAt = i.Now,
                    UpdatedAt = i.Now
                });
            }
            return graph;
        }
    }
}
