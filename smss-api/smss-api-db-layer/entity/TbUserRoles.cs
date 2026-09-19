using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_user_roles", Schema = "public")]
    public class TbUserRoles
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("user_role_id")]
        public long UserRoleId { get; set; }

        [Column("user_id")] public long UserId { get; set; }
        [Column("role_id")] public long RoleId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }

        public TbUsers? User { get; set; }
    }
}
