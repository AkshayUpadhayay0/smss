using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_board_type", Schema = "public")]
    public class LutBoardType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("board_type_id")]
        public long BoardTypeId { get; set; }

        [Column("board_code")]
        [StringLength(50)]
        public string BoardCode { get; set; } = null!;

        [Column("board_name")]
        [StringLength(150)]
        public string BoardName { get; set; } = null!;

        [Column("description")]
        public string? Description { get; set; }

        [Column("isactive")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}