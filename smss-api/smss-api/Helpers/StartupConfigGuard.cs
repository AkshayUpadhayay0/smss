namespace smss_api.Helpers
{
    /// <summary>
    /// Fails fast at startup when a required setting is missing, naming the SETTING (never its value). The committed appsettings.json carries empty placeholders for every secret;
    /// the real values come from user-secrets (development) or environment variables (production, "Section__Key" form). See Docs/configuration.md.
    /// </summary>
    public static class StartupConfigGuard
    {
        /// <summary>Setting name, why it is needed, and whether it is needed in Development too (without it the app cannot do anything useful).</summary>
        private sealed record Required(string Key, string Why, bool EvenInDevelopment, Func<IConfiguration, bool>? Applies = null, int MinLength = 1);

        private static readonly Required[] Settings =
        {
            new("ConnectionStrings:DefaultConnection", "the PostgreSQL connection string", true),
            new("Jwt:Key", "the JWT signing key (at least 32 characters)", true, MinLength: 32),
            new("Jwt:Issuer", "the JWT issuer", true),
            new("Jwt:Audience", "the JWT audience", true),
            new("Email:SenderEmail", "the SMTP sender address", false),
            new("Email:SenderAppPassword", "the SMTP app password (without it no credentials e-mail can be sent)", false),
            new("Bootstrap:SuperAdminSetupKey", "the one-time Super Admin setup key (needed only while Bootstrap:Enabled is true)", false, c => c.GetValue<bool>("Bootstrap:Enabled")),
        };

        public static bool IsMissing(string? value, int minLength = 1) =>
            string.IsNullOrWhiteSpace(value) || value.Trim().Length < minLength || value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase) || value.StartsWith("<");

        /// <summary>Names of the settings that are missing or still a placeholder, for the given environment. Pure: no values are returned or logged.</summary>
        public static List<string> Missing(IConfiguration config, bool isDevelopment) =>
            Settings.Where(s => (s.EvenInDevelopment || !isDevelopment) && (s.Applies == null || s.Applies(config)) && IsMissing(config[s.Key], s.MinLength))
                    .Select(s => $"{s.Key} ({s.Why})").ToList();

        public static void Validate(IConfiguration config, IHostEnvironment env)
        {
            var missing = Missing(config, env.IsDevelopment());
            if (missing.Count == 0) return;
            var how = env.IsDevelopment()
                ? "Set them with: dotnet user-secrets set \"<Setting>\" \"<value>\" --project smss-api/smss-api  (see Docs/configuration.md)."
                : "Set them as environment variables, with \"__\" for \":\" (for example Jwt__Key). See Docs/configuration.md.";
            throw new InvalidOperationException($"The application cannot start: required configuration is missing or still a placeholder:{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", missing)}{Environment.NewLine}{how}");
        }
    }
}
