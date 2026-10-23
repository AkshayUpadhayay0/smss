using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>Create and update share one shape: the id, school and photo are never accepted from the client.</summary>
    public class EmployeeRequest
    {
        [Required(ErrorMessage = "Designation is required.")] public long? DesignationId { get; set; }
        [Required(ErrorMessage = "Department is required.")] public long? DepartmentId { get; set; }
        /// <summary>Omit on create to start as Active.</summary>
        public long? EmploymentStatusId { get; set; }
        [Required(ErrorMessage = "Employee type is required."), RegularExpression("^(Teaching|Non-Teaching)$", ErrorMessage = "Employee type must be Teaching or Non-Teaching.")]
        public string? EmployeeType { get; set; }

        [Required(ErrorMessage = "First name is required."), MaxLength(100)] public string? FirstName { get; set; }
        [MaxLength(100)] public string? MiddleName { get; set; }
        [Required(ErrorMessage = "Last name is required."), MaxLength(100)] public string? LastName { get; set; }
        public long? GenderId { get; set; }
        public long? BloodGroupId { get; set; }
        [Required(ErrorMessage = "Date of birth is required.")] public DateOnly? DateOfBirth { get; set; }

        [EmailAddress, MaxLength(150)] public string? Email { get; set; }
        [Required(ErrorMessage = "Mobile number is required."), RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")] public string? MobileNumber { get; set; }
        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")] public string? AlternateMobile { get; set; }
        [Required(ErrorMessage = "Address is required."), MaxLength(1000)] public string? Address { get; set; }

        [Required(ErrorMessage = "Date of joining is required.")] public DateOnly? DateOfJoining { get; set; }
        public DateOnly? DateOfLeaving { get; set; }
        [MaxLength(1000)] public string? Qualification { get; set; }
    }

    public class EmployeeListItemDto
    {
        public string EmployeeId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string EmployeeType { get; set; } = null!;
        public long DesignationId { get; set; }
        public string DesignationName { get; set; } = null!;
        public long DepartmentId { get; set; }
        public string DepartmentName { get; set; } = null!;
        public string EmploymentStatusName { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        public string? Email { get; set; }
        public DateOnly DateOfJoining { get; set; }
        /// <summary>Active / Inactive: whether the row is shown (the soft-delete flag).</summary>
        public string StatusName { get; set; } = null!;
        public string? PhotoUrl { get; set; }
        public bool HasLogin { get; set; }
    }

    public class EmployeePagedDto
    {
        public List<EmployeeListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class EmployeeDto
    {
        public string EmployeeId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = null!;
        public string EmployeeType { get; set; } = null!;
        public long DesignationId { get; set; }
        public string DesignationName { get; set; } = null!;
        public long DepartmentId { get; set; }
        public string DepartmentName { get; set; } = null!;
        public long EmploymentStatusId { get; set; }
        public string EmploymentStatusName { get; set; } = null!;
        public long? GenderId { get; set; }
        public string? GenderName { get; set; }
        public long? BloodGroupId { get; set; }
        public string? BloodGroupName { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public string? Email { get; set; }
        public string MobileNumber { get; set; } = null!;
        public string? AlternateMobile { get; set; }
        public string Address { get; set; } = null!;
        public DateOnly DateOfJoining { get; set; }
        public DateOnly? DateOfLeaving { get; set; }
        public string? Qualification { get; set; }
        public string? PhotoUrl { get; set; }
        public string StatusName { get; set; } = null!;
        /// <summary>The employee's login id once a login exists (null before); the password is never returned again.</summary>
        public string? LoginId { get; set; }
        public List<string> LoginRoles { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EmployeePhotoDto
    {
        public string EmployeeId { get; set; } = null!;
        public string? PhotoUrl { get; set; }
    }

    public class EmployeeDocumentDto
    {
        public long EmployeeDocumentId { get; set; }
        public long DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = null!;
        public string FileName { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long FileSize { get; set; }
        public string? Remarks { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedAt { get; set; }
        /// <summary>Active or Inactive (a removed document is kept, hidden by default).</summary>
        public string StatusName { get; set; } = null!;
    }

    public class CreateEmployeeLoginRequest
    {
        [Required(ErrorMessage = "Choose at least one role."), MinLength(1, ErrorMessage = "Choose at least one role."), MaxLength(20)]
        public List<long>? RoleIds { get; set; }
    }

    public class EmployeeLoginCreatedDto
    {
        public string EmployeeId { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string OrgUserId { get; set; } = null!;
        /// <summary>Shown ONCE; only its hash is stored.</summary>
        public string TemporaryPassword { get; set; } = null!;
        public List<string> Roles { get; set; } = new();
        public bool EmailSent { get; set; }
        public string? EmailSentTo { get; set; }
    }

    public class LookupItemDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public class EmployeeDocumentTypeDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
        public string Code { get; set; } = null!;
        public bool IsRequired { get; set; }
    }

    public class AssignableRoleDto
    {
        public long RoleId { get; set; }
        public string RoleName { get; set; } = null!;
        public string? Description { get; set; }
    }

    /// <summary>Everything the employee form and the create-login dialog choose from, in one call.</summary>
    public class EmployeeLookupsDto
    {
        public List<LookupItemDto> Designations { get; set; } = new();
        public List<LookupItemDto> Departments { get; set; } = new();
        public List<LookupItemDto> EmploymentStatuses { get; set; } = new();
        public List<LookupItemDto> Genders { get; set; } = new();
        public List<LookupItemDto> BloodGroups { get; set; } = new();
        public List<EmployeeDocumentTypeDto> DocumentTypes { get; set; } = new();
        public List<AssignableRoleDto> AssignableRoles { get; set; } = new();
    }
}
