using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class DriverPhotoService : IDriverPhotoService
    {
        private readonly IDriverRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly ILogger<DriverPhotoService> _logger;

        public DriverPhotoService(IDriverRepository repo, IFileStorageService storage, ILogger<DriverPhotoService> logger) { _repo = repo; _storage = storage; _logger = logger; }

        private static StudentPhotoResult Fail(int code, string message) => new() { StatusCode = code, Message = message };

        public async Task<StudentPhotoResult> GetAsync(string? schoolId, string driverId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var id = (driverId ?? string.Empty).Trim();
                // another school's driver (or one that does not exist) is a 404: ids cannot be probed across schools
                var (exists, stored) = await _repo.GetPhotoKeyAsync(schoolId, id, ct);
                if (!exists) return Fail(404, "Driver not found.");
                if (string.IsNullOrEmpty(stored)) return Fail(404, "This driver has no photo.");
                var stream = _storage.OpenPhoto(stored);
                if (stream == null) return Fail(404, "The photo file could not be found.");
                return new StudentPhotoResult { Content = stream, ContentType = StudentPhotos.ContentType(stored), ETag = StudentPhotos.ETag(stored) };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading a driver photo");
                return Fail(500, "Something went wrong. Please try again later.");
            }
        }
    }
}
