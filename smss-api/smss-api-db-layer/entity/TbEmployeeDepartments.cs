using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_employee_departments", Schema = "public")]
    public class TbEmployeeDepartments
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("department_id")]
        public long DepartmentId { get; set; }

        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("department_name")] public string DepartmentName { get; set; } = null!;
        [Column("description")] public string? Description { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
