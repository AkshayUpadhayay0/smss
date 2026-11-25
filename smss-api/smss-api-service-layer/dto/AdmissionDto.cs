using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace smss_api_service_layer.dto
{
    public class SubmitAdmissionRequestDto : IValidatableObject
    {
        [Required] public long AcademicYearId { get; set; }
        [Required] public long AppliedClassId { get; set; }
        /// <summary>Optional: left out, a new application is New Admission (found by code); an edit keeps its current type.</summary>
        public long? AdmissionTypeId { get; set; }
        [Required, MaxLength(100)] public string ApplicantFirstName { get; set; } = null!;
        [MaxLength(100)] public string? ApplicantMiddleName { get; set; }
        [MaxLength(100)] public string? ApplicantLastName { get; set; }   // optional: only the first name is mandatory (a blank becomes null)
        [Required] public DateTime DateOfBirth { get; set; }
        public int? GenderId { get; set; }
        public int? BloodGroupId { get; set; }
        public int? StudentCategoryId { get; set; }
        [RegularExpression(@"^\d{12}$", ErrorMessage = "Aadhaar number must be exactly 12 digits.")]
        public string? AadharNumber { get; set; }
        [MaxLength(50)] public string? StudentNationId { get; set; }
        [EmailAddress, MaxLength(150)] public string? Email { get; set; }
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")]
        public string? MobileNumber { get; set; }
        [MaxLength(200)] public string? AddressLine1 { get; set; }
        [MaxLength(200)] public string? AddressLine2 { get; set; }
        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public int? DistrictId { get; set; }
        public int? CityId { get; set; }
        [RegularExpression(Rx.Pincode, ErrorMessage = "Invalid pincode.")] public string? Pincode { get; set; }
        [MaxLength(150)] public string GuardianName { get; set; } = null!;   // required unless an existing guardian is picked (see Validate)
        [RegularExpression("^(FATHER|MOTHER|GUARDIAN)$", ErrorMessage = "GuardianRelationship must be FATHER, MOTHER or GUARDIAN.")]
        public string GuardianRelationship { get; set; } = null!;
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")]
        public string GuardianMobileNumber { get; set; } = null!;
        /// <summary>Mandatory for the primary guardian (checked by <see cref="Validate"/>: it needs a message that says which of the two is wrong).</summary>
        [MaxLength(150)] public string? GuardianEmail { get; set; }
        /// <summary>The guardian's WhatsApp number: 10 digits, or with the country code. Mandatory unless <see cref="GuardianWhatsAppSameAsMobile"/> is set.</summary>
        [MaxLength(25)] public string? GuardianWhatsAppNumber { get; set; }
        /// <summary>"Same as mobile number": the server then uses the guardian's mobile number as the WhatsApp number.</summary>
        public bool GuardianWhatsAppSameAsMobile { get; set; }
        /// <summary>
        /// The EXISTING guardian who is this student's primary guardian (a sibling's parent, picked from the sibling search). Null = the primary guardian is the person typed in below. When it is set the guardian's
        /// name and mobile come from the guardian record (what is sent for them is ignored), WhatsApp and email are needed only where the record has none, and those are written to the shared record.
        /// </summary>
        public long? ExistingGuardianId { get; set; }
        /// <summary>Other existing guardians to link (not primary), each with the relationship for this student.</summary>
        public List<ExistingGuardianPickDto>? AdditionalGuardians { get; set; }
        /// <summary>The school the applicant comes from, if any.</summary>
        [MaxLength(200)] public string? PreviousSchool { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            // an EXISTING guardian is picked: name and mobile come from the record, and the service checks WhatsApp and email against the record
            if (ExistingGuardianId != null) yield break;
            if (string.IsNullOrWhiteSpace(GuardianName)) yield return new ValidationResult("The GuardianName field is required.", new[] { nameof(GuardianName) });
            if (string.IsNullOrWhiteSpace(GuardianRelationship)) yield return new ValidationResult("The GuardianRelationship field is required.", new[] { nameof(GuardianRelationship) });
            if (string.IsNullOrWhiteSpace(GuardianMobileNumber)) yield return new ValidationResult("The GuardianMobileNumber field is required.", new[] { nameof(GuardianMobileNumber) });
            var w = smss_api_service_layer.helper.PhoneNumbers.WhatsAppError(GuardianWhatsAppNumber, GuardianWhatsAppSameAsMobile, GuardianMobileNumber, "The guardian's");
            if (w != null) yield return new ValidationResult(w, new[] { nameof(GuardianWhatsAppNumber) });
            var e = smss_api_service_layer.helper.PhoneNumbers.EmailError(GuardianEmail, "The guardian's");
            if (e != null) yield return new ValidationResult(e, new[] { nameof(GuardianEmail) });
        }
    }

    /// <summary>An existing guardian picked for an application or a direct entry, with the relationship the new student has to them.</summary>
    public class ExistingGuardianPickDto
    {
        [Required] public long GuardianId { get; set; }
        [RegularExpression("^(FATHER|MOTHER|GUARDIAN)$", ErrorMessage = "Relationship must be FATHER, MOTHER or GUARDIAN.")] public string? Relationship { get; set; }
    }

    /// <summary>An existing guardian as the forms show them: the name, a MASKED mobile number, and whether a WhatsApp number and an email are already on record. Never the whole number.</summary>
    public class GuardianPickDto
    {
        public long GuardianId { get; set; }
        public string Name { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string MaskedMobile { get; set; } = null!;
        public bool HasWhatsApp { get; set; }
        public bool HasEmail { get; set; }
    }

    public class AdmissionDecisionRequestDto
    {
        [MaxLength(2000)] public string? Remarks { get; set; }
    }

    public class ConfirmAdmissionRequestDto
    {
        // tb_sections.section_id: the section the student is placed in
        [Required, Range(1, long.MaxValue)] public long ClassSectionId { get; set; }
        [MaxLength(30)] public string? RollNumber { get; set; }

        /// <summary>Fee configuration chosen at admission (frequency / applicability per fee head). Optional: omitted = the structure's defaults.</summary>
        [MaxLength(100)] public List<FeeItemSelection>? FeeItems { get; set; }
    }

    public class AdmissionDocumentDto
    {
        public long ApplicationDocumentId { get; set; }
        public long DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = null!;
        public string FileName { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long FileSize { get; set; }
        public string? Remarks { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; }
        /// <summary>Active / Inactive (a removed document is hidden unless asked for).</summary>
        public string StatusName { get; set; } = null!;
    }

    // What the list shows (the joined names mean the table needs no second lookup)
    public class AdmissionResponseDto
    {
        public long AdmissionId { get; set; }
        public string ApplicationNumber { get; set; } = null!;
        public string ApplicantFullName { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public string? AadharNumberMasked { get; set; }
        public DateTime ApplicationDate { get; set; }
        public string? StudentId { get; set; }
        public long? EnrollmentId { get; set; }

        public long AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public long AppliedClassId { get; set; }
        public string? AppliedClassName { get; set; }
        public long AdmissionTypeId { get; set; }
        public string? AdmissionTypeName { get; set; }
    }

    // Everything submitted, plus the decision trail. Aadhaar stays masked.
    public class AdmissionDetailDto : AdmissionResponseDto
    {
        /// <summary>Set by confirm-admission: the fee structure the new student was automatically assigned to.</summary>
        public string? FeeStructureName { get; set; }
        /// <summary>Things that did not block the operation but need attention (e.g. no fee structure found for the class).</summary>
        public List<string> Warnings { get; set; } = new();

        public string ApplicantFirstName { get; set; } = null!;
        public string? ApplicantMiddleName { get; set; }
        public string? ApplicantLastName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public int? GenderId { get; set; }
        public string? GenderName { get; set; }
        public int? BloodGroupId { get; set; }
        public string? BloodGroupName { get; set; }
        public int? StudentCategoryId { get; set; }
        public string? StudentCategoryName { get; set; }
        public string? StudentNationId { get; set; }
        public string? Email { get; set; }
        public string? MobileNumber { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public int? DistrictId { get; set; }
        public int? CityId { get; set; }
        public string? Pincode { get; set; }
        public string GuardianName { get; set; } = null!;
        public string GuardianRelationship { get; set; } = null!;
        public string GuardianMobileNumber { get; set; } = null!;
        public string? GuardianEmail { get; set; }
        /// <summary>The existing guardian this application reuses as the primary guardian (a sibling's parent); null when the primary guardian is a person typed in.</summary>
        public long? GuardianId { get; set; }
        public GuardianPickDto? ExistingGuardian { get; set; }
        public List<GuardianPickDto> AdditionalGuardians { get; set; } = new();
        /// <summary>With the country code; null on an application made before it was collected.</summary>
        public string? GuardianWhatsAppNumber { get; set; }

        public string? PreviousSchool { get; set; }
        public string? ReviewNotes { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        /// <summary>When the application was confirmed (the student was created); null until then.</summary>
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? DecisionRemarks { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
