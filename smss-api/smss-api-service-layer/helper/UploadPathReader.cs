namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Turns the value a database row keeps for an uploaded file (school logo, legacy student photo) into the path RELATIVE to the uploads folder, or null when it does not point into it.
    /// The upload code always writes "/uploads/{schoolCode}/logo/{file}", but a row can come from an import, an older release or a hand edit, so a few harmless spellings are understood:
    /// backslashes, no leading slash, a doubled slash, a full http(s) URL, a "?v=3" / "#x" suffix, surrounding spaces, percent-encoding, a different case of the "/uploads" prefix, and an
    /// absolute path inside the uploads folder. Anything with a ".." or "." segment, a drive/stream colon after the prefix, or a NUL is refused here; the caller still checks the final full
    /// path against the uploads root, so this is a convenience in front of the guard, never a replacement for it.
    /// </summary>
    public static class UploadPathReader
    {
        private const char Bs = (char)92;   // backslash


        public static string? RelativeTo(string? stored, string requestPath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            var s = stored.Trim();

            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
                s = uri.AbsolutePath;                      // drops scheme, host, query and fragment
            }
            else
            {
                var cut = s.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) s = s[..cut];
            }

            try { s = Uri.UnescapeDataString(s); } catch { return null; }
            if (s.Contains('\0')) return null;
            s = s.Replace(Bs, '/').Trim();

            // an absolute path inside the uploads folder (C:/.../uploads/School/logo/x.png)
            var root = rootPath.Replace(Bs, '/').TrimEnd('/');
            if (root.Length > 0 && s.StartsWith(root + "/", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return Clean(s[(root.Length + 1)..]);

            while (s.Contains("//")) s = s.Replace("//", "/");
            if (!s.StartsWith('/')) s = "/" + s;
            var prefix = requestPath.TrimEnd('/') + "/";
            if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return Clean(s[prefix.Length..]);
        }

        private static string? Clean(string relative)
        {
            if (relative.Length == 0) return null;
            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            foreach (var p in parts)
                if (p is "." or ".." || p.Contains(':') || p.Trim().Length != p.Length) return null;
            return string.Join('/', parts);
        }
    }
}
