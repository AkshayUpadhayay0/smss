namespace smss_api_service_layer.dto
{
    public class StudentListItemDto
    {
        public long StudentId { get; set; }
        public string AdmissionNumber { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }          // from the enrollment, not the student
        public string? GenderName { get; set; }
        public string StatusName { get; set; } = null!;
        public long EnrollmentId { get; set; }
        public string? ProfilePhotoUrl { get; set; }   // relative public URL; null = no photo
    }

    public class GuardianSummaryDto
    {
        public long GuardianId { get; set; }
        public string FullName { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        public string? Email { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsEmergencyContact { get; set; }
        public bool CanPickupStudent { get; set; }
    }

    public class StudentPhotoDto
    {
        public long StudentId { get; set; }
        public string? ProfilePhotoUrl { get; set; }
    }

    public class StudentProfileDto
    {
        public long StudentId { get; set; }
        public string AdmissionNumber { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public DateTime DateOfBirth { get; set; }
        public string? GenderName { get; set; }
        public string? BloodGroupName { get; set; }
        public string? StudentCategoryName { get; set; }
        public string? AdmissionTypeName { get; set; }
        public DateTime AdmissionDate { get; set; }
        public string? Email { get; set; }
        public string? MobileNumber { get; set; }
        public string StatusName { get; set; } = null!;
        public string? AadharNumberMasked { get; set; }
        public string? ProfilePhotoUrl { get; set; }   // relative public URL; null = no photo

        // Current enrollment (null when the student has none yet)
        public long? EnrollmentId { get; set; }
        public string? AcademicYearName { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? RollNumber { get; set; }
        public string? EnrollmentStatusName { get; set; }

        public List<GuardianSummaryDto> Guardians { get; set; } = new();
    }
}
