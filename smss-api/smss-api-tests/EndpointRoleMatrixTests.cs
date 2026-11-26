using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api.Controllers;
using smss_api.Helpers;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// WHO CAN REACH WHAT (the behavioural half of the endpoint authorization guard). For every signed-in controller action the test calls it, with fake services behind it, as a signed-in user holding ONLY one role,
    /// and records whether the call got through to the service layer. The result (which roles get through, per action) is compared with the golden file endpoint-role-matrix.txt: a NEW endpoint that a Student,
    /// a Guardian or any other role can reach shows up as a difference and fails the build until the golden file is updated on purpose (SMSS_WRITE_GOLDEN=1 rewrites it). "Reaches the service" means the
    /// controller let the call through; a service may still refuse (own-record checks, the access helper): those endpoints are the "my ..." ones and are reviewed in Docs/endpoint-authorization-audit.md.
    /// </summary>
    public class EndpointRoleMatrixTests
    {
        private static readonly string[] Roles = { "Student", "Guardian", "Teacher", "Accountant", "Admission Staff", "Store Keeper", "Driver", "Principal", "School Admin" };

        private static string GoldenPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "smss-api-tests"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "smss-api-tests", "golden", "endpoint-role-matrix.txt");
        }

        private static object? Make(Type t, List<object> fakes, int depth = 0)
        {
            if (t == typeof(string)) return "x";
            if (t == typeof(CancellationToken)) return default(CancellationToken);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ILogger<>)) return typeof(NullLogger<>).MakeGenericType(t.GetGenericArguments()).GetField("Instance")!.GetValue(null);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IOptions<>)) return typeof(Options).GetMethod("Create")!.MakeGenericMethod(t.GetGenericArguments()).Invoke(null, new[] { Make(t.GetGenericArguments()[0], fakes, depth + 1) });
            if (t == typeof(IConfiguration)) return new ConfigurationBuilder().Build();
            if (t.IsInterface)
            {
                var f = typeof(Fake).GetMethod("Of")!.MakeGenericMethod(t).Invoke(null, new object?[] { null })!;
                fakes.Add(f);
                return f;
            }
            if (Nullable.GetUnderlyingType(t) is { } u) return u == typeof(DateOnly) ? DateOnly.FromDateTime(DateTime.UtcNow) : u == typeof(bool) ? false : u.IsPrimitive ? Convert.ChangeType(1, u) : Activator.CreateInstance(u);
            if (t.IsValueType) return t == typeof(DateOnly) ? DateOnly.FromDateTime(DateTime.UtcNow) : t == typeof(DateTime) ? DateTime.UtcNow : Nullable.GetUnderlyingType(t) != null ? null : Activator.CreateInstance(t);
            if (t.IsArray) return Array.CreateInstance(t.GetElementType()!, 0);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Activator.CreateInstance(t);
            if (depth > 2) return null;
            var ctor = t.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor == null) return null;
            try { return ctor.Invoke(ctor.GetParameters().Select(p => Make(p.ParameterType, fakes, depth + 1)).ToArray()); } catch { return null; }
        }

        private static ClaimsPrincipal User(string role) => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim("school_id", "SCH1"), new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "Test"), new Claim("user_type", role), new Claim("guardian_id", "1"),
        }, "test"));

        private static async Task<string> TryAsync(Type controller, MethodInfo action, string role)
        {
            var fakes = new List<object>();
            ControllerBase c;
            try { c = (ControllerBase)Make(controller, fakes)!; } catch { return "error"; }
            if (c == null) return "error";
            c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(role) } };
            var args = action.GetParameters().Select(p => p.ParameterType.IsAssignableTo(typeof(IFormFile)) ? new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "file", "a.png") : p.Name == "schoolId" ? "SCH1" : Make(p.ParameterType, fakes)).ToArray();   // a route schoolId is the token's own school, so the controller's school check passes and the ROLE check is what is measured
            object? result = null;
            try
            {
                result = action.Invoke(c, args);
                if (result is Task task) { await task; result = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null; }
            }
            catch { /* a null deeper down after the service was reached is still "reached" */ }
            var reached = fakes.Any(f => Fake.ProxyOf(f).Calls.Count > 0);
            if (result is IActionResult r)
            {
                var code = r switch { StatusCodeResult s => s.StatusCode, ObjectResult o => o.StatusCode ?? 200, ForbidResult => 403, _ => 0 };
                if (code == 403 && !reached) return "403";
            }
            return reached ? "REACHES" : "stopped";
        }

        private static bool NeedsAuth(Type t, MethodInfo m) =>
            !(m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any() || t.GetCustomAttributes<AllowAnonymousAttribute>(true).Any())
            && m.GetCustomAttributes(true).Concat(t.GetCustomAttributes(true)).Any(a => a is IAuthorizeData) && !m.GetCustomAttributes(true).Concat(t.GetCustomAttributes(true)).Any(a => a is SuperAdminOnlyAttribute);

        [Fact]
        public async Task The_roles_that_can_reach_each_signed_in_action_are_exactly_the_golden_matrix()
        {
            var lines = new List<string>();
            var actions = typeof(AuthController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName && m.GetCustomAttributes(true).Any(a => a is IActionHttpMethodProvider)).Select(m => (t, m)))
                .Where(x => NeedsAuth(x.t, x.m)).OrderBy(x => x.t.Name, StringComparer.Ordinal).ThenBy(x => x.m.Name, StringComparer.Ordinal).ThenBy(x => string.Join(",", x.m.GetParameters().Select(p => p.ParameterType.Name)), StringComparer.Ordinal).ToList();
            Assert.True(actions.Count > 300, "the scan is broken: " + actions.Count);
            foreach (var (t, m) in actions)
            {
                var reach = new List<string>(); var error = false;
                foreach (var role in Roles)
                {
                    var r = await TryAsync(t, m, role);
                    if (r == "REACHES") reach.Add(role); else if (r == "error") error = true;
                }
                lines.Add($"{t.Name}.{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))}) => {(error ? "ERROR " : "")}{(reach.Count == 0 ? "-" : string.Join("|", reach))}");
            }
            var path = GoldenPath();
            if (Environment.GetEnvironmentVariable("SMSS_WRITE_GOLDEN") == "1") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllLines(path, lines); return; }
            Assert.True(File.Exists(path), "golden file missing: " + path);
            var golden = File.ReadAllLines(path);
            var added = lines.Except(golden).ToList(); var removed = golden.Except(lines).ToList();
            Assert.True(added.Count == 0 && removed.Count == 0, "Who can reach what has CHANGED. New or changed:\n" + string.Join("\n", added.Take(40)) + "\nGone or changed from:\n" + string.Join("\n", removed.Take(40)) + "\n(review, then rewrite the golden file with SMSS_WRITE_GOLDEN=1)");
        }

        [Fact]
        public async Task A_Student_or_a_Guardian_signed_in_cannot_reach_the_school_staff_actions_by_name()
        {
            // a few staff-only writes that must be closed to both, named on purpose so the intent survives a golden-file rewrite
            var staffOnly = new (Type T, string A)[]
            {
                (typeof(PromotionController), "Execute"), (typeof(StudentLeavingController), "Record"), (typeof(StudentLeavingController), "Correct"), (typeof(TransferCertificatesController), "Issue"),
                (typeof(TransferCertificatesController), "Void"), (typeof(SectionChangesController), "Change"), (typeof(ClassTeacherController), "Assign"), (typeof(GuardiansController), "Merge"),
            };
            foreach (var (t, a) in staffOnly)
            {
                var m = t.GetMethods().First(x => x.Name == a);
                foreach (var role in new[] { "Student", "Guardian", "Teacher", "Accountant", "Store Keeper", "Driver" })
                    Assert.NotEqual("REACHES", await TryAsync(t, m, role));
            }
        }
    }
}
