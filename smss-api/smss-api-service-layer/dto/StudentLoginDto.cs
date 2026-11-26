namespace smss_api_service_layer.dto
{
    /// <summary>
    /// A student login was created WITHOUT a password: the primary guardian is sent a one-time link to set it (WhatsApp and email). No password, token or link is ever returned. When the link could not be sent
    /// (no primary guardian, no consent on record, no channel, a failed send) <see cref="LinkSent"/> is false and <see cref="Message"/> says what to do; the school can resend.
    /// </summary>
    public class StudentLoginCreatedDto
    {
        public string StudentId { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        /// <summary>The login id to sign in with, e.g. DPS001ST001 (tb_users.org_user_id).</summary>
        public string OrgUserId { get; set; } = null!;
        public bool LinkSent { get; set; }
        public string? Message { get; set; }
        public List<ParentDeliveryDto> Deliveries { get; set; } = new();
    }
}
