using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // ---- public (anonymous) requests: the token travels in the BODY, never in a URL
    public class SetParentPasswordRequest
    {
        [Required, StringLength(100)] public string Token { get; set; } = null!;
        [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "The password must be at least 8 characters.")] public string NewPassword { get; set; } = null!;
    }

    public class CheckLinkRequest
    {
        [Required, StringLength(100)] public string Token { get; set; } = null!;
    }

    public class ForgotLoginRequest
    {
        /// <summary>The registered WhatsApp number or email address.</summary>
        [Required, StringLength(150)] public string Contact { get; set; } = null!;
    }

    public class LinkCheckDto { public bool Valid { get; set; } }

    public class PasswordSetDto
    {
        /// <summary>The login id the person signs in with (shown so a parent can note it down).</summary>
        public string LoginId { get; set; } = null!;
    }

    // ---- what the staff screens see
    public class ParentDeliveryDto
    {
        public long DeliveryId { get; set; }
        public string Channel { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string TemplateCode { get; set; } = null!;
        public string Purpose { get; set; } = null!;
        public string? RecipientMasked { get; set; }
        public string SenderKind { get; set; } = null!;
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// What happened to a parent account request. Outcome: CREATED (new account, link sent where possible), CHILD_ADDED (the guardian already had an account: the "child added" message was sent), RESENT (a new link was sent),
    /// NOT_CREATED (a reason a person can act on) or FAILED (something unexpected; the admission / action itself is not affected). Never holds a link, a token or a password.
    /// </summary>
    public class ParentAccountResultDto
    {
        public string Outcome { get; set; } = null!;
        public string? Reason { get; set; }
        public long? GuardianId { get; set; }
        public string? LoginId { get; set; }
        /// <summary>True when a staff member can press Resend now.</summary>
        public bool CanResend { get; set; }
        public List<ParentDeliveryDto> Deliveries { get; set; } = new();
    }

    public class ParentAccountStatusDto
    {
        public long GuardianId { get; set; }
        public string GuardianName { get; set; } = null!;
        public bool HasAccount { get; set; }
        public string? LoginId { get; set; }
        /// <summary>True once the parent has chosen a password (the link was used).</summary>
        public bool PasswordSet { get; set; }
        public bool ConsentRecorded { get; set; }
        public bool WhatsAppOnFile { get; set; }
        public bool EmailOnFile { get; set; }
        public int LinksInLast24Hours { get; set; }
        public int MaxLinksPer24Hours { get; set; }
        public List<ParentDeliveryDto> Deliveries { get; set; } = new();
    }

    public class BulkParentAccountPreviewDto
    {
        /// <summary>Guardians of Active students with no account and complete details (name, mobile, WhatsApp number and email).</summary>
        public int Eligible { get; set; }
        /// <summary>How many the next run would create: at most <see cref="MaxPerRun"/>.</summary>
        public int WillCreate { get; set; }
        public int MaxPerRun { get; set; }
        /// <summary>Guardians of Active students with no account who still lack a WhatsApp number or an email: complete them on the Parent details screen first.</summary>
        public int IncompleteDetails { get; set; }
        /// <summary>Of the eligible guardians, those with no consent on record: the confirmation below records it for them.</summary>
        public int WithoutConsentOnRecord { get; set; }
    }

    public class BulkParentAccountRequest
    {
        /// <summary>The School Admin's confirmation that the school holds the parents' consent to be contacted by WhatsApp and email. Must be true; it is written (with who and when) to the consent columns of every guardian in the run who has none yet.</summary>
        public bool ConfirmSchoolHoldsParentConsent { get; set; }
    }

    public class BulkParentAccountItemDto
    {
        public long GuardianId { get; set; }
        public string Name { get; set; } = null!;
        public string Outcome { get; set; } = null!;
        public string? Reason { get; set; }
    }

    public class BulkParentAccountResultDto
    {
        public int Attempted { get; set; }
        public int Created { get; set; }
        public int NotCreated { get; set; }
        /// <summary>Eligible guardians left for another run.</summary>
        public int Remaining { get; set; }
        public List<BulkParentAccountItemDto> Items { get; set; } = new();
    }
}
