using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using smss_api.Controllers;
using smss_api.Helpers;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The endpoint authorization guard. EVERY controller action must carry explicit authorization: [Authorize] (on the class or the action) or the SuperAdminOnly filter. An action that is anonymous
    /// ([AllowAnonymous], or no authorization at all) fails this test unless it is in the allow-list below WITH a reason. The allow-list is also checked the other way round: an entry that is no longer
    /// anonymous (or no longer exists) fails, so the list cannot rot. Adding an endpoint without authorization therefore breaks the build.
    /// </summary>
    public class EndpointAuthorizationGuardTests
    {
        /// <summary>The ONLY actions that may be anonymous, each with why.</summary>
        private static readonly Dictionary<string, string> AllowedAnonymous = new()
        {
            ["AuthController.Login"] = "Signing in: the caller has no token yet. Guarded by the sign-in throttle (per login id and per address).",
            ["AuthController.Refresh"] = "Trading a refresh token for a new access token: the caller's access token has expired. The single-use refresh token is the credential.",
            ["AuthController.Logout"] = "Ends a session with the refresh token in the body (works after the 15-minute access token expired). The refresh token is the credential; it can only revoke its own session.",
            ["HomeController.Get"] = "A one-line greeting (liveness check) with no data at all.",
            ["SystemSetupController.BootstrapSuperAdmin"] = "One-time first Super Admin. Answers 404 unless Bootstrap:Enabled is true (false everywhere after setup), needs the X-Setup-Key header (constant-time compared), and refuses once a Super Admin exists.",
            ["ParentAuthController.CheckLink"] = "Public parent set-password page: asks whether the one-time link in the message is still valid. The hashed single-use token is the credential; per-address rate limit.",
            ["ParentAuthController.SetPassword"] = "Public parent set-password AND reset-password page (one endpoint, the token's purpose decides): sets the password with the one-time link token. Single use, expires, per-address rate limit.",
            ["ParentAuthController.Forgot"] = "Public forgot-ID-or-password: the parent has no login. Same answer for any contact (no account is revealed); sending is queued after the answer; per-address and per-contact limits.",
            ["PublicEnquiriesController.Submit"] = "The public Contact Us / Request a Demo form on the marketing site. Honeypot, validation and a per-address rate limit; stores nothing but the enquiry.",
            ["PublicPartnerSchoolsController.List"] = "The public landing page's partner-schools strip: only Active schools the Super Admin switched on, only name, logo, city and state. Cached, rate limited.",
            ["WhatsAppWebhookController.Verify"] = "Meta calls this once to register the webhook: guarded by the verify token. Answers 404 unless WhatsApp is configured (AppSecret set).",
            ["WhatsAppWebhookController.Receive"] = "Meta posts delivery statuses here: guarded by the X-Hub-Signature-256 HMAC on every call. Answers 404 unless configured; changes only the status of a delivery-log row.",
            ["DriverDutyController.Location"] = "A driver's phone sends its location with the duty TRACKING TOKEN (X-Duty-Token), never the login: the background task must not use the JWT. The token is single-session, hashed, and replaced on every start.",
            ["DriverDutyController.End"] = "Ends a duty with the duty TRACKING TOKEN (X-Duty-Token) when the login is gone; same token as the location ping.",
        };

        private static IEnumerable<Type> Controllers() =>
            typeof(AuthController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);

        private static IEnumerable<MethodInfo> Actions(Type t) =>
            t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttributes(true).Any(a => a is IActionHttpMethodProvider or RouteAttribute));

        private static bool IsAnonymous(Type t, MethodInfo m) => m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any() || t.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();

        private static bool HasAuthorization(Type t, MethodInfo m) =>
            m.GetCustomAttributes(true).Concat(t.GetCustomAttributes(true)).Any(a => a is IAuthorizeData or SuperAdminOnlyAttribute);

        private static string Key(Type t, MethodInfo m) => $"{t.Name}.{m.Name}";

        private static List<(Type Type, MethodInfo Method)> All() => Controllers().SelectMany(t => Actions(t).Select(m => (t, m))).ToList();

        [Fact]
        public void Every_action_has_explicit_authorization_or_is_on_the_allow_list_with_a_reason()
        {
            var all = All();
            Assert.True(all.Count > 300, $"expected hundreds of actions, found {all.Count}: the scan is broken");
            var open = all.Where(a => IsAnonymous(a.Type, a.Method) || !HasAuthorization(a.Type, a.Method)).Select(a => Key(a.Type, a.Method)).OrderBy(x => x).ToList();
            var unlisted = open.Where(k => !AllowedAnonymous.ContainsKey(k)).ToList();
            Assert.True(unlisted.Count == 0, "Actions with no authorization that are not on the allow-list: " + string.Join(", ", unlisted));
            var stale = AllowedAnonymous.Keys.Where(k => !open.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "Allow-list entries that no longer exist or are no longer anonymous (remove them): " + string.Join(", ", stale));
            Assert.All(AllowedAnonymous.Values, reason => Assert.True(reason.Length > 25));
        }

        [Fact]
        public void Every_non_anonymous_action_that_writes_is_a_POST_PUT_or_DELETE_never_a_GET()
        {
            // a GET must be safe to repeat and to prefetch; the few GETs that record an audit row (a certificate print, a notice read) are listed on purpose
            var writeNames = new[] { "Create", "Update", "Delete", "Remove", "Toggle", "Approve", "Reject", "Cancel", "Void", "Issue", "Publish", "Mark", "Assign", "Reset", "Confirm" };
            var offenders = All().Where(a => a.Method.GetCustomAttributes<HttpGetAttribute>(true).Any() && writeNames.Any(w => a.Method.Name.StartsWith(w, StringComparison.Ordinal)))
                .Select(a => Key(a.Type, a.Method)).OrderBy(x => x).ToList();
            Assert.True(offenders.Count == 0, "GET actions that look like writes: " + string.Join(", ", offenders));
        }

        [Fact]
        public void The_audit_can_be_dumped_for_review()
        {
            var path = Environment.GetEnvironmentVariable("SMSS_AUDIT_OUT");
            if (string.IsNullOrWhiteSpace(path)) return;
            var lines = new List<string> { "controller\taction\tverbs\troute\tauthorization\tid-parameters\tuses-token-school" };
            foreach (var (t, m) in All().OrderBy(a => a.Type.Name).ThenBy(a => a.Method.Name))
            {
                var verbs = string.Join("/", m.GetCustomAttributes<HttpMethodAttribute>(true).SelectMany(a => a.HttpMethods).Distinct());
                var route = (t.GetCustomAttribute<RouteAttribute>()?.Template ?? "") + "/" + (m.GetCustomAttributes<HttpMethodAttribute>(true).Select(a => a.Template).FirstOrDefault(x => x != null) ?? "");
                var auth = IsAnonymous(t, m) ? "ANONYMOUS" : t.GetCustomAttributes(true).Concat(m.GetCustomAttributes(true)).OfType<IAuthorizeData>().Any(a => !string.IsNullOrEmpty(a.Roles) || !string.IsNullOrEmpty(a.Policy)) ? "AUTHORIZE(roles/policy)"
                    : m.GetCustomAttributes(true).Concat(t.GetCustomAttributes(true)).Any(a => a is SuperAdminOnlyAttribute) ? "SUPERADMIN" : HasAuthorization(t, m) ? "AUTHORIZE" : "NONE";
                var ids = string.Join(",", m.GetParameters().Where(p => p.Name != null && p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name));
                lines.Add($"{t.Name}\t{m.Name}\t{verbs}\t{route}\t{auth}\t{ids}\t");
            }
            File.WriteAllLines(path, lines);
        }
    }
}
