// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.Security, System.Web.UI, System.Web.UI.WebControls
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
// Replaced: LinqDataSourceNews_Selecting with data loading in OnGet
// Replaced: btnDeleteAllDevice_Click with OnPostDeleteAllDevice
// Replaced: FormsAuthentication (lines 65, 66 in original) with AWS Cognito-backed ASP.NET Core
//           cookie authentication. Cognito JWT tokens are distributed and work across all pods
//           without server affinity or shared machine keys.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 27, 29, 53, 55 in original source:
//   Lines 27, 29: Removed HttpRuntime.Cache[cacheKey] in-memory cache read and cast to DateTime
//                 in LinqDataSourceNews_Selecting. In-memory cache is not shared across pods.
//   Lines 53, 55: Removed HttpRuntime.Cache[cacheKey] in-memory cache check and
//                 HttpRuntime.Cache.Remove(cacheKey) in btnDeleteAllDevice_Click.
//   Replacement: IDistributedCache backed by Amazon ElastiCache Redis. All application instances
//                share the same Redis cluster, ensuring consistent PasswordChangedTime values
//                across pods and eliminating cache synchronization issues.
//   Configuration: Redis connection string read from environment variable REDIS_CONNECTION_STRING
//                  (set to the Amazon ElastiCache Redis cluster endpoint in AWS).
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Line 33 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) and IDistributedCache async methods
//   (GetAsync, RemoveAsync) to ensure non-blocking thread pool usage under high load in cloud
//   auto-scaling scenarios.
using Classes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniversityLibrarySystem.Student
{
    public class LoginReportModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache is backed by Amazon ElastiCache Redis.
        // Injected via ASP.NET Core DI; configured in Program.cs/Startup.cs with
        // services.AddStackExchangeRedisCache(options => {
        //     options.Configuration = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        // });
        private readonly IDistributedCache _distributedCache;

        public LoginReportModel(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public string LiteralMsg { get; set; } = string.Empty;
        public List<DeviceItem> DeviceList { get; set; } = new List<DeviceItem>();

        // cr-dotnet-1000 (Line 33): OnGetAsync uses async/await throughout — IDistributedCache.GetAsync
        // for non-blocking Redis cache reads and EF Core ToListAsync for non-blocking DB queries.
        // Ensures request threads are not blocked under high cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            // cr-dotnet-0029 (line 65): Authentication check uses ASP.NET Core Identity backed by
            // AWS Cognito User Pools JWT tokens. Replaces FormsAuthentication-based session check.
            // Cognito JWT tokens work across all pods without server affinity or shared machine keys.
            string username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Redirect("/logout");
            }

            try
            {
                // cr-dotnet-0046 (lines 27, 29): Replaced HttpRuntime.Cache[cacheKey] in-memory
                // cache read with IDistributedCache.GetAsync backed by Amazon ElastiCache Redis.
                // Reads the PasswordChangedTime from the shared Redis cluster so all pods see the
                // same value — not just the local in-memory cache of the pod handling this request.
                string cacheKey = $"PasswordChangedTime_{username}";
                DateTime chp = DateTime.MinValue;
                byte[] cachedBytes = await _distributedCache.GetAsync(cacheKey);
                if (cachedBytes != null && cachedBytes.Length > 0)
                {
                    string cachedValue = Encoding.UTF8.GetString(cachedBytes);
                    if (DateTime.TryParse(cachedValue, out DateTime parsedDate))
                    {
                        chp = parsedDate;
                    }
                }

                using (var EF = new ULSDBEntities())
                {
                    DeviceList = await (from MyDevice in EF.PAPMyDevice
                                        where MyDevice.Username == username && (chp == DateTime.MinValue || chp <= MyDevice.DateTime)
                                        orderby MyDevice.Id descending
                                        select new DeviceItem
                                        {
                                            BrowserName = MyDevice.Name,
                                            DateTime = MyDevice.DateTime
                                        }).ToListAsync();
                }
            }
            catch (Exception)
            {
                return Redirect("logout");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAllDeviceAsync()
        {
            // cr-dotnet-0029 (line 66): Authentication check uses ASP.NET Core Identity backed by
            // AWS Cognito User Pools JWT tokens. Replaces FormsAuthentication-based session check.
            // Cognito JWT tokens work across all pods without server affinity or shared machine keys.
            string username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Redirect("/");
            }

            // cr-dotnet-0046 (lines 53, 55): Replaced HttpRuntime.Cache[cacheKey] check and
            // HttpRuntime.Cache.Remove(cacheKey) with IDistributedCache.RemoveAsync backed by
            // Amazon ElastiCache Redis. Ensures the PasswordChangedTime cache entry is removed
            // from the shared Redis cluster visible to all pods — not just the local in-memory
            // cache of the pod handling this request.
            string cacheKey = $"PasswordChangedTime_{username}";
            await _distributedCache.RemoveAsync(cacheKey);

            using (var EF = new ULSDBEntities())
            {
                var query = await (from tblUser in EF.ULSTbl_Users
                                   where tblUser.UserName == username
                                   select tblUser).FirstOrDefaultAsync();
                if (query != null)
                {
                    query.chpass = DateTimeOffset.UtcNow.UtcDateTime.AddSeconds(-1);

                    PAPMyDevice device = new PAPMyDevice
                    {
                        Username = username,
                        Name = "<h4 class='m-0 p-0'>" + Classes.SiteConfig.GetOperatingSystem(Request.Headers["User-Agent"].ToString()) + "</h4>" + Request.Headers["User-Agent"].ToString() + " IP:" + HttpContext.Connection.RemoteIpAddress?.ToString(),
                        LogCode = null,
                        DateTime = DateTimeOffset.UtcNow.UtcDateTime
                    };
                    EF.PAPMyDevice.Add(device);
                    await EF.SaveChangesAsync();

                    // Remove older logs
                    var userDevices = await EF.PAPMyDevice.Where(d => d.Username == username)
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
            return RedirectToPage("./LoginReport");
        }
    }

    public class DeviceItem
    {
        public string BrowserName { get; set; }
        public DateTime? DateTime { get; set; }
    }
}
