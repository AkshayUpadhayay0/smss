using Microsoft.Extensions.Logging;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Item photos in the PRIVATE store (the same private root as employee documents and homework attachments, which is never served by URL).</summary>
    public class InventoryFileStore : IInventoryFileStore
    {
        private readonly string _root;
        private readonly ILogger<InventoryFileStore> _logger;

        public InventoryFileStore(string privateRootPath, ILogger<InventoryFileStore> logger)
        {
            _root = Path.GetFullPath(privateRootPath);
            _logger = logger;
        }

        public async Task<string> SaveItemPhotoAsync(string schoolCode, long itemId, Stream content, string extension, CancellationToken ct)
        {
            if (!SchoolCodeRules.IsFolderSafe(schoolCode)) throw new InvalidOperationException("School code is not a valid folder name.");
            if (itemId <= 0) throw new InvalidOperationException("Item id is not valid.");
            if (!LogoRules.AllowedExtensions.Contains(extension)) throw new InvalidOperationException("Extension is not allowed.");
            var folder = Path.Combine(_root, schoolCode, "inventory", "items", itemId.ToString());
            Directory.CreateDirectory(folder);
            var fileName = $"photo_{Guid.NewGuid():N}{extension}";   // a new name per upload: an old URL/blob can never show a replaced photo
            await using var fs = new FileStream(Path.Combine(folder, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(fs, ct);
            return $"{schoolCode}/inventory/items/{itemId}/{fileName}";
        }

        private string? Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;   // never outside the private root
        }

        public Stream? Open(string key)
        {
            try
            {
                var full = Resolve(key);
                return full != null && File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
            }
            catch (Exception ex) { _logger.LogError(ex, "Could not open inventory photo {Key}", key); return null; }
        }

        public void Delete(string key)
        {
            try { var full = Resolve(key); if (full != null && File.Exists(full)) File.Delete(full); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete inventory photo {Key}", key); }
        }
    }
}
