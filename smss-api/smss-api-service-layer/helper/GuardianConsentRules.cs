namespace smss_api_service_layer.helper
{
    /// <summary>The parent consent tick: the guardian agrees to be contacted by WhatsApp and email about the account and the child. It is recorded with the date and the user who ticked it; an account is never made, and no message is ever sent, without it.</summary>
    public static class GuardianConsentRules
    {
        public const string Required = "Tick the consent: the parent agrees to be contacted by WhatsApp and email about the account and the child.";
        public const string SourceAdmissionForm = "ADMISSION_FORM";
        public const string SourceBulkConfirmed = "BULK_CONFIRMED";
        public const string SourceManual = "MANUAL";
    }
}
