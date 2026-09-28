using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    public static class SchoolCodeRules
    {
        // Folder-safe: the school code is used as a directory name for uploads
        public const string Pattern = @"^[A-Za-z0-9_-]+$";
        public const string Message = "School code can contain only letters, numbers, hyphen and underscore.";

        public static bool IsFolderSafe(string? code) =>
            !string.IsNullOrEmpty(code) && System.Text.RegularExpressions.Regex.IsMatch(code, Pattern);
    }

    public static class LogoRules
    {
        public const long MaxBytes = 2 * 1024 * 1024;   // 2 MB
        public static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

        // The extension is client-controlled, so also check the file's real signature
        public static bool SignatureMatches(string extension, byte[] b)
        {
            return extension switch
            {
                ".png" => b.Length >= 8
                          && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                          && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,
                ".jpg" or ".jpeg" => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
                ".webp" => b.Length >= 12
                           && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46      // "RIFF"
                           && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,   // "WEBP"
                _ => false
            };
        }
    }
}
