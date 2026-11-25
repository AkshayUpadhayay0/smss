using System.ComponentModel.DataAnnotations;
using smss_api_service_layer.helper;

namespace smss_api_service_layer.dto
{
    public class GuardianStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public bool IsPrimary { get; set; }
    }

    public class GuardianDto
    {
        public long GuardianId { get; set; }
        public string Name { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        /// <summary>With the country code; null for a guardian added before it was collected.</summary>
        public string? WhatsAppNumber { get; set; }
        public string? Email { get; set; }
        public string? Occupation { get; set; }
        public bool IsActive { get; set; }
        /// <summary>How many students this guardian is linked to. More than 1 means a change here applies to all of them.</summary>
        public int LinkedStudentCount { get; set; }
        public List<GuardianStudentDto> Students { get; set; } = new();
        /// <summary>True when this guardian is the primary guardian of an active student: WhatsApp and email are then required.</summary>
        public bool ContactRequired { get; set; }
    }

    public class GuardianUpdateRequestDto : IValidatableObject
    {
        [Required, MaxLength(100)] public string Name { get; set; } = null!;
        [Required, RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")] public string MobileNumber { get; set; } = null!;
        /// <summary>As typed (10 digits or with the country code); ignored when "same as mobile number" is ticked.</summary>
        public string? WhatsAppNumber { get; set; }
        public bool WhatsAppSameAsMobile { get; set; }
        [MaxLength(150)] public string? Email { get; set; }
        [MaxLength(150)] public string? Occupation { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(Name)) yield return new ValidationResult("The name is required.", new[] { nameof(Name) });
            // WhatsApp and email may be left blank here (a guardian who is not a primary guardian of an active student); the service makes them required where they must be there
            if (WhatsAppSameAsMobile || !string.IsNullOrWhiteSpace(WhatsAppNumber))
            {
                var w = PhoneNumbers.WhatsAppError(WhatsAppNumber, WhatsAppSameAsMobile, MobileNumber, "The");
                if (w != null) yield return new ValidationResult(w, new[] { nameof(WhatsAppNumber) });
            }
            if (!string.IsNullOrWhiteSpace(Email))
            {
                var e = PhoneNumbers.EmailError(Email, "The");
                if (e != null) yield return new ValidationResult(e, new[] { nameof(Email) });
            }
        }
    }

    public class IncompleteGuardianDto
    {
        public long GuardianId { get; set; }
        public string Name { get; set; } = null!;
        public string Relationship { get; set; } = null!;
        public string MobileNumber { get; set; } = null!;
        public string? WhatsAppNumber { get; set; }
        public string? Email { get; set; }
        /// <summary>"WhatsApp" and / or "Email".</summary>
        public List<string> Missing { get; set; } = new();
        public List<IncompleteGuardianStudentDto> Students { get; set; } = new();
    }

    public class IncompleteGuardianStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long? ClassId { get; set; }
        public string? ClassName { get; set; }
        public long? SectionId { get; set; }
        public string? SectionName { get; set; }
    }

    public class IncompleteGuardiansDto
    {
        /// <summary>All guardians that match the filters (before paging).</summary>
        public int Total { get; set; }
        /// <summary>All guardians who need their details completed, before any filter (the dashboard tile's number).</summary>
        public int TotalIncomplete { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        /// <summary>The academic year the class and section come from (the current-flagged year); null when the school has none.</summary>
        public long? ViewedYearId { get; set; }
        public List<IncompleteGuardianDto> Items { get; set; } = new();
    }

    public class BulkWhatsAppRequestDto
    {
        [Required, MinLength(1), MaxLength(200)] public List<long> GuardianIds { get; set; } = new();
    }

    public class BulkWhatsAppResultDto
    {
        public int Selected { get; set; }
        public int Updated { get; set; }
        /// <summary>Guardians that already had a WhatsApp number: left as they were.</summary>
        public int Skipped { get; set; }
    }
}
