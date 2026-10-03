using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace smss_api_service_layer.dto
{
    internal class BoardTypeDtos
    {
    }

    public class CreateBoardTypeRequestDto
    {
        [Required, StringLength(50, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string BoardCode { get; set; } = string.Empty;

        [Required, StringLength(150, MinimumLength = 2)]
        public string BoardName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }

    // BoardCode is deliberately absent: the code is immutable after creation.
    public class UpdateBoardTypeRequestDto
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string BoardName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }

    public class CreateSchoolTypeRequestDto
    {
        [Required, StringLength(50, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string SchoolTypeCode { get; set; } = string.Empty;

        [Required, StringLength(150, MinimumLength = 2)]
        public string SchoolTypeName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }

    // SchoolTypeCode is deliberately absent: the code is immutable after creation.
    public class UpdateSchoolTypeRequestDto
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string SchoolTypeName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }


    public class CreateSchoolLevelRequestDto
    {
        [Required, StringLength(50, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string SchoolLevelCode { get; set; } = string.Empty;

        [Required, StringLength(150, MinimumLength = 2)]
        public string SchoolLevelName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }

    // SchoolLevelCode is deliberately absent: the code is immutable after creation.
    public class UpdateSchoolLevelRequestDto
    {
        [Required, StringLength(150, MinimumLength = 2)]
        public string SchoolLevelName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }



    // Status has no code: sid is the identity other tables reference, so name and type are safe to edit.
    public class CreateStatusRequestDto
    {
        [Required, StringLength(250, MinimumLength = 2)]
        public string Sname { get; set; } = string.Empty;

        [Required, StringLength(250, MinimumLength = 2)]
        public string Stype { get; set; } = string.Empty;
    }

    public class UpdateStatusRequestDto
    {
        [Required, StringLength(250, MinimumLength = 2)]
        public string Sname { get; set; } = string.Empty;

        [Required, StringLength(250, MinimumLength = 2)]
        public string Stype { get; set; } = string.Empty;
    }


    public class RoleDto
    {
        public long RoleId { get; set; }
        public string RoleCode { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? StatusId { get; set; }
        public bool IsActive { get; set; }
        public bool IsProtected { get; set; }   // system roles: cannot be deactivated
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CreateRoleRequestDto
    {
        [Required, StringLength(50, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string RoleCode { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string RoleName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }

    // RoleCode is deliberately absent: the code is immutable after creation.
    public class UpdateRoleRequestDto
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string RoleName { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Description { get; set; }
    }



}
