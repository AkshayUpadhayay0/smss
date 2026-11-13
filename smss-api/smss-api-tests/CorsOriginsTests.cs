using Microsoft.Extensions.Configuration;
using smss_api.Helpers;
using Xunit;

namespace smss_api_tests
{
    public class CorsOriginsTests
    {
        private static IConfiguration Config(params string[] origins) =>
            new ConfigurationBuilder().AddInMemoryCollection(origins.Select((o, i) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", o))).Build();

        [Fact] public void Development_without_config_defaults_to_the_local_dev_server() =>
            Assert.Equal(new[] { "http://localhost:4200" }, StartupConfigGuard.CorsOrigins(Config(), true));

        [Fact] public void Development_with_config_uses_only_the_configured_origins() =>
            Assert.Equal(new[] { "https://app.example.com" }, StartupConfigGuard.CorsOrigins(Config("https://app.example.com"), true));

        [Fact] public void Production_without_config_has_no_origins_and_no_fallback() =>
            Assert.Empty(StartupConfigGuard.CorsOrigins(Config(), false));

        [Fact] public void Wildcard_blank_and_duplicate_entries_are_dropped_and_trailing_slash_removed() =>
            Assert.Equal(new[] { "https://a.example.com" }, StartupConfigGuard.CorsOrigins(Config("*", " ", "https://a.example.com/", "https://A.example.com"), false));

        [Fact] public void Production_with_only_a_wildcard_counts_as_missing() =>
            Assert.Contains(StartupConfigGuard.Missing(Config("*"), false), m => m.StartsWith("Cors:AllowedOrigins"));

        [Fact] public void Development_never_reports_cors_as_missing() =>
            Assert.DoesNotContain(StartupConfigGuard.Missing(Config(), true), m => m.StartsWith("Cors:AllowedOrigins"));
    }
}
