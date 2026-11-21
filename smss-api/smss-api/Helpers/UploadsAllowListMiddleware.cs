using System.Text.RegularExpressions;

namespace smss_api.Helpers
{
    /// <summary>
    /// The ONLY thing the public /uploads URL space serves: a school logo, /uploads/{schoolCode}/logo/{file} with a png / jpg / jpeg / webp file name.
    /// Everything else under /uploads answers 404 (other folders, directory requests, the bare /uploads, odd or encoded spellings, path traversal),
    /// so a file of any other kind that ever lands in the public folder is still never reachable. Private files live in the separate private store
    /// and are served only by authenticated endpoints. Must run BEFORE UseStaticFiles.
    /// </summary>
    public sealed class UploadsAllowListMiddleware
    {
        // Letters, digits, '-' and '_' for the school code (SchoolCodeRules.Pattern); a plain file name with one of the four image extensions.
        // No '%', '\', '/' or ':' can match, so encoded and double-encoded variants fall out as well.
        private static readonly Regex LogoPath = new(@"^/uploads/[A-Za-z0-9_-]+/logo/[A-Za-z0-9_-][A-Za-z0-9._-]*\.(png|jpe?g|webp)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

        private const string Prefix = "/uploads";   // the public request path of the uploads folder (Program.cs: uploadsRequestPath)

        private readonly RequestDelegate _next;

        public UploadsAllowListMiddleware(RequestDelegate next) => _next = next;

        /// <summary>True when this path may be served from the public uploads folder.</summary>
        public static bool IsAllowedLogoPath(string? path) =>
            !string.IsNullOrEmpty(path) && !path.Contains("..", StringComparison.Ordinal) && LogoPath.IsMatch(path);

        public Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var underUploads = path.Equals(Prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase);
            if (underUploads && !IsAllowedLogoPath(path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            return _next(context);
        }
    }
}
