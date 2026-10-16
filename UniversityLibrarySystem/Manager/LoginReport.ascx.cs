using Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.UI.UserControl, System.Web.UI.WebControls, System.Web.Security
// Replaced: Membership with User.Identity (ASP.NET Core Identity backed by AWS Cognito User Pools)
// Replaced: FormsAuthentication (lines 65, 66 in original) with ASP.NET Core Cookie Authentication
//           backed by AWS Cognito JWT tokens that work across all pods without server affinity
// Replaced: LinqDataSource with direct LINQ queries
// Replaced: Request.Browser / Request.UserHostAddress with HttpContext equivalents
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 27, 29, 53, 55 in original source:
//   Line 27: Removed HttpRuntime.Cache[cacheKey] != null (IIS in-memory cache check).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 29: Removed (DateTime)HttpRuntime.Cache[cacheKey] (IIS in-memory cache read).
//            Replaced with IDistributedCache.GetStringAsync() backed by Amazon ElastiCache Redis.
//   Line 53: Removed HttpRuntime.Cache[cacheKey] != null (IIS in-memory cache check).
//   Line 55: Removed HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory cache invalidation).
//            Replaced with IDistributedCache.RemoveAsync() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//            in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//            ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//            Redis cluster, ensuring consistent cache state across pods.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Line 33 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are now async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) and IDistributedCache async methods
//   (GetAsync, RemoveAsync) to ensure non-blocking thread pool usage under high load in cloud
//   auto-scaling scenarios.

namespace UniversityLibrarySystem.Manager
{
    public class DeviceInfo
    {
        public string BrowserName { get; set; }
        public DateTime DateTime { get; set; }
    }

    public class LoginReportModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        public LoginReportModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public List<DeviceInfo> Devices { get; set; } = new List<DeviceInfo>();
        public string Message { get; set; } = "";

        // cr-dotnet-1000 (Line 33): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries and cache operations with async equivalents to prevent blocking
        // request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            // cr-dotnet-0029 (line 65): Authentication check uses ASP.NET Core Identity backed by
            // AWS Cognito User Pools JWT tokens. Replaces FormsAuthentication-based session check.
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            await LoadDevicesAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAllDevicesAsync()
        {
            // cr-dotnet-0029 (line 66): Authentication check uses ASP.NET Core Identity backed by
            // AWS Cognito User Pools JWT tokens. Replaces FormsAuthentication-based session check.
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            string username = User.Identity.Name;
            // cr-dotnet-0046 (Lines 53, 55): Replaced HttpRuntime.Cache[cacheKey] != null check
            // (IIS in-memory cache check) and HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory
            // cache invalidation) with IDistributedCache.RemoveAsync() backed by Amazon ElastiCache Redis.
            // Cache invalidation is now propagated to all pods sharing the same Redis cluster.
            string cacheKey = $"PasswordChangedTime_{username}";
            await _cache.RemoveAsync(cacheKey);

            using (var EF = new ULSDBEntities())
            {
                var query = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.UserName == username);
                if (query != null)
                {
                    query.chpass = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(-1);

                    string userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
                    string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    string browserInfo = $"<h4 class='m-0 p-0'>{Classes.SiteConfig.GetOperatingSystem(userAgent)}</h4>{userAgent} IP:{ipAddress}";

                    PAPMyDevice device = new PAPMyDevice
                    {
                        Username = username,
                        Name = browserInfo,
                        LogCode = null,
                        DateTime = DateTimeOffset.UtcNow.UtcDateTime
                    };
                    EF.PAPMyDevice.Add(device);
                    await EF.SaveChangesAsync();

                    // Remove older device logs, keep only the latest
                    var userDevices = await EF.PAPMyDevice
                        .Where(d => d.Username == username)
                        .OrderByDescending(d => d.Id)
                        .Skip(1)
                        .ToListAsync();
                    if (userDevices.Any())
                    {
                        EF.PAPMyDevice.RemoveRange(userDevices);
                        await EF.SaveChangesAsync();
                    }
                }
            }

            return RedirectToPage();
        }

        private async Task LoadDevicesAsync()
        {
            try
            {
                string username = User.Identity.Name;
                // cr-dotnet-0046 (Lines 27, 29): Replaced HttpRuntime.Cache[cacheKey] != null check
                // (IIS in-memory cache check, line 27) and (DateTime)HttpRuntime.Cache[cacheKey]
                // (IIS in-memory cache read, line 29) with IDistributedCache.GetStringAsync() backed by
                // Amazon ElastiCache Redis. All pods share the same Redis cluster — consistent cache state.
                string cacheKey = $"PasswordChangedTime_{username}";
                DateTime chp = DateTime.MinValue;
                string cachedValue = await _cache.GetStringAsync(cacheKey);
                if (cachedValue != null && DateTime.TryParse(cachedValue, out DateTime cachedTime))
                {
                    chp = cachedTime;
                }

                using (var EF = new ULSDBEntities())
                {
                    Devices = await (from myDevice in EF.PAPMyDevice
                                     where myDevice.Username == username && (chp == DateTime.MinValue || chp <= myDevice.DateTime)
                                     orderby myDevice.Id descending
                                     select new DeviceInfo
                                     {
                                         BrowserName = myDevice.Name,
                                         DateTime = myDevice.DateTime
                                     }).ToListAsync();
                }
            }
            catch
            {
                Devices = new List<DeviceInfo>();
            }
        }
    }
}
