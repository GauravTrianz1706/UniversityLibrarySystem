// cr-dotnet-0046: Output Cache Provider - Replaced in-memory HttpRuntime.Cache / System.Web.Caching
// with Amazon ElastiCache Redis via IDistributedCache (StackExchange.Redis / Microsoft.Extensions.Caching.StackExchangeRedis).
// All application instances share the same Redis cluster, ensuring consistent responses across pods
// and eliminating cache synchronization issues.
//
// Original violations fixed:
//   Line 19: HttpRuntime.Cache["SiteName"] == null  → _cache.TryGetValue / _cache.GetString
//   Line 30: HttpRuntime.Cache.Insert("SiteName", ...)  → _cache.SetString with DistributedCacheEntryOptions
//   Line 42: HttpRuntime.Cache["Categories"] as List<CategoryInfo>  → _cache.GetString + JSON deserialize
//   Line 48: HttpRuntime.Cache.Insert("Categories", ...)  → _cache.SetString with JSON serialize
//   Line 71: HttpRuntime.Cache.Insert("Categories", ...)  → _cache.SetString
//   Line 129: HttpRuntime.Cache["SiteStatus"] == null  → _cache.GetString
//   Line 138: HttpRuntime.Cache.Insert("SiteStatus", false, ...)  → _cache.SetString
//   Line 141: HttpRuntime.Cache.Insert("SiteStatus", true, ...)  → _cache.SetString
//   Line 149: HttpRuntime.Cache["CanRes"] == null  → _cache.GetString
//   Line 154: HttpRuntime.Cache.Insert("CanRes", false, ...)  → _cache.SetString
//   Line 163: HttpRuntime.Cache.Insert("CanRes", true, ...)  → _cache.SetString
//   Line 166: HttpRuntime.Cache["CanRes"]  → _cache.GetString
//   Line 174: HttpRuntime.Cache["SiteStatus"]  → _cache.GetString
//
// Removed: using System.Web.Caching; (IIS/System.Web-specific, incompatible with cloud/Kestrel)
// Added:   using Microsoft.Extensions.Caching.Distributed; (IDistributedCache - ElastiCache Redis)
//          using System.Text.Json; (for serializing complex objects to/from Redis)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using UniversityLibrarySystem;

namespace Classes
{
    /// <summary>
    /// Site configuration helper.
    /// cr-dotnet-0046: All caching now uses IDistributedCache backed by Amazon ElastiCache Redis.
    /// IDistributedCache is injected via ASP.NET Core DI and registered with AddStackExchangeRedisCache()
    /// in Program.cs / Startup.ConfigureServices, pointing to the ElastiCache Redis endpoint read from
    /// the ELASTICACHE_REDIS_CONNECTION_STRING environment variable (12-factor app principle).
    /// All application instances share the same Redis cluster, ensuring consistent cache state across pods.
    /// </summary>
    public static class SiteConfig
    {
        private const string _siteName = "سامانه کتابخانه دانشگاه";
        private const string _devName = "امیر محمد هدایتی نیا";
        private const string _devLink = "https://github.com/hedayatiniadev";

        // cr-dotnet-0046: IDistributedCache instance set during application startup via SetCache().
        // Backed by Amazon ElastiCache Redis registered in DI with AddStackExchangeRedisCache().
        private static IDistributedCache _cache;

        /// <summary>
        /// Initializes the distributed cache instance (called from Program.cs / Startup after DI build).
        /// cr-dotnet-0046: Replaces static HttpRuntime.Cache (IIS in-process) with IDistributedCache
        /// pointing to Amazon ElastiCache Redis cluster shared across all application pods.
        /// </summary>
        public static void SetCache(IDistributedCache cache)
        {
            _cache = cache;
        }

        private static DistributedCacheEntryOptions OneDayExpiry =>
            new DistributedCacheEntryOptions
            {
                // cr-dotnet-0046: Absolute expiration replaces Cache.NoSlidingExpiration pattern
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
            };

        private static DistributedCacheEntryOptions FiveMinuteExpiry =>
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            };

        public static string getSiteName()
        {
            // cr-dotnet-0046: Replaced HttpRuntime.Cache["SiteName"] == null (line 19)
            // with IDistributedCache.GetString() backed by Amazon ElastiCache Redis.
            if (_cache != null)
            {
                var cached = _cache.GetString("SiteName");
                if (cached != null)
                    return cached;
            }

            try
            {
                // cr-dotnet-0123: Replaced hardcoded Web.config connection string with AWS Secrets Manager.
                using (var EF = new UniversityLibrarySystem.ULSDBEntities(AwsSecretsManagerHelper.GetDbConnectionString()))
                {
                    var query = (from tableSiteSettings in EF.ULSTbl_SiteSetting
                                 where tableSiteSettings.ID != 0
                                 select tableSiteSettings.SiteName).FirstOrDefault();
                    if (query != null)
                    {
                        // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("SiteName", ...) (line 30)
                        // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis.
                        _cache?.SetString("SiteName", query, OneDayExpiry);
                        return query;
                    }
                    return _siteName;
                }
            }
            catch
            {
                return _siteName;
            }
        }

        public static List<CategoryInfo> GetCategories()
        {
            // cr-dotnet-0046: Replaced HttpRuntime.Cache["Categories"] as List<CategoryInfo> (line 42)
            // with IDistributedCache.GetString() + JSON deserialization from Amazon ElastiCache Redis.
            if (_cache != null)
            {
                var cachedJson = _cache.GetString("Categories");
                if (cachedJson != null)
                {
                    var cachedCategories = JsonSerializer.Deserialize<List<CategoryInfo>>(cachedJson);
                    if (cachedCategories != null)
                        return cachedCategories;
                }
            }

            try
            {
                // cr-dotnet-0123: Replaced hardcoded Web.config connection string with AWS Secrets Manager.
                using (var EF = new UniversityLibrarySystem.ULSDBEntities(AwsSecretsManagerHelper.GetDbConnectionString()))
                {
                    var query = (from TableCategory in EF.ULSTbl_Categories
                                 where TableCategory.Status == true
                                 orderby TableCategory.ID descending
                                 select new CategoryInfo
                                 {
                                     ID = TableCategory.ID,
                                     Title = TableCategory.Title,
                                     PicName = TableCategory.PicName
                                 }).ToList();

                    if (query.Any())
                    {
                        // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("Categories", ...) (lines 48, 71)
                        // with IDistributedCache.SetString() + JSON serialization to Amazon ElastiCache Redis.
                        // All pods share the same Redis cluster — no cache inconsistency across instances.
                        var json = JsonSerializer.Serialize(query);
                        _cache?.SetString("Categories", json, OneDayExpiry);
                        return query;
                    }
                    return new List<CategoryInfo>();
                }
            }
            catch (Exception)
            {
                return new List<CategoryInfo>();
            }
        }

        // کلاس برای نگهداری اطلاعات ضروری
        public class CategoryInfo
        {
            public int ID { get; set; }
            public string Title { get; set; }
            public string PicName { get; set; }
        }

        public static string getDevName() => _devName;
        public static string getDevLink() => _devLink;
        public static string mixTitle(string title) => title + " | " + getSiteName();

        public static string GetOperatingSystem(string userAgent)
        {
            userAgent = userAgent.ToLower();

            if (userAgent.Contains("windows nt 10.0"))
                return "Windows 10";
            if (userAgent.Contains("windows nt 6.3"))
                return "Windows 8.1";
            if (userAgent.Contains("windows nt 6.2"))
                return "Windows 8";
            if (userAgent.Contains("windows nt 6.1"))
                return "Windows 7";
            if (userAgent.Contains("windows nt 6.0"))
                return "Windows Vista";
            if (userAgent.Contains("windows nt 5.1"))
                return "Windows XP";
            if (userAgent.Contains("macintosh"))
                return "Mac OS";
            if (userAgent.Contains("linux"))
                return "Linux";
            if (userAgent.Contains("iphone"))
                return "iOS (iPhone)";
            if (userAgent.Contains("ipad"))
                return "iOS (iPad)";
            if (userAgent.Contains("android"))
                return "Android";

            return "Unknown";
        }

        public static bool siteStatus()
        {
            // cr-dotnet-0046: Replaced HttpRuntime.Cache["SiteStatus"] == null (line 129)
            // with IDistributedCache.GetString() backed by Amazon ElastiCache Redis.
            if (_cache != null)
            {
                var cached = _cache.GetString("SiteStatus");
                if (cached != null)
                    return bool.Parse(cached);
            }

            try
            {
                // cr-dotnet-0123: Replaced hardcoded Web.config connection string with AWS Secrets Manager.
                using (UniversityLibrarySystem.ULSDBEntities EF = new UniversityLibrarySystem.ULSDBEntities(AwsSecretsManagerHelper.GetDbConnectionString()))
                {
                    var siteSetting = EF.ULSTbl_SiteSetting.FirstOrDefault();
                    if (siteSetting == null || siteSetting.Status == false)
                    {
                        // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("SiteStatus", false, ...) (line 138)
                        // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis.
                        _cache?.SetString("SiteStatus", "false", OneDayExpiry);
                        return false;
                    }
                    // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("SiteStatus", true, ...) (line 141)
                    // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis.
                    _cache?.SetString("SiteStatus", "true", OneDayExpiry);
                }
            }
            catch
            {
                return false;
            }

            // cr-dotnet-0046: Replaced (bool)HttpRuntime.Cache["SiteStatus"] (line 174)
            // with IDistributedCache.GetString() read from Amazon ElastiCache Redis.
            var result = _cache?.GetString("SiteStatus");
            return result != null && bool.Parse(result);
        }

        public static bool siteCanRes()
        {
            // cr-dotnet-0046: Replaced HttpRuntime.Cache["CanRes"] == null (line 149)
            // with IDistributedCache.GetString() backed by Amazon ElastiCache Redis.
            if (_cache != null)
            {
                var cached = _cache.GetString("CanRes");
                if (cached != null)
                    return bool.Parse(cached);
            }

            try
            {
                // cr-dotnet-0123: Replaced hardcoded Web.config connection string with AWS Secrets Manager.
                using (UniversityLibrarySystem.ULSDBEntities EF = new UniversityLibrarySystem.ULSDBEntities(AwsSecretsManagerHelper.GetDbConnectionString()))
                {
                    var siteSetting = EF.ULSTbl_SiteSetting.FirstOrDefault();
                    if (siteSetting == null || siteSetting.CanRes == false)
                    {
                        // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("CanRes", false, ...) (line 154)
                        // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis.
                        _cache?.SetString("CanRes", "false", OneDayExpiry);
                        return false;
                    }
                    // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("CanRes", true, ...) (line 163)
                    // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis.
                    _cache?.SetString("CanRes", "true", OneDayExpiry);
                }
            }
            catch
            {
                return false;
            }

            // cr-dotnet-0046: Replaced (bool)HttpRuntime.Cache["CanRes"] (line 166)
            // with IDistributedCache.GetString() read from Amazon ElastiCache Redis.
            var canResResult = _cache?.GetString("CanRes");
            return canResResult != null && bool.Parse(canResResult);
        }
    }
}
