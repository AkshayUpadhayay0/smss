using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_city", Schema = "public")] public class LutCity { [Column("cid")] public int Cid { get; set; } [Column("sid")] public int Sid { get; set; } [Column("did")] public int Did { get; set; } [Column("city_id")] public int CityId { get; set; } [Column("city_name")][StringLength(250)] public string CityName { get; set; } = null!; }
}
