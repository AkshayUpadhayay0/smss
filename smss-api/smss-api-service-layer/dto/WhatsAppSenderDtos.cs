using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public static class SenderStatuses
    {
        public const string NotRegistered = "NOT_REGISTERED";
        public const string Pending = "PENDING";
        public const string Active = "ACTIVE";
        public static readonly string[] All = { NotRegistered, Pending, Active };
    }

    /// <summary>
    /// A school's WhatsApp sender as it is shown. It deliberately carries NO credential of any kind (no access token, app secret or password): those will live only in user-secrets and environment variables.
    /// The Meta phone-number id and who changed it last are shown to the Super Admin only.
    /// </summary>
    public class SenderStatusDto
    {
        public string SchoolId { get; set; } = null!;
        public string SchoolName { get; set; } = null!;
        public string? WhatsAppNumber { get; set; }
        /// <summary>NOT_REGISTERED, PENDING or ACTIVE.</summary>
        public string Status { get; set; } = SenderStatuses.NotRegistered;
        public string StatusLabel { get; set; } = null!;
        /// <summary>In words: whose number the school's messages go from (the platform's until the school's own is ACTIVE).</summary>
        public string SendsFrom { get; set; } = null!;
        public DateTime? VerifiedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        /// <summary>The Meta phone-number id (an identifier, not a secret). Super Admin only; null for everyone else.</summary>
        public string? PhoneNumberId { get; set; }
        /// <summary>Who saved it last. Super Admin only.</summary>
        public string? UpdatedByName { get; set; }
        /// <summary>True when the caller may change it (the Super Admin).</summary>
        public bool CanEdit { get; set; }
    }

    public class SaveSenderRequestDto
    {
        /// <summary>NOT_REGISTERED, PENDING or ACTIVE.</summary>
        [Required] public string Status { get; set; } = null!;
        /// <summary>The school's WhatsApp number: 10 digits, or with the country code. Required for PENDING and ACTIVE.</summary>
        [MaxLength(25)] public string? WhatsAppNumber { get; set; }
        /// <summary>The Meta phone-number id. Required for ACTIVE.</summary>
        [MaxLength(50)] public string? PhoneNumberId { get; set; }
    }
}
