using Microsoft.Extensions.Caching.Memory;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class PartnerSchoolCache : IPartnerSchoolCache
    {
        public const string CacheKey = "public:partner-schools";

        private readonly IMemoryCache _cache;
        public PartnerSchoolCache(IMemoryCache cache) => _cache = cache;

        public void Invalidate() => _cache.Remove(CacheKey);
    }
}
