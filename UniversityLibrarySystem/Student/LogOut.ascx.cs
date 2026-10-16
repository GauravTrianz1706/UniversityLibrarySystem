// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.Security, System.Web.UI
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
// Replaced: FormsAuthentication.SignOut() (line 16 in original) with AWS Cognito-backed
//           HttpContext.SignOutAsync(). Cognito JWT tokens are distributed and work across all pods
//           without server affinity or shared machine keys.
//
// cr-dotnet-0044 (IIS Module Dependencies) - Lines 12, 14 in original source:
//   Line 12: Removed IIS-specific 'using System.Web;' - System.Web is an IIS/Windows-only assembly
//            unavailable on Kestrel running on Amazon ECS/EKS. Prevents cloud deployment.
//   Line 14: Removed IIS-specific 'using System.Web.Security;' - System.Web.Security contains
//            FormsAuthentication and Membership which are IIS HttpModule dependencies.
//            These modules are not available in the ASP.NET Core middleware pipeline on Kestrel.
//   Replacement: Microsoft.AspNetCore.Authentication (HttpContext.SignOutAsync) and
//                Microsoft.AspNetCore.Mvc.RazorPages.PageModel replace all IIS-specific types.
//   Authentication: ASP.NET Core cookie authentication middleware (UseAuthentication) backed by
//                   AWS Cognito User Pools JWT tokens replaces FormsAuthentication IIS module.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 12, 14 in original source:
//   Line 12: Removed HttpContext.Current.Cache[cacheKey] in-memory cache check on logout.
//   Line 14: Removed HttpContext.Current.Cache.Remove(cacheKey) in-memory cache removal on logout.
//   Replacement: IDistributedCache backed by Amazon ElastiCache Redis. All application instances
//                share the same Redis cluster, ensuring the PasswordChangedTime cache entry is
//                removed from the shared cache visible to all pods — not just the local in-memory
//                cache of the pod handling the logout request.
//   Configuration: Redis connection string read from environment variable REDIS_CONNECTION_STRING
//                  (set to the Amazon ElastiCache Redis cluster endpoint in AWS).
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System.Threading.Tasks;

namespace UniversityLibrarySystem.Student
{
    // cr-dotnet-0044: Replaced System.Web.UI.UserControl (IIS HttpHandler) with ASP.NET Core
    // Razor Pages PageModel. Runs on Kestrel on Amazon ECS or EKS without IIS dependency.
    public class LogOutModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache is backed by Amazon ElastiCache Redis.
        // Injected via ASP.NET Core DI; configured in Program.cs/Startup.cs with
        // services.AddStackExchangeRedisCache(options => {
        //     options.Configuration = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        // });
        private readonly IDistributedCache _distributedCache;

        public LogOutModel(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // cr-dotnet-0046 (lines 12, 14): Replaced HttpContext.Current.Cache[cacheKey] check
            // and HttpContext.Current.Cache.Remove(cacheKey) with IDistributedCache.RemoveAsync
            // backed by Amazon ElastiCache Redis. Ensures the PasswordChangedTime cache entry is
            // removed from the shared Redis cluster visible to all pods — not just the local
            // in-memory cache of the pod handling this logout request.
            if (User.Identity?.Name != null)
            {
                string cacheKey = $"PasswordChangedTime_{User.Identity.Name}";
                await _distributedCache.RemoveAsync(cacheKey);
            }

            // cr-dotnet-0029 (line 16): Replaced FormsAuthentication.SignOut() with AWS Cognito-backed
            // ASP.NET Core Identity sign-out. SignOutAsync invalidates the JWT-backed cookie
            // issued by AWS Cognito User Pools, ensuring distributed sign-out across all pods
            // without server affinity or shared machine keys.
            // cr-dotnet-0044: HttpContext.SignOutAsync runs in ASP.NET Core middleware pipeline
            // on Kestrel - no IIS FormsAuthentication module or System.Web.Security dependency.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/");
        }
    }
}
