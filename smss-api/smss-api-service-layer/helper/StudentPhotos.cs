using System.Security.Cryptography;
using System.Text;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Student photos live in PRIVATE storage and are never reachable by a public URL. The database keeps the private storage key in profile_photo_url
    /// ({schoolCode}/students/{studentId}/photo_<timestamp><ext>); the API never hands that key out. What every DTO carries instead is the path of the
    /// authenticated read endpoint (GET /api/Student/{studentId}/photo?v=...), where v is a short fingerprint of the stored key so a replaced photo gets a new URL.
    /// </summary>
    public static class StudentPhotos
    {
        public static string? ApiPath(string studentId, string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            var v = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(stored)))[..10].ToLowerInvariant();
            return $"/api/Student/{studentId}/photo?v={v}";
        }

        public static string ETag(string stored) => "\"" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(stored)))[..16].ToLowerInvariant() + "\"";

        public static string ContentType(string stored) => Path.GetExtension(stored).ToLowerInvariant() switch
        {
            ".png" => "image/png", ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream"
        };
    }

    /// <summary>Driver photos: private storage, the database keeps the key, DTOs carry the authenticated path.</summary>
    public static class DriverPhotos
    {
        public static string? ApiPath(string driverId, string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            var v = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(stored)))[..10].ToLowerInvariant();
            return $"/api/Drivers/{driverId}/photo?v={v}";
        }
    }

    /// <summary>Employee photos follow the same rule as student photos: private storage, the database keeps the key, DTOs carry the authenticated path.</summary>
    public static class EmployeePhotos
    {
        public static string? ApiPath(string employeeId, string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            var v = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(stored)))[..10].ToLowerInvariant();
            return $"/api/Employee/{employeeId}/photo?v={v}";
        }
    }
}
