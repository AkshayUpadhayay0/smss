using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_school_contacts", Schema = "public")]
    public class TbSchoolContacts
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("contact_id")]
        public long ContactId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("contact_type")][StringLength(100)] public string ContactType { get; set; } = null!;
        [Column("contact_name")][StringLength(200)] public string ContactName { get; set; } = null!;
        [Column("designation")][StringLength(150)] public string? Designation { get; set; }
        [Column("email")][StringLength(150)] public string? Email { get; set; }
        [Column("mobile_number")][StringLength(20)] public string? MobileNumber { get; set; }
        [Column("alternate_mobile_number")][StringLength(20)] public string? AlternateMobileNumber { get; set; }
        [Column("is_primary")] public bool IsPrimary { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }

        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
