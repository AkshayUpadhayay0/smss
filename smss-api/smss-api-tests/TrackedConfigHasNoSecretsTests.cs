using System.Text.Json;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Guard against committing a secret again (it happened on 15 Nov 2026): every committed appsettings*.json must hold an EMPTY value
    /// under any key whose name looks like a secret. Real values live in user-secrets (Development) / environment variables (server).
    /// The failure message names the key and file, never the value.
    /// </summary>
    public class TrackedConfigHasNoSecretsTests
    {
        private static readonly string[] SecretWords = { "password", "secret", "key", "token", "connectionstring", "defaultconnection", "credential", "pwd" };
        // names that contain a secret word but are not secrets
        private static readonly string[] NotSecret = { "MaxTrackedKeys", "BulkMax" };

        private static string ApiProjectFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "smss-api", "smss-api", "appsettings.json"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "smss-api", "smss-api");
        }

        private static IEnumerable<(string Path, JsonElement Value)> Leaves(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) foreach (var l in Leaves(p.Value, path.Length == 0 ? p.Name : path + ":" + p.Name)) yield return l;
            else if (e.ValueKind == JsonValueKind.Array) { var i = 0; foreach (var v in e.EnumerateArray()) foreach (var l in Leaves(v, $"{path}:{i++}")) yield return l; }
            else yield return (path, e);
        }

        public static IEnumerable<object[]> ConfigFiles() =>
            Directory.GetFiles(ApiProjectFolder(), "appsettings*.json").Select(f => new object[] { Path.GetFileName(f) });

        [Theory, MemberData(nameof(ConfigFiles))]
        public void Committed_settings_hold_no_secret_values(string file)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ApiProjectFolder(), file)), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var offenders = Leaves(doc.RootElement, "")
                .Where(l => l.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(l.Value.GetString()))
                .Where(l => { var name = l.Path.Split(':')[^1]; return !NotSecret.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)) && SecretWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)); })
                .Select(l => l.Path).ToList();
            Assert.True(offenders.Count == 0, $"{file} holds a value for: {string.Join(", ", offenders)}. Keep it empty and set it in user-secrets / an environment variable.");
        }
    }
}
