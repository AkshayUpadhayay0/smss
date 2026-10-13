namespace smss_api_service_layer.dto
{
    /// <summary>Returned once, right after a student login is created. The password cannot be fetched again.</summary>
    public class StudentLoginCreatedDto
    {
        public string StudentId { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        /// <summary>The login id to sign in with, e.g. DPS001ST001 (tb_users.org_user_id).</summary>
        public string OrgUserId { get; set; } = null!;
        public string TemporaryPassword { get; set; } = null!;
        public bool EmailSent { get; set; }
        public string? EmailSentTo { get; set; }
    }
}
