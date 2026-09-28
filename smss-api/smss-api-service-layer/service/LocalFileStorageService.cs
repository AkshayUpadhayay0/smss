using Microsoft.Extensions.Logging;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly string _rootPath;       // absolute folder on disk
        private readonly string _requestPath;    // public URL prefix, e.g. "/uploads"
        private readonly ILogger<LocalFileStorageService> _logger;

        public LocalFileStorageService(string rootPath, string requestPath, ILogger<LocalFileStorageService> logger)
        {
            _rootPath = Path.GetFullPath(rootPath);
            _requestPath = requestPath.TrimEnd('/');
            _logger = logger;
        }

        public async Task<string> SaveSchoolLogoAsync(string schoolCode, Stream content, string extension, CancellationToken ct)
        {
            if (!SchoolCodeRules.IsFolderSafe(schoolCode))
                throw new InvalidOperationException("School code is not a valid folder name.");

            var folder = Path.Combine(_rootPath, schoolCode, "logo");
            Directory.CreateDirectory(folder);

            var fileName = $"logo_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var fullPath = Path.Combine(folder, fileName);

            await using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(fs, ct);

            return $"{_requestPath}/{schoolCode}/logo/{fileName}";
        }

        public void Delete(string relativeUrl)
        {
            try
            {
                if (!relativeUrl.StartsWith(_requestPath + "/", StringComparison.Ordinal)) return;

                var relative = relativeUrl[(_requestPath.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relative));

                // Never delete anything outside the uploads root
                if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return;

                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete file {Url}", relativeUrl);
            }
        }
    }
}