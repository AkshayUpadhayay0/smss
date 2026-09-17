using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_state", Schema = "public")] public class LutState { [Column("cid")] public int Cid { get; set; } [Column("sid")] public int Sid { get; set; } [Column("sname")][StringLength(250)] public string Sname { get; set; } = null!; }
}
