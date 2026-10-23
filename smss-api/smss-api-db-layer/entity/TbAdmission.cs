using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_admissions", Schema = "public")]
    public class TbAdmission
    {
        [Key][Column("admission_id")] public long AdmissionId { get; set; }
        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("application_number")] public string ApplicationNumber { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("applied_class_id")] public long AppliedClassId { get; set; }
        [Column("admission_type_id")] public long AdmissionTypeId { get; set; }
        [Column("applicant_first_name")] public string ApplicantFirstName { get; set; } = null!;
        [Column("applicant_middle_name")] public string? ApplicantMiddleName { get; set; }
        [Column("applicant_last_name")] public string ApplicantLastName { get; set; } = null!;
        [Column("date_of_birth", TypeName = "date")] public DateTime DateOfBirth { get; set; }
        [Column("gender_id")] public int? GenderId { get; set; }
        [Column("blood_group_id")] public int? BloodGroupId { get; set; }
        [Column("student_category_id")] public int? StudentCategoryId { get; set; }
        [Column("aadhar_number")] public string? AadharNumber { get; set; }
        [Column("student_nation_id")] public string? StudentNationId { get; set; }
        [Column("email")] public string? Email { get; set; }
        [Column("mobile_number")] public string? MobileNumber { get; set; }
        [Column("address_line1")] public string? AddressLine1 { get; set; }
        [Column("address_line2")] public string? AddressLine2 { get; set; }
        [Column("country_id")] public int? CountryId { get; set; }
        [Column("state_id")] public int? StateId { get; set; }
        [Column("district_id")] public int? DistrictId { get; set; }
        [Column("city_id")] public int? CityId { get; set; }
        [Column("pincode")] public string? Pincode { get; set; }
        [Column("guardian_name")] public string GuardianName { get; set; } = null!;
        [Column("guardian_relationship")] public string GuardianRelationship { get; set; } = null!;
        [Column("guardian_mobile_number")] public string GuardianMobileNumber { get; set; } = null!;
        [Column("guardian_email")] public string? GuardianEmail { get; set; }
        [Column("application_date", TypeName = "date")] public DateTime ApplicationDate { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("reviewed_by")] public long? ReviewedBy { get; set; }
        [Column("reviewed_at")] public DateTime? ReviewedAt { get; set; }
        [Column("decision_remarks")] public string? DecisionRemarks { get; set; }
        [Column("rejection_reason")] public string? RejectionReason { get; set; }
        [Column("approved_by")] public long? ApprovedBy { get; set; }
        [Column("approved_at")] public DateTime? ApprovedAt { get; set; }
        [Column("previous_school")][StringLength(200)] public string? PreviousSchool { get; set; }
        [Column("review_notes")] public string? ReviewNotes { get; set; }
        [Column("rejected_by")] public long? RejectedBy { get; set; }
        [Column("rejected_at")] public DateTime? RejectedAt { get; set; }
        [Column("student_id")][StringLength(20)] public string? StudentId { get; set; }
        [ForeignKey(nameof(StudentId))] public TbStudent? Student { get; set; }
        [Column("enrollment_id")] public long? EnrollmentId { get; set; }
        // Lets confirm-admission save the enrollment and this link in ONE SaveChanges
        [ForeignKey(nameof(EnrollmentId))] public TbStudentEnrollment? Enrollment { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
