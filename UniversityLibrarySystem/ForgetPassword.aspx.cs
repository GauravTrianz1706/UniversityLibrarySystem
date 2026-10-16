// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 12 in original source used System.Web.Security.Membership (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// cr-dotnet-1018: XSS Prevention - Request.Query values sanitized via XssSanitizer (Ganss.Xss)
//   Line 83: Request.Query["reset"] sanitized with XssSanitizer.SanitizeText() before use.
//   Sanitizer configuration (allowed tags/attributes) stored in AWS Secrets Manager under
//   XSS_SANITIZER_CONFIG_SECRET_NAME for centralized multi-tenant management.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 12: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership API)
//              Replaced with ASP.NET Core Identity backed by Amazon Cognito User Pools.
//              Session management replaced with Amazon ElastiCache for Redis distributed session.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// Migrated from ASP.NET Web Forms (System.Web.UI.Page) to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0045: Replaced in-process HttpSessionState (InProc) with Amazon ElastiCache for Redis
// distributed session via ASP.NET Core IDistributedCache / ISession middleware.
// Session.Clear() and Session["key"] = null replaced with HttpContext.Session distributed methods.
//
// cr-dotnet-0126 (Heavy Coupling to Stateful Middleware) - Lines 33, 60 in original source:
//   Line 33: Session.Clear() — IIS in-process sticky-session state cleared.
//            Replaced with HttpContext.Session.Clear() backed by Amazon ElastiCache for Redis
//            distributed session. Session data persists across pod restarts and scales
//            horizontally without sticky session routing.
//   Line 60: Session["UserNameForResetPassword"] = Username.Text — IIS in-process session write.
//            Replaced with HttpContext.Session.SetString("UserNameForResetPassword", Username)
//            backed by Amazon ElastiCache for Redis distributed session.
//   Replacement: ASP.NET Core ISession middleware backed by IDistributedCache (StackExchange.Redis)
//            registered via services.AddStackExchangeRedisCache() pointing to Amazon ElastiCache
//            Redis endpoint from REDIS_CONNECTION_STRING environment variable.
//            services.AddSession() configures session cookie and idle timeout.
//            app.UseSession() must be called in the middleware pipeline before routing.
using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using UniversityLibrarySystem.Classes;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Membership,
    // System.Web.UI.Page) with Amazon Cognito User Pools + JWT Bearer Token authentication.
    // User identity is now derived from Amazon Cognito JWT claims validated by ASP.NET Core
    // cookie authentication middleware on Kestrel — no on-premise Active Directory required.
    //
    // Migrated from ASP.NET Web Forms (System.Web.UI.Page) to ASP.NET Core Razor Pages (PageModel)
    // Removed: System.Web.Security.Membership, System.Web.UI.Page, System.Web.UI
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
    public class ForgetPasswordModel : PageModel
    {
        [BindProperty]
        public string Fullname { get; set; }

        [BindProperty]
        public string Username { get; set; }

        [BindProperty]
        public string Mobile { get; set; }

        [BindProperty]
        public string Email { get; set; }

        public bool ShowSuccess { get; set; }
        public bool ShowError { get; set; }
        public string SuccessMessage { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        public IActionResult OnGet()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToPage("/Dash");
            }

            // cr-dotnet-1018 (Line 83): Sanitize user-supplied query parameter 'reset' using
            // XssSanitizer (Ganss.Xss) before use to prevent XSS injection.
            // Sanitizer configuration (allowed tags/attributes) is loaded from AWS Secrets Manager
            // under XSS_SANITIZER_CONFIG_SECRET_NAME for centralized multi-tenant management.
            string reset = XssSanitizer.SanitizeText(Request.Query["reset"]);
            if (!string.IsNullOrEmpty(reset) && reset == "1")
            {
                ShowSuccess = true;
                SuccessMessage = "رمز عبور با موفقیت تغییر یافت.";
            }

            return Page();
        }

        public IActionResult OnPost()
        {
            ShowError = false;

            if (!ModelState.IsValid)
                return Page();

            using (var uls = new ULSDBEntities())
            {
                var queryGetUser = (from u in uls.ULSTbl_Users
                                    where u.UserName == Username
                                        && u.Mobile == Mobile
                                        && u.FullName.ToLower().Trim() == Fullname.ToLower().Trim()
                                        && u.RoleID < 3
                                        && Email == u.Email
                                    select new
                                    {
                                        u.UserName,
                                        u.Mobile,
                                        u.FullName
                                    }).FirstOrDefault();

                if (queryGetUser != null)
                {
                    // cr-dotnet-0126 (Line 33): Replaced IIS in-process Session.Clear() (sticky-session
                    // stateful middleware) with HttpContext.Session.Clear() backed by Amazon ElastiCache
                    // for Redis distributed session. Session data persists across pod restarts and
                    // scales horizontally without sticky session routing.
                    HttpContext.Session.Clear();

                    // cr-dotnet-0126 (Line 60): Replaced IIS in-process Session["UserNameForResetPassword"] = null
                    // and Session["UserNameForResetPassword"] = Username.Text (sticky-session stateful middleware)
                    // with HttpContext.Session.SetString() backed by Amazon ElastiCache for Redis distributed
                    // session. Session data is stored in Redis and shared across all application instances,
                    // enabling stateless horizontal scaling without sticky session routing.
                    HttpContext.Session.SetString("UserNameForResetPassword", Username);
                    return RedirectToPage("/ResetPassword");
                }
                else
                {
                    ShowError = true;
                    ErrorMessage = "مقادیر وارد شده نادرست میباشند.";
                    return Page();
                }
            }
        }
    }
}
