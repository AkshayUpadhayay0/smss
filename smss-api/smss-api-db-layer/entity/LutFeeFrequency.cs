using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // Global, small fixed list: One Time, Monthly, Quarterly, Half-Yearly, Annual. Read-only through the API.
    [Table("lut_fee_frequency", Schema = "public")]
    public class LutFeeFrequency
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("frequency_id")]
        public long FrequencyId { get; set; }

        [Column("frequency_name")][StringLength(50)] public string FrequencyName { get; set; } = null!;
        [Column("frequency_code")][StringLength(30)] public string FrequencyCode { get; set; } = null!;
        [Column("isactive")] public bool IsActive { get; set; } = true;
    }
}
