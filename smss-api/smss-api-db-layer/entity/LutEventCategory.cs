using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Global lookup of calendar event categories, each with the colour it is drawn in (#rrggbb).</summary>
    [Table("lut_event_category", Schema = "public")]
    public class LutEventCategory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("event_category_id")]
        public long EventCategoryId { get; set; }

        [Column("category_name")][StringLength(50)] public string CategoryName { get; set; } = null!;
        [Column("category_code")][StringLength(30)] public string CategoryCode { get; set; } = null!;
        [Column("color_hex")][StringLength(7)] public string ColorHex { get; set; } = null!;
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
