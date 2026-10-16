// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 18 in original source used System.Web.Security.Membership and Roles (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 18: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership/Roles API)
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
// Removed: System.Web, System.Web.Security, System.Web.UI, System.Web.UI.WebControls
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
// Replaced: Membership/Roles API with ASP.NET Core Identity / HttpContext.User
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 23, 41 in original source:
//   Line 23: Removed string text = HttpRuntime.Cache["FaqPageCache"] as string in-memory cache read.
//   Line 41: Removed HttpRuntime.Cache.Insert("FaqPageCache", text, null,
//            DateTime.Now.AddMinutes(3), System.Web.Caching.Cache.NoSlidingExpiration)
//            in-memory cache write.
//   Replacement: IDistributedCache backed by Amazon ElastiCache Redis. All application instances
//                share the same Redis cluster, ensuring consistent FAQ page content across pods
//                and eliminating cache synchronization issues. Cache entry uses a 3-minute absolute
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
    public class FaqModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache is backed by Amazon ElastiCache Redis.
        // Injected via ASP.NET Core DI; configured in Program.cs/Startup.cs with
        // services.AddStackExchangeRedisCache(options => {
        //     options.Configuration = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        // });
        private readonly IDistributedCache _distributedCache;

        public FaqModel(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public string FaqsHtml { get; set; } = string.Empty;
        public bool ShowEditButton { get; set; } = false;

        public async Task<IActionResult> OnGetAsync()
        {
            ViewData["Title"] = SiteConfig.mixTitle("سوالات متداول");

            // cr-dotnet-1031 (Line 18): Replaced Membership.GetUser() != null (Windows Membership
            // provider check) and Roles.GetRolesForUser() (Windows/AD-backed role provider) with
            // ASP.NET Core User.Identity.IsAuthenticated and User.IsInRole() backed by Amazon
            // Cognito JWT Bearer Token (cognito:groups claim). No Active Directory required.
            if (User.Identity?.IsAuthenticated == true)
            {
                ShowEditButton = User.IsInRole("admin");
            }

            FaqsHtml = await GetFaqsAsync();
            return Page();
        }

        private async Task<string> GetFaqsAsync()
        {
            try
            {
                // cr-dotnet-0046 (line 23): Replaced HttpRuntime.Cache["FaqPageCache"] as string
                // in-memory cache read with IDistributedCache.GetAsync backed by Amazon ElastiCache
                // Redis. Reads the FAQ HTML from the shared Redis cluster so all pods see the same
                // cached value — not just the local in-memory cache of one pod.
                byte[] cachedBytes = await _distributedCache.GetAsync("FaqPageCache");
                if (cachedBytes != null && cachedBytes.Length > 0)
                {
                    return Encoding.UTF8.GetString(cachedBytes);
                }

                string text = string.Empty;
                using (var EF = new ULSDBEntities())
                {
                    var qGetFaq = (from item in EF.ULSTbl_Faq
                                   where item.Status == true
                                   select new { item.Question, item.Answer }).ToList();
                    foreach (var item in qGetFaq)
                    {
                        text += @"            <div class=""faq-card"">
                <h5>" + item.Question + @"</h5>
                <p style=""white-space:break-spaces"">" + item.Answer + @"</p>
            </div>";
                    }

                    // cr-dotnet-0046 (line 41): Replaced HttpRuntime.Cache.Insert("FaqPageCache",
                    // text, null, DateTime.Now.AddMinutes(3), Cache.NoSlidingExpiration) with
                    // IDistributedCache.SetAsync backed by Amazon ElastiCache Redis.
                    // Uses AbsoluteExpirationRelativeToNow of 3 minutes to match original behavior.
                    // Cache entry is stored in the shared Redis cluster visible to all pods.
                    var cacheOptions = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
                    };
                    await _distributedCache.SetAsync("FaqPageCache", Encoding.UTF8.GetBytes(text), cacheOptions);
                }
                return text;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
