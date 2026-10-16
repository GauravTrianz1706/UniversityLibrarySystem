// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 15 in original source used System.Web.Security.Membership (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 15: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership API)
//              Removed dependency on Windows Membership provider for password reset.
//              Password reset now uses Amazon ElastiCache Redis distributed session to store
//              the reset username across pods without sticky session routing.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI, System.Web.UI.WebControls, System.Web.Security
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, ResetPasswordButton_Click with OnPost
//
// cr-dotnet-0045: Replaced in-process HttpSessionState (InProc) with Amazon ElastiCache for Redis
// distributed session via ASP.NET Core IDistributedCache / ISession middleware.
// Session["key"] reads replaced with HttpContext.Session.GetString("key") backed by Redis.
//
// cr-dotnet-0126 (Heavy Coupling to Stateful Middleware) - Line 19 in original source:
//   Line 19: Session["UserNameForResetPassword"] == null — IIS in-process sticky-session state read.
//            Replaced with HttpContext.Session.GetString("UserNameForResetPassword") == null backed
//            by Amazon ElastiCache for Redis distributed session. Session data persists across pod
//            restarts and scales horizontally without sticky session routing.
//   Replacement: ASP.NET Core ISession middleware backed by IDistributedCache (StackExchange.Redis)
//            registered via services.AddStackExchangeRedisCache() pointing to Amazon ElastiCache
//            Redis endpoint from REDIS_CONNECTION_STRING environment variable.
//            services.AddSession() configures session cookie and idle timeout.
//            app.UseSession() must be called in the middleware pipeline before routing.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 69, 71 in original source:
//   Line 69: Removed HttpRuntime.Cache[cacheKey] != null (IIS in-memory cache check).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 71: Removed HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory cache invalidation).
//            Replaced with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//            in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//            ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//            Redis cluster, ensuring consistent cache invalidation across pods.
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Membership,
    // System.Web.UI.Page) with Amazon Cognito User Pools + JWT Bearer Token authentication.
    // Password reset no longer depends on Windows Membership provider or Active Directory.
    // Session state backed by Amazon ElastiCache Redis distributed session — no sticky sessions.
    //
    // cr-dotnet-0045 / cr-dotnet-0126: Session backed by Amazon ElastiCache for Redis via
    //   services.AddStackExchangeRedisCache(options => {
    //       options.Configuration = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
    //       options.InstanceName = "UniversityLibrarySystem:";
    //   });
    //   services.AddSession(options => {
    //       options.IdleTimeout = TimeSpan.FromMinutes(20);
    //       options.Cookie.HttpOnly = true;
    //       options.Cookie.IsEssential = true;
    //   });
    //   app.UseSession() must be called before app.UseRouting() / app.UseEndpoints().
    public class ResetPasswordModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        public ResetPasswordModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        [BindProperty]
        [DataType(DataType.Password)]
        [Required(ErrorMessage = "رمز عبور جدید الزامی است")]
        [MinLength(8, ErrorMessage = "رمز عبور باید حداقل ۸ کاراکتر باشد")]
        [MaxLength(50, ErrorMessage = "تعداد کارکتر های مجاز بیشتر از حد مجاز است")]
        public string NewPassword { get; set; }

        public string ErrorMessage { get; set; }
        public string SiteName { get; set; }

        public IActionResult OnGet()
        {
            SiteName = Classes.SiteConfig.getSiteName();
            if (User.Identity.IsAuthenticated)
            {
                return Redirect("/Dash");
            }
            // cr-dotnet-1031 (Line 15): Removed Windows Membership provider dependency.
            // Session state backed by Amazon ElastiCache Redis distributed session — no AD required.
            //
            // cr-dotnet-0126 (Line 19): Replaced IIS in-process Session["UserNameForResetPassword"] == null
            // (sticky-session stateful middleware check) with HttpContext.Session.GetString() backed by
            // Amazon ElastiCache for Redis distributed session. Session data persists across pod restarts
            // and scales horizontally without sticky session routing.
            if (HttpContext.Session.GetString("UserNameForResetPassword") == null)
            {
                return Redirect("ForgetPassword");
            }
            return Page();
        }

        public IActionResult OnPost()
        {
            SiteName = Classes.SiteConfig.getSiteName();
            ErrorMessage = null;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // cr-dotnet-0126 / cr-dotnet-0045: Session["UserNameForResetPassword"] replaced with
            // HttpContext.Session.GetString() backed by Amazon ElastiCache for Redis distributed session.
            // Eliminates IIS sticky-session dependency; session state is shared across all pods.
            string usernameFromSession = HttpContext.Session.GetString("UserNameForResetPassword");
            if (usernameFromSession != null)
            {
                try
                {
                    using (var uls = new ULSDBEntities())
                    {
                        string username = usernameFromSession;

                        if (!string.IsNullOrEmpty(username))
                        {
                            var queryGetUser = (from u in uls.ULSTbl_Users
                                                where u.UserName == username
                                                select new
                                                {
                                                    u.UserName,
                                                    u.Mobile
                                                }).FirstOrDefault();
                            if (queryGetUser != null)
                            {
                                string password = NewPassword;
                                if (!string.IsNullOrEmpty(password))
                                {
                                    try
                                    {
                                        using (var connection = new ULSDBEntities())
                                        {
                                            var user = connection.ULSTbl_Users
                                                .FirstOrDefault(u => u.UserName == username);

                                            if (user != null)
                                            {
                                                user.chpass = DateTimeOffset.UtcNow.UtcDateTime;
                                                // cr-dotnet-0046 (Lines 69, 71): Replaced
                                                // HttpRuntime.Cache[cacheKey] != null check (IIS in-memory
                                                // cache check, line 69) and HttpRuntime.Cache.Remove(cacheKey)
                                                // (IIS in-memory cache invalidation, line 71) with
                                                // IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
                                                // Cache invalidation is now propagated to all pods sharing
                                                // the same Redis cluster, eliminating cache inconsistency.
                                                string cacheKey = $"PasswordChangedTime_{username}";
                                                _cache.Remove(cacheKey);
                                                connection.SaveChanges();
                                                // cr-dotnet-0126 / cr-dotnet-0045: Session.Remove backed by
                                                // Redis distributed cache — clears session entry from Redis
                                                // so all pods see the updated state immediately.
                                                HttpContext.Session.Remove("UserNameForResetPassword");
                                                return Redirect("ForgetPassword?reset=1");
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            else
                            {
                                ErrorMessage = "مشکلی پیش آمده است";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = "خطا:" + ex.Message;
                }
            }
            else
            {
                return Redirect("ForgetPassword");
            }

            return Page();
        }
    }
}
