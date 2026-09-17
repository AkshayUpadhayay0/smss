using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_country", Schema = "public")] 
    public class LutCountry 
    { 
        [Column("cid")] 
        public int Cid { get; set; } 
        [Column("cname")][StringLength(250)] 
        public string Cname { get; set; } = null!; 
    }
}
