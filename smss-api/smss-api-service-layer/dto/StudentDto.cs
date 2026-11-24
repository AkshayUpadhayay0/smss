namespace smss_api_service_layer.dto
{
    public class StudentListItemDto
    {
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }          // from the enrollment, not the student
        public string? GenderName { get; set; }
        public string StatusName { get; set; } = null!;
        public long EnrollmentId { get; set; }
        public string? ProfilePhotoUrl { get; set; }   // relative public URL; null = no photo
        /// <summary>Overdue | Partially Paid | Pending | Paid | Not Set (no fee installments for this year), for the list's academic year.</summary>
        public string FeeStatus { get; set; } = "Not Set";
        /// <summary>Totals over all the student's installments for that year; null when FeeStatus is Not Set.</summary>
        public decimal? FeeTotalDue { get; set; }
        public decimal? FeeTotalPaid { get; set; }
        public decimal? FeeOutstanding { get; set; }
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

    /// <summary>One guardian as the student / parent sees them: no primary / emergency / pickup flags.</summary>
    public class MyGuardianDto
    {
        public string FullName { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string? MobileNumber { get; set; }
        public string? Email { get; set; }
    }

    /// <summary>
    /// GET api/Student/me. Deliberately NOT the staff StudentProfileDto: no Aadhaar (not even masked), login id, fee data, admission type / date or other internal fields.
    /// </summary>
    public class StudentMyProfileDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public string? ProfilePhotoUrl { get; set; }   // existing public photo URL, as everywhere else
        public string? AcademicYearName { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? RollNumber { get; set; }
        public string? GenderName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? BloodGroupName { get; set; }
        public string? StudentCategoryName { get; set; }
        public string? MobileNumber { get; set; }
        public string? Email { get; set; }
        /// <summary>Address lines, city, state and pincode joined into one display string; null when nothing is recorded.</summary>
        public string? Address { get; set; }
        public List<MyGuardianDto> Guardians { get; set; } = new();
    }

    public class StudentPhotoDto
    {
        public string StudentId { get; set; } = null!;
        public string? ProfilePhotoUrl { get; set; }
    }

    public class StudentProfileDto
    {
        /// <summary>True when only the limited view was returned (an Accountant): header fields and class / section; everything else is empty.</summary>
        public bool Limited { get; set; }
        public string StudentId { get; set; } = null!;
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

        /// <summary>The student's login id once a login exists (null before); the password is never returned again.</summary>
        public string? LoginId { get; set; }

        public List<GuardianSummaryDto> Guardians { get; set; } = new();

        /// <summary>EVERY enrolment of the student, newest year first (one per academic year). The single-enrolment fields above are the current one and stay for older clients.</summary>
        public List<StudentEnrollmentSummaryDto> Enrollments { get; set; } = new();
    }

    /// <summary>One year of a student's history: where they were in that year.</summary>
    public class StudentEnrollmentSummaryDto
    {
        public long EnrollmentId { get; set; }
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        /// <summary>The school's current year.</summary>
        public bool IsCurrentYear { get; set; }
        /// <summary>A closed year is read-only history.</summary>
        public bool YearIsClosed { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public string? RollNumber { get; set; }
        public string StatusName { get; set; } = null!;
    }
}
