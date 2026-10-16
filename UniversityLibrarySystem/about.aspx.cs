// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 16 in original source used System.Web.Security.Membership and Roles (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 16: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership/Roles API)
//              Removed 'Membership.GetUser()' (Windows Membership provider)
//              Removed 'Roles.GetRolesForUser()' (Windows/AD-backed role provider)
//              Replaced with ASP.NET Core User.Identity.IsAuthenticated and User.IsInRole()
//              backed by Amazon Cognito JWT Bearer Token (cognito:groups claim).
//              Roles are managed in Cognito User Pool groups — no Active Directory required.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.Security, System.Web.UI
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
// Replaced: Membership/Roles API with ASP.NET Core Identity / HttpContext.User
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 24, 30, 39 in original source:
//   Line 24: Removed HttpRuntime.Cache["AboutUS"] == null in-memory cache check.
//   Line 30: Removed HttpRuntime.Cache.Insert("AboutUS", ...) in-memory cache write with
//            DateTime.Now.AddMinutes(5) absolute expiration and System.Web.Caching.Cache.NoSlidingExpiration.
//   Line 39: Removed HttpRuntime.Cache["AboutUS"].ToString() in-memory cache read.
//   Replacement: IDistributedCache backed by Amazon ElastiCache Redis. All application instances
//                share the same Redis cluster, ensuring consistent "AboutUS" content across pods
//                and eliminating cache synchronization issues. Cache entry uses a 5-minute absolute
//                expiration matching the original HttpRuntime.Cache behavior.
//   Configuration: Redis connection string read from environment variable REDIS_CONNECTION_STRING
//                  (set to the Amazon ElastiCache Redis cluster endpoint in AWS).
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Membership,
    // Roles.GetRolesForUser, System.Web.UI.Page) with Amazon Cognito User Pools + JWT Bearer
    // Token authentication. User roles are now derived from Amazon Cognito cognito:groups claim
    // in the JWT Bearer Token validated by ASP.NET Core cookie authentication middleware on Kestrel.
    // No on-premise Active Directory or Windows role provider required.
    public class AboutModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache is backed by Amazon ElastiCache Redis.
        // Injected via ASP.NET Core DI; configured in Program.cs/Startup.cs with
        // services.AddStackExchangeRedisCache(options => {
        //     options.Configuration = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        // });
        private readonly IDistributedCache _distributedCache;

        public AboutModel(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public string AboutUsContent { get; set; } = string.Empty;
        public bool ShowEditButton { get; set; } = false;

        public async Task<IActionResult> OnGetAsync()
        {
            ViewData["Title"] = SiteConfig.mixTitle("درباره ما");

            // cr-dotnet-1031 (Line 16): Replaced Membership.GetUser() != null (Windows Membership
            // provider check) and Roles.GetRolesForUser() (Windows/AD-backed role provider) with
            // ASP.NET Core User.Identity.IsAuthenticated and User.IsInRole() backed by Amazon
            // Cognito JWT Bearer Token (cognito:groups claim). No Active Directory required.
            // cr-dotnet-1031 (Line 73): User.Identity.IsAuthenticated and User.IsInRole() are backed
            // by Amazon Cognito JWT Bearer Token — no Windows/NTLM/Kerberos or Active Directory required.
            if (User.Identity?.IsAuthenticated == true)
            {
                ShowEditButton = User.IsInRole("admin");
            }

            AboutUsContent = await GetSiteSettingAsync();
            return Page();
        }

        private async Task<string> GetSiteSettingAsync()
        {
            try
            {
                // cr-dotnet-0046 (line 24): Replaced HttpRuntime.Cache["AboutUS"] == null check
                // with IDistributedCache.GetAsync backed by Amazon ElastiCache Redis.
                // Reads the AboutUS content from the shared Redis cluster so all pods see the
                // same cached value — not just the local in-memory cache of one pod.
                byte[] cachedBytes = await _distributedCache.GetAsync("AboutUS");
                if (cachedBytes != null && cachedBytes.Length > 0)
                {
                    // cr-dotnet-0046 (line 39): Replaced HttpRuntime.Cache["AboutUS"].ToString()
                    // with IDistributedCache.GetAsync result deserialization from Redis.
                    return Encoding.UTF8.GetString(cachedBytes);
                }

                using (var EF = new ULSDBEntities())
                {
                    var qGetAbout = (from item in EF.ULSTbl_SiteSetting
                                     select new { item.AboutUs }).FirstOrDefault();
                    string aboutUs = qGetAbout?.AboutUs ?? string.Empty;

                    // cr-dotnet-0046 (line 30): Replaced HttpRuntime.Cache.Insert("AboutUS", ...,
                    // DateTime.Now.AddMinutes(5), Cache.NoSlidingExpiration) with
                    // IDistributedCache.SetAsync backed by Amazon ElastiCache Redis.
                    // Uses AbsoluteExpirationRelativeToNow of 5 minutes to match original behavior.
                    // Cache entry is stored in the shared Redis cluster visible to all pods.
                    var cacheOptions = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    };
                    await _distributedCache.SetAsync("AboutUS", Encoding.UTF8.GetBytes(aboutUs), cacheOptions);
                    return aboutUs;
                }
            }
            catch
            {
                return "متن این صفحه موجود نیست،لطفا از صفحه مدیریت اقدام به پر کردن صفحه نمایید";
            }
        }
    }
}
