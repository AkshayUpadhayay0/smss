using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_refresh_tokens", Schema = "public")]
    public class TbRefreshTokens
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("refresh_token_id")]
        public long RefreshTokenId { get; set; }

        [Column("user_id")] public long UserId { get; set; }
        [Column("token_hash")][StringLength(64)] public string TokenHash { get; set; } = null!;
        [Column("remember_me")] public bool RememberMe { get; set; }
        [Column("expires_at")] public DateTime ExpiresAt { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("revoked_at")] public DateTime? RevokedAt { get; set; }
        [Column("replaced_by_token_hash")][StringLength(64)] public string? ReplacedByTokenHash { get; set; }
    }
}
