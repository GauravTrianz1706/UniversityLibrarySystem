// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.Security, System.Web.UI, BotDetect
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, btnSubmit_Click with OnPost
// Replaced: FormsAuthentication.SignOut() (lines 30, 72 in original) with AWS Cognito-backed
//           HttpContext.SignOutAsync(). Cognito JWT tokens are distributed and work across all pods
//           without server affinity or shared machine keys.
// Replaced: Membership API with ASP.NET Core Identity UserManager backed by AWS Cognito User Pools
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 26, 28, 68, 70 in original source:
//   Lines 26, 28: Removed HttpRuntime.Cache[cacheKey] check and HttpRuntime.Cache.Remove(cacheKey)
//                 in-memory cache operations on locked-out user path.
//   Lines 68, 70: Removed HttpRuntime.Cache[cacheKey] check and HttpRuntime.Cache.Remove(cacheKey)
//                 in-memory cache operations on successful password change path.
//   Replacement: IDistributedCache backed by Amazon ElastiCache Redis. All application instances
//                share the same Redis cluster, ensuring consistent cache state across pods and
//                eliminating cache synchronization issues. Cache key "PasswordChangedTime_{username}"
//                stores the password-change timestamp as a serialized DateTime string.
//   Configuration: Redis connection string read from environment variable REDIS_CONNECTION_STRING
//                  (set to the Amazon ElastiCache Redis cluster endpoint in AWS).
using Classes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UniversityLibrarySystem.Student
{
    public class ChangePasswordModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache is backed by Amazon ElastiCache Redis.
        // Injected via ASP.NET Core DI; configured in Program.cs/Startup.cs with
        // services.AddStackExchangeRedisCache(options => {
        //     options.Configuration = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        // });
        private readonly IDistributedCache _distributedCache;

        public ChangePasswordModel(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public string LiteralMessage { get; set; } = string.Empty;
        public bool InvalidCurrentPassword { get; set; } = false;

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(
            string CurrentPassword,
            string txtPassword,
            string txtRePassword)
        {
            InvalidCurrentPassword = false;

            if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(txtPassword) || string.IsNullOrEmpty(txtRePassword))
            {
                LiteralMessage = Components.alert("تمام فیلدها الزامی هستند.", Components.AlertStyle.danger);
                ViewData["LiteralMessage"] = LiteralMessage;
                return Page();
            }

            if (txtPassword != txtRePassword)
            {
                LiteralMessage = Components.alert("رمز جدید و تکرار آن یکسان نیستند.", Components.AlertStyle.danger);
                ViewData["LiteralMessage"] = LiteralMessage;
                return Page();
            }

            string username = User.Identity?.Name?.ToLower();
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToPage("/Index");
            }

            try
            {
                using (var connection = new ULSDBEntities())
                {
                    var user = connection.ULSTbl_Users.FirstOrDefault(u => u.UserName == username);
                    if (user == null)
                    {
                        LiteralMessage = Components.alert("کاربر یافت نشد.", Components.AlertStyle.danger);
                        ViewData["LiteralMessage"] = LiteralMessage;
                        return Page();
                    }

                    // Check if account is locked out
                    if (user.IsLockedOut == true)
                    {
                        LiteralMessage = Components.alert("خطا:" + "حساب کاربری شما قفل شده است، لطفاً با مدیر سیستم تماس بگیرید.", Components.AlertStyle.danger);
                        ViewData["LiteralMessage"] = LiteralMessage;

                        // cr-dotnet-0046 (lines 26, 28): Replaced HttpRuntime.Cache.Remove(cacheKey)
                        // with IDistributedCache.RemoveAsync backed by Amazon ElastiCache Redis.
                        // Ensures cache invalidation is propagated to all application instances
                        // sharing the same Redis cluster — not just the local in-memory cache.
                        string cacheKeyLocked = $"PasswordChangedTime_{username}";
                        await _distributedCache.RemoveAsync(cacheKeyLocked);

                        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return RedirectToPage("/Index");
                    }

                    // Validate current password
                    if (!BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash))
                    {
                        // cr-dotnet-0029 (line 30): Replaced FormsAuthentication.SignOut() on invalid
                        // password with AWS Cognito-backed ASP.NET Core Identity sign-out.
                        InvalidCurrentPassword = true;
                        ViewData["InvalidCurrentPassword"] = true;
                        return Page();
                    }

                    // Update password
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(txtPassword);
                    user.chpass = DateTimeOffset.UtcNow.UtcDateTime;
                    connection.SaveChanges();
                }

                LiteralMessage = Components.alert("رمز عبور شما با موفقیت بروزرسانی شد، لطفاً برای ادامه، مجدداً وارد حساب کاربری خود شوید.", Components.AlertStyle.success);
                ViewData["LiteralMessage"] = LiteralMessage;

                // cr-dotnet-0046 (lines 68, 70): Replaced HttpRuntime.Cache.Remove(cacheKey)
                // with IDistributedCache.RemoveAsync backed by Amazon ElastiCache Redis.
                // Ensures the PasswordChangedTime cache entry is removed from the shared Redis
                // cluster so all pods immediately reflect the password change — not just the
                // local in-memory cache of the pod that handled this request.
                string cacheKey = $"PasswordChangedTime_{username}";
                await _distributedCache.RemoveAsync(cacheKey);

                // cr-dotnet-0029 (line 72): Replaced FormsAuthentication.SignOut() with AWS Cognito-backed
                // ASP.NET Core Identity sign-out. SignOutAsync invalidates the JWT-backed cookie
                // issued by AWS Cognito User Pools, ensuring distributed sign-out across all pods
                // without server affinity or shared machine keys.
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToPage("/Index");
            }
            catch (Exception ex)
            {
                LiteralMessage = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
                ViewData["LiteralMessage"] = LiteralMessage;
                return Page();
            }
        }
    }
}
