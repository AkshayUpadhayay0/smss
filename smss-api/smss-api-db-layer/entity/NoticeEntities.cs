using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One notice for one audience tier (ALL, STAFF or STUDENTS_GUARDIANS), published the moment it is created. Deleting hides it (status Inactive).</summary>
    [Table("tb_notices", Schema = "public")]
    public class TbNotice
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("notice_id")]
        public long NoticeId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("title")][StringLength(200)] public string Title { get; set; } = null!;
        [Column("body")] public string Body { get; set; } = null!;
        [Column("audience")][StringLength(20)] public string Audience { get; set; } = null!;
        [Column("created_by")] public long CreatedBy { get; set; }
        [Column("published_at")] public DateTime PublishedAt { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>That a user has opened a notice (one row per notice per user).</summary>
    [Table("tb_notice_reads", Schema = "public")]
    public class TbNoticeRead
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("notice_read_id")]
        public long NoticeReadId { get; set; }

        [Column("notice_id")] public long NoticeId { get; set; }
        [Column("user_id")] public long UserId { get; set; }
        [Column("read_at")] public DateTime ReadAt { get; set; }
    }
}
