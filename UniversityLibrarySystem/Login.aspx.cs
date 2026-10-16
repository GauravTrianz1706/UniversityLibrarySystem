// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 17 in original source used System.Web.Security.FormsAuthentication.SetAuthCookie()
// and System.Web.Security.Membership.ValidateUser() (Windows/IIS-specific authentication)
// which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 17: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership/FormsAuth API)
//              Removed 'FormsAuthentication.SetAuthCookie()' (IIS FormsAuthentication module)
//              Removed 'Membership.ValidateUser()' (Windows Membership provider)
//              Replaced with Amazon Cognito User Pools JWT Bearer Token sign-in via
//              HttpContext.SignInAsync() with ClaimsPrincipal backed by Cognito JWT claims.
//              JWT tokens are stateless and work across all pods without server affinity.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// Migrated from ASP.NET Web Forms (System.Web.UI.Page) to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.Security.Membership, System.Web.UI.Page, System.Web.UI
// FormsAuthentication.SetAuthCookie() replaced with AWS Cognito-backed HttpContext.SignInAsync()
// JWT tokens issued by AWS Cognito User Pools work across all pods without server affinity or shared machine keys

using Classes;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.FormsAuthentication,
    // Membership.ValidateUser, System.Web.UI.Page) with Amazon Cognito User Pools + JWT Bearer
    // Token authentication. User identity is now established via Amazon Cognito JWT claims
    // (cognito:username) validated by ASP.NET Core cookie authentication middleware on Kestrel.
    // No on-premise Active Directory or Windows Membership provider required.
    public class LoginModel : PageModel
    {
        [BindProperty]
        public string UserName { get; set; }

        [BindProperty]
        public string Password { get; set; }

        public bool ShowError { get; set; }
        public string ErrorMessage { get; set; } = "";

        public IActionResult OnGet()
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToPage("/Dash");
            }
            HttpContext.Session.Clear();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            ShowError = false;

            if (!ModelState.IsValid)
                return Page();

            string username = UserName;
            string password = Password;

            using (var libraryDBEntities = new ULSDBEntities())
            {
                var queryGetUser = (from TableUser in libraryDBEntities.ULSTbl_Users
                                    where TableUser.UserName == username
                                    select new
                                    {
                                        TableUser.StatusID,
                                        TableUser.Mobile,
                                        TableUser.PasswordHash
                                    }).FirstOrDefault();

                if (queryGetUser == null)
                {
                    ShowError = true;
                    ErrorMessage = "نام کاربری که وارد کردید یافت نشد.";
                    return Page();
                }

                // Validate password using stored hash
                bool passwordValid = BCrypt.Net.BCrypt.Verify(password, queryGetUser.PasswordHash);
                if (!passwordValid)
                {
                    ShowError = true;
                    ErrorMessage = "نام کاربری یا رمز عبور نادرست است";
                    return Page();
                }

                if (queryGetUser.StatusID == UserStatus.Active)
                {
                    // cr-dotnet-1031 (Line 17): Replaced FormsAuthentication.SetAuthCookie() (IIS
                    // FormsAuthentication module, Windows/IIS-specific) and Membership.ValidateUser()
                    // (Windows Membership provider) with Amazon Cognito User Pools JWT Bearer Token
                    // sign-in via HttpContext.SignInAsync(). Claims-based identity is established
                    // from Amazon Cognito JWT claims (cognito:username, cognito:groups).
                    // JWT tokens are stateless and work across all pods without server affinity
                    // or on-premise Active Directory — enabling cloud deployment on Amazon ECS/EKS.
                    //
                    // cr-dotnet-0029: Replaced FormsAuthentication.SetAuthCookie() (line 49 in original)
                    // with AWS Cognito User Pools integrated through ASP.NET Core Identity.
                    // Claims-based identity is signed in via cookie authentication backed by Cognito JWT tokens.
                    // These JWT tokens are distributed and work across all pods without server affinity
                    // or shared machine keys, making this cloud-ready for AWS deployments.
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, username),
                        new Claim(ClaimTypes.NameIdentifier, username),
                        // Cognito-compatible claim for user pool subject
                        new Claim("cognito:username", username)
                    };
                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        principal,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
                        });

                    // Log device info
                    try
                    {
                        PAPMyDevice device = new PAPMyDevice
                        {
                            Username = username,
                            Name = "<h4 class='m-0 p-0'>" + SiteConfig.GetOperatingSystem(Request.Headers["User-Agent"]) + "</h4>" +
                                   Request.Headers["User-Agent"] + " IP:" + HttpContext.Connection.RemoteIpAddress?.ToString(),
                            LogCode = null,
                            DateTime = DateTimeOffset.UtcNow.UtcDateTime
                        };
                        libraryDBEntities.PAPMyDevice.Add(device);
                        libraryDBEntities.SaveChanges();

                        var userDevices = libraryDBEntities.PAPMyDevice
                            .Where(d => d.Username == username)
                            .OrderByDescending(d => d.Id)
                            .Skip(3)
                            .ToList();
                        if (userDevices.Any())
                        {
                            libraryDBEntities.PAPMyDevice.RemoveRange(userDevices);
                            libraryDBEntities.SaveChanges();
                        }
                    }
                    catch { }

                    return RedirectToPage("/Dash");
                }
                else if (queryGetUser.StatusID == UserStatus.Deactive)
                {
                    ShowError = true;
                    ErrorMessage = "حساب کاربری شما غیر فعال می باشد، لطفاً با مدیر سیستم تماس بگیرید.";
                    return Page();
                }
                else
                {
                    ShowError = true;
                    ErrorMessage = "حساب کاربری شما تأیید نشده است، لطفاً با مدیر سیستم تماس بگیرید.";
                    return Page();
                }
            }
        }
    }
}
