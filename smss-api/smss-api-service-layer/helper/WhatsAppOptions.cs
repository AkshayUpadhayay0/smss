namespace smss_api_service_layer.helper
{
    /// <summary>
    /// WhatsApp settings (section "WhatsApp"). EVERY secret is read only from user-secrets / environment variables and is empty in the committed appsettings.json (the tracked-config test fails the build otherwise):
    /// AccessToken, AppSecret and WebhookVerifyToken are never logged and never stored. Everything is OFF by default: with Enabled false (or Sender "Log", or no token / phone number id) the log-only sender is used and
    /// nothing leaves the server. The template names are settings so a template renamed in Meta is a configuration change.
    /// </summary>
    public class WhatsAppOptions
    {
        public const string Section = "WhatsApp";

        /// <summary>The master switch. Off = nothing is ever sent to Meta and the webhook answers 404.</summary>
        public bool Enabled { get; set; }
        /// <summary>"Log" (default: writes a delivery row, sends nothing) or "Cloud" (the Meta WhatsApp Cloud API; needs Enabled, AccessToken and PhoneNumberId).</summary>
        public string Sender { get; set; } = "Log";
        public string? AccessToken { get; set; }
        /// <summary>The platform's default sender (Meta phone number id); a school's own number is used only once its sender status is ACTIVE.</summary>
        public string? PhoneNumberId { get; set; }
        public string? BusinessAccountId { get; set; }
        public string ApiVersion { get; set; } = "v21.0";
        public string BaseUrl { get; set; } = "https://graph.facebook.com";
        /// <summary>The webhook's signature secret (X-Hub-Signature-256). Without it the webhook is disabled.</summary>
        public string? AppSecret { get; set; }
        public string? WebhookVerifyToken { get; set; }
        public string Language { get; set; } = "en";
        public int TimeoutSeconds { get; set; } = 15;

        public string TemplateParentAccountReady { get; set; } = ParentTemplateCodes.AccountReady;
        public string TemplateParentChildAdded { get; set; } = ParentTemplateCodes.ChildAdded;
        public string TemplateParentLoginCode { get; set; } = ParentTemplateCodes.LoginCode;
        public string TemplateParentStudentLoginReady { get; set; } = ParentTemplateCodes.StudentLoginReady;

        public bool IsCloudSender => Enabled && string.Equals(Sender, "Cloud", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
        public bool WebhookEnabled => Enabled && !string.IsNullOrWhiteSpace(AppSecret);

        /// <summary>The template name Meta knows for one of our template codes (the code itself when no override is set).</summary>
        public string MetaNameOf(string code) => code switch
        {
            ParentTemplateCodes.AccountReady => Pick(TemplateParentAccountReady, code),
            ParentTemplateCodes.ChildAdded => Pick(TemplateParentChildAdded, code),
            ParentTemplateCodes.LoginCode => Pick(TemplateParentLoginCode, code),
            ParentTemplateCodes.StudentLoginReady => Pick(TemplateParentStudentLoginReady, code),
            _ => code,
        };
        private static string Pick(string? configured, string code) => string.IsNullOrWhiteSpace(configured) ? code : configured.Trim();
    }

    /// <summary>The ParentAccounts settings (section "ParentAccounts"): link lifetimes, the resend limit and where the links point.</summary>
    public class ParentAccountsOptions
    {
        public const string Section = "ParentAccounts";
        public int SetPasswordLinkHours { get; set; } = 72;
        public int ResetLinkMinutes { get; set; } = 60;
        public int ResendLimitPer24Hours { get; set; } = 3;
        /// <summary>The website's address the links in the messages point at (for example https://smss-erp.digital). Empty by default; the parent-account features refuse to build a link without it.</summary>
        public string? WebBaseUrl { get; set; }
    }
}
