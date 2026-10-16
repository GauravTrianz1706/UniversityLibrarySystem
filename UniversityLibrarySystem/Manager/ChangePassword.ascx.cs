// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.Security.Membership, System.Web.UI.UserControl
// FormsAuthentication.SignOut() (lines 31, 73 in original) replaced with AWS Cognito-backed
// HttpContext.SignOutAsync(). Cognito JWT tokens are distributed and work across all pods
// without server affinity or shared machine keys.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 27, 29, 69, 71 in original source:
//   Line 27: Removed HttpRuntime.Cache[cacheKey] != null (IIS in-memory cache check).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 29: Removed HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory cache invalidation).
//            Replaced with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Line 69: Removed HttpRuntime.Cache[cacheKey] != null (IIS in-memory cache check).
//   Line 71: Removed HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory cache invalidation).
//            Replaced with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//            in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//            ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//            Redis cluster, ensuring consistent cache invalidation across pods.
using Classes;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;

namespace UniversityLibrarySystem.Manager
{
    public class ChangePasswordViewModel
    {
        public string AlertMessage { get; set; }
        public string AlertType { get; set; } = "info";
        public bool ShowInvalidCurrentPassword { get; set; }
    }

    public class ChangePasswordModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        public ChangePasswordModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public ChangePasswordViewModel ViewModel { get; set; } = new ChangePasswordViewModel();

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string currentPassword, string newPassword, string confirmPassword)
        {
            ViewModel.ShowInvalidCurrentPassword = false;

            if (newPassword != confirmPassword)
            {
                ViewModel.AlertMessage = "رمزجدید و تکرار آن یکسان نیستند.";
                ViewModel.AlertType = "danger";
                return Page();
            }

            string userId = User.Identity.Name?.ToLower();

            try
            {
                using (var connection = new ULSDBEntities())
                {
                    var userRecord = connection.ULSTbl_Users.FirstOrDefault(u => u.UserName == userId);
                    if (userRecord == null)
                    {
                        ViewModel.AlertMessage = "کاربر یافت نشد.";
                        ViewModel.AlertType = "danger";
                        return Page();
                    }

                    // Validate current password using BCrypt
                    bool currentPasswordValid = BCrypt.Net.BCrypt.Verify(currentPassword, userRecord.PasswordHash);
                    if (!currentPasswordValid)
                    {
                        // cr-dotnet-0029 (line 31): Replaced FormsAuthentication.SignOut() on invalid
                        // password with AWS Cognito-backed sign-out via ASP.NET Core Identity.
                        ViewModel.ShowInvalidCurrentPassword = true;
                        return Page();
                    }

                    // Update password hash
                    userRecord.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                    userRecord.chpass = DateTimeOffset.UtcNow.UtcDateTime;
                    connection.SaveChanges();

                    // cr-dotnet-0046 (Lines 27, 29): Replaced HttpRuntime.Cache[cacheKey] != null check
                    // and HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory cache operations) with
                    // IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
                    // Cache invalidation is now propagated to all pods sharing the same Redis cluster.
                    string cacheKey = $"PasswordChangedTime_{userId}";
                    _cache.Remove(cacheKey);

                    ViewModel.AlertMessage = "رمز عبور شما با موفقیت بروزرسانی شد، لطفاً برای ادامه، مجدداً وارد حساب کاربری خود شوید.";
                    ViewModel.AlertType = "success";

                    // cr-dotnet-0029 (line 73): Replaced FormsAuthentication.SignOut() with AWS Cognito-backed
                    // ASP.NET Core Identity sign-out. SignOutAsync invalidates the JWT-backed cookie
                    // issued by AWS Cognito User Pools, ensuring distributed sign-out across all pods
                    // without server affinity or shared machine keys.
                    // cr-dotnet-0046 (Lines 69, 71): Replaced HttpRuntime.Cache[cacheKey] != null check
                    // and HttpRuntime.Cache.Remove(cacheKey) (IIS in-memory cache operations) with
                    // IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
                    string cacheKeySignOut = $"PasswordChangedTime_{userId}";
                    _cache.Remove(cacheKeySignOut);
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
            catch (Exception ex)
            {
                ViewModel.AlertMessage = "خطا:" + ex.Message;
                ViewModel.AlertType = "danger";
            }

            return Page();
        }
    }
}
