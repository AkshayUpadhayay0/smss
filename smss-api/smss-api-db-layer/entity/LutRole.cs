using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace smss_api_db_layer.entity
{
    [Table("lut_roles", Schema = "public")]
    public class LutRole
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("role_id")]
        public long RoleId { get; set; }

        [Column("role_name")]
        [StringLength(100)]
        public string RoleName { get; set; } = null!;

        [Column("role_code")]
        [StringLength(50)]
        public string RoleCode { get; set; } = null!;

        [Column("description")]
        public string? Description { get; set; }

        [Column("status_id")]
        public int? StatusId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}

