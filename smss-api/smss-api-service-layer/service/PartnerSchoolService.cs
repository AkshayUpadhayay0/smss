using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class PartnerSchoolService : IPartnerSchoolService
    {
        public const int DefaultMaxItems = 60;
        public const int DefaultCacheSeconds = 300;

        private readonly IPartnerSchoolRepository _repo;
        private readonly IMemoryCache _cache;
        private readonly IPartnerSchoolCache _listCache;
        private readonly ILogger<PartnerSchoolService> _logger;
        private readonly int _max;
        private readonly TimeSpan _ttl;

        public PartnerSchoolService(IPartnerSchoolRepository repo, IMemoryCache cache, IPartnerSchoolCache listCache, IConfiguration config, ILogger<PartnerSchoolService> logger)
        {
            _repo = repo;
            _cache = cache;
            _listCache = listCache;
            _logger = logger;
            _max = Math.Clamp(IntOf(config, "PartnerSchools:MaxItems", DefaultMaxItems), 1, 200);
            _ttl = TimeSpan.FromSeconds(CacheSecondsOf(config));
        }

        /// <summary>How long the server keeps the list; the controller also sends it as the browser's Cache-Control max-age.</summary>
        public static int CacheSecondsOf(IConfiguration config) => Math.Clamp(IntOf(config, "PartnerSchools:CacheSeconds", DefaultCacheSeconds), 0, 3600);

        private static int IntOf(IConfiguration config, string key, int fallback) =>
            int.TryParse(config[key], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public async Task<ApiResponse<List<PartnerSchoolDto>>> GetPublicListAsync(CancellationToken ct)
        {
            try
            {
                if (_ttl > TimeSpan.Zero && _cache.TryGetValue(PartnerSchoolCache.CacheKey, out List<PartnerSchoolDto>? cached) && cached != null)
                    return Result(200, "Successfully fetched", cached);

                var rows = await _repo.GetListedAsync(StatusNames.Active, StatusNames.GeneralType, _max, ct);
                var list = rows.Select(r => new PartnerSchoolDto { Name = r.Name, LogoUrl = r.LogoUrl, City = r.City, State = r.State }).ToList();
                if (_ttl > TimeSpan.Zero) _cache.Set(PartnerSchoolCache.CacheKey, list, _ttl);
                return Result(200, "Successfully fetched", list);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while listing partner schools");
                return Result<List<PartnerSchoolDto>>(500, "Something went wrong. Please try again later.", null);
            }
        }

        public async Task<ApiResponse<WebsiteVisibilityDto>> SetVisibilityAsync(string schoolId, SetWebsiteVisibilityRequest request, CancellationToken ct)
        {
            try
            {
                if (request?.Show == null) return Result<WebsiteVisibilityDto>(400, "Please say whether the school should be shown (show: true or false).", null);
                if (string.IsNullOrWhiteSpace(schoolId)) return Result<WebsiteVisibilityDto>(404, "School not found", null);

                // The platform's own school row is not a partner; it can never be listed
                if (await _repo.IsPlatformSchoolAsync(schoolId, ct)) return Result<WebsiteVisibilityDto>(400, "The platform's own school cannot be shown on the website.", null);

                var found = await _repo.SetShowOnWebsiteAsync(schoolId, request.Show.Value, DateTime.UtcNow, ct);
                if (!found) return Result<WebsiteVisibilityDto>(404, "School not found", null);

                _listCache.Invalidate();
                return Result(200, request.Show.Value ? "School will be shown on the website" : "School is hidden from the website",
                    new WebsiteVisibilityDto { SchoolId = schoolId, ShowOnWebsite = request.Show.Value });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while setting website visibility");
                return Result<WebsiteVisibilityDto>(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
