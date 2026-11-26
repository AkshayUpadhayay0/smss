namespace smss_api_service_layer.helper
{
    /// <summary>The template codes (also what the delivery log stores). The names Meta knows are WhatsAppOptions settings that default to these. The text and variables are in Docs/whatsapp-templates.md; a test keeps the two in step.</summary>
    public static class ParentTemplateCodes
    {
        public const string AccountReady = "parent_account_ready";
        public const string ChildAdded = "parent_child_added";
        public const string LoginHelp = "parent_login_help";
        public const string StudentLoginReady = "parent_student_login_ready";
        public static readonly string[] All = { AccountReady, ChildAdded, LoginHelp, StudentLoginReady };
    }

    /// <summary>What a purpose is called in the delivery log (the resend limit counts deliveries per guardian and purpose).</summary>
    public static class MessagePurposes
    {
        public const string SetPassword = "SET_PASSWORD";
        public const string ResetPassword = "RESET_PASSWORD";
        public const string ChildAdded = "CHILD_ADDED";
        public const string StudentLogin = "STUDENT_LOGIN";
    }

    /// <summary>The variables of one WhatsApp template: the body variables in order, and the value of the button's URL variable (the token) when the template has one. Built by <see cref="ParentTemplates"/>.</summary>
    public sealed record WhatsAppContent(string TemplateCode, IReadOnlyList<string> BodyParameters, string? ButtonParameter);

    /// <summary>The ONE place that knows each template's variable order. Every template carries the school's name, so a message sent from the platform number still says which school it is from.</summary>
    public static class ParentTemplates
    {
        /// <summary>parent_account_ready: {{1}} guardian, {{2}} child, {{3}} school, {{4}} login id, {{5}} hours the link works; button = the set-password token.</summary>
        public static WhatsAppContent AccountReady(string guardianName, string childName, string schoolName, string loginId, int linkHours, string token) =>
            new(ParentTemplateCodes.AccountReady, new[] { guardianName, childName, schoolName, loginId, linkHours.ToString() }, token);

        /// <summary>parent_child_added: {{1}} guardian, {{2}} child, {{3}} school, {{4}} login id; button = a fixed link to the login page (no variable).</summary>
        public static WhatsAppContent ChildAdded(string guardianName, string childName, string schoolName, string loginId) =>
            new(ParentTemplateCodes.ChildAdded, new[] { guardianName, childName, schoolName, loginId }, null);

        /// <summary>parent_login_help (login help: the login id and a reset link): {{1}} guardian, {{2}} school, {{3}} login id, {{4}} minutes the link works; button = the reset token.</summary>
        public static WhatsAppContent LoginHelp(string guardianName, string schoolName, string loginId, int linkMinutes, string token) =>
            new(ParentTemplateCodes.LoginHelp, new[] { guardianName, schoolName, loginId, linkMinutes.ToString() }, token);

        /// <summary>parent_student_login_ready: {{1}} guardian, {{2}} student, {{3}} school, {{4}} the student's login id, {{5}} hours the link works; button = the set-password token.</summary>
        public static WhatsAppContent StudentLoginReady(string guardianName, string studentName, string schoolName, string studentLoginId, int linkHours, string token) =>
            new(ParentTemplateCodes.StudentLoginReady, new[] { guardianName, studentName, schoolName, studentLoginId, linkHours.ToString() }, token);

        /// <summary>How many body variables each template has (the documentation must agree).</summary>
        public static int BodyVariableCount(string code) => code switch
        {
            ParentTemplateCodes.AccountReady => 5,
            ParentTemplateCodes.ChildAdded => 4,
            ParentTemplateCodes.LoginHelp => 4,
            ParentTemplateCodes.StudentLoginReady => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
    }
}
