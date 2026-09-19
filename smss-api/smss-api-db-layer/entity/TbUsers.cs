using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_users", Schema = "public")]
    public class TbUsers
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("user_id")]
        public long UserId { get; set; }

        [Column("school_id")][StringLength(100)] public string? SchoolId { get; set; }
        [Column("user_type")][StringLength(30)] public string UserType { get; set; } = null!;
        [Column("org_user_id")][StringLength(50)] public string? OrgUserId { get; set; }
        [Column("username")][StringLength(100)] public string? Username { get; set; }
        [Column("email")][StringLength(150)] public string? Email { get; set; }
        [Column("mobile_number")][StringLength(20)] public string? MobileNumber { get; set; }
        [Column("password_hash")] public string? PasswordHash { get; set; }

        [Column("is_first_login")] public bool IsFirstLogin { get; set; } = true;
        [Column("is_email_verified")] public bool IsEmailVerified { get; set; }
        [Column("is_mobile_verified")] public bool IsMobileVerified { get; set; }

        [Column("status_id")] public int? StatusId { get; set; }
        [Column("last_login_at")] public DateTime? LastLoginAt { get; set; }

        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }

        public ICollection<TbUserRoles> UserRoles { get; set; } = new List<TbUserRoles>();
    }
}
