using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A public "Contact Us" or "Request a Demo" submission. Platform-level: no school_id.</summary>
    [Table("tb_enquiries", Schema = "public")]
    public class TbEnquiry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("enquiry_id")] public long EnquiryId { get; set; }

        [Column("enquiry_type")][StringLength(20)] public string EnquiryType { get; set; } = null!;
        [Column("full_name")][StringLength(150)] public string FullName { get; set; } = null!;
        [Column("designation")][StringLength(100)] public string? Designation { get; set; }
        [Column("school_name")][StringLength(200)] public string? SchoolName { get; set; }
        [Column("email")][StringLength(200)] public string Email { get; set; } = null!;
        [Column("phone")][StringLength(30)] public string? Phone { get; set; }
        [Column("city")][StringLength(100)] public string? City { get; set; }
        [Column("student_count_range")][StringLength(30)] public string? StudentCountRange { get; set; }
        [Column("subject")][StringLength(200)] public string? Subject { get; set; }
        [Column("message")] public string? Message { get; set; }
        [Column("preferred_demo_date")] public DateOnly? PreferredDemoDate { get; set; }
        [Column("preferred_time_slot")][StringLength(50)] public string? PreferredTimeSlot { get; set; }
        [Column("consent_given")] public bool ConsentGiven { get; set; }
        [Column("status")][StringLength(20)] public string Status { get; set; } = "NEW";
        [Column("admin_notes")] public string? AdminNotes { get; set; }
        [Column("handled_by_user_id")] public long? HandledByUserId { get; set; }
        [Column("ip_address")][StringLength(45)] public string? IpAddress { get; set; }
        [Column("user_agent")][StringLength(500)] public string? UserAgent { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime? UpdatedAt { get; set; }
    }
}
