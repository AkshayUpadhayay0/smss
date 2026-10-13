using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Direct entry of a student the school already has: no application, review or approval stage.
    public class DirectStudentEntryRequestDto
    {
        // The school's own number when it has one; blank = generated (ADMNO{year}{4-digit seq}, per school)
        [MaxLength(50)] public string? AdmissionNumber { get; set; }

        // Historical: whenever the school actually admitted the student, not today
        [Required] public DateTime AdmissionDate { get; set; }

        [Required, Range(1, long.MaxValue)] public long AcademicYearId { get; set; }

        // tb_sections.section_id
        [Required, Range(1, long.MaxValue)] public long ClassSectionId { get; set; }
        [MaxLength(30)] public string? RollNumber { get; set; }

        [Required, MaxLength(100)] public string FirstName { get; set; } = null!;
        [MaxLength(100)] public string? MiddleName { get; set; }
        [Required, MaxLength(100)] public string LastName { get; set; } = null!;
        [Required] public DateTime DateOfBirth { get; set; }

        public int? GenderId { get; set; }
        public int? BloodGroupId { get; set; }
        public int? StudentCategoryId { get; set; }

        [RegularExpression(@"^\d{12}$", ErrorMessage = "Aadhaar number must be exactly 12 digits.")]
        public string? AadharNumber { get; set; }

        [EmailAddress, MaxLength(150)] public string? Email { get; set; }
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")] public string? MobileNumber { get; set; }

        [Required, MaxLength(150)] public string GuardianName { get; set; } = null!;
        [Required, RegularExpression("^(FATHER|MOTHER|GUARDIAN)$", ErrorMessage = "GuardianRelationship must be FATHER, MOTHER or GUARDIAN.")]
        public string GuardianRelationship { get; set; } = null!;
        [Required, RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")] public string GuardianMobileNumber { get; set; } = null!;
        [EmailAddress, MaxLength(150)] public string? GuardianEmail { get; set; }
    }

    public class StudentOnboardingResponseDto
    {
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public long EnrollmentId { get; set; }
        public string? AadharNumberMasked { get; set; }
    }
}
