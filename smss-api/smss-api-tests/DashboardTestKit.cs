using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.dto;

namespace smss_api_tests
{
    /// <summary>
    /// Shared helpers for the role dashboards' tests. <see cref="Fake.Of{T}"/> builds an interface stand-in: a method named in <c>answers</c> returns that value (an Exception makes the
    /// Task fail); any other method returns a FAILED ApiResponse (500) when it returns one, so a dashboard part nobody configured is exactly a "part that could not be loaded".
    /// </summary>
    public static class Fake
    {
        public class Proxy : DispatchProxy
        {
            public Dictionary<string, object?> Answers { get; } = new();
            public List<string> Calls { get; } = new();

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                Calls.Add(method!.Name);
                var ret = method.ReturnType;
                if (ret == typeof(Task)) return Task.CompletedTask;
                if (!ret.IsGenericType || ret.GetGenericTypeDefinition() != typeof(Task<>)) return ret.IsValueType ? Activator.CreateInstance(ret) : null;
                var inner = ret.GetGenericArguments()[0];
                object? value;
                if (Answers.TryGetValue(method.Name, out var a))
                {
                    if (a is Exception ex) return FromException(inner, ex);
                    value = a is Func<object?[], object?> f ? f(args!) : a;
                }
                else if (inner.IsGenericType && inner.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                {
                    value = Activator.CreateInstance(inner);
                    inner.GetProperty("Status")!.SetValue(value, false);
                    inner.GetProperty("StatusCode")!.SetValue(value, 500);
                    inner.GetProperty("Message")!.SetValue(value, "not configured in the test");
                }
                else value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { value });
            }

            private static object FromException(Type inner, Exception ex) =>
                typeof(Task).GetMethod(nameof(Task.FromException), 1, new[] { typeof(Exception) })!.MakeGenericMethod(inner).Invoke(null, new object[] { ex })!;
        }

        public static T Of<T>(Dictionary<string, object?>? answers = null) where T : class
        {
            var t = DispatchProxy.Create<T, Proxy>();
            var p = (Proxy)(object)t;
            if (answers != null) foreach (var kv in answers) p.Answers[kv.Key] = kv.Value;
            return t;
        }

        public static Proxy ProxyOf(object fake) => (Proxy)fake;

        public static ApiResponse<T> Ok<T>(T data) => new() { Status = true, StatusCode = 200, Message = "ok", Data = data };

        public static ClaimsPrincipal User(string? school, long userId, params string[] roles)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
            if (school != null) claims.Add(new Claim("school_id", school));
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        public static T WithUser<T>(T controller, ClaimsPrincipal user) where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
            return controller;
        }

        public static int Code(IActionResult r) => r is StatusCodeResult s ? s.StatusCode : ((ObjectResult)r).StatusCode ?? 200;

        public static readonly string[] AllRoles = { "Super Admin", "School Admin", "Principal", "Teacher", "Accountant", "Admission Staff", "Student", "Guardian" };
    }
}
