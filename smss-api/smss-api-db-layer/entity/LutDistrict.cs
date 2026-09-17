using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_district", Schema = "public")] public class LutDistrict { [Column("cid")] public int Cid { get; set; } [Column("sid")] public int Sid { get; set; } [Column("did")] public int Did { get; set; } [Column("dname")][StringLength(250)] public string Dname { get; set; } = null!; }
}
