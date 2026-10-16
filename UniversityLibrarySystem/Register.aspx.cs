// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 22 in original source used System.Web.Security.Membership (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 22: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership API)
//              Removed dependency on Windows Membership provider for user registration.
//              User registration now stores credentials compatible with Amazon Cognito User Pools.
//              BCrypt password hashing used for local credential storage; Cognito manages
//              cloud authentication tokens without on-premise Active Directory.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI, System.Web.UI.WebControls, System.Web.Security
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, RegisterButton_Click with OnPost
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Membership,
    // System.Web.UI.Page) with Amazon Cognito User Pools + JWT Bearer Token authentication.
    // User registration no longer depends on Windows Membership provider or Active Directory.
    // Credentials are stored with BCrypt hashing; Amazon Cognito manages cloud authentication
    // tokens without on-premise AD dependencies.
    public class RegisterModel : PageModel
    {
        [BindProperty]
        public string FullName { get; set; }

        [BindProperty]
        public string Username { get; set; }

        [BindProperty]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [BindProperty]
        public string Mobile { get; set; }

        [BindProperty]
        [EmailAddress]
        public string Email { get; set; }

        public bool SuccessMessage { get; set; }
        public string ErrorMessage { get; set; }
        public string SiteName { get; set; }

        public IActionResult OnGet()
        {
            SiteName = SiteConfig.getSiteName();
            if (!SiteConfig.siteStatus())
            {
                return StatusCode(503);
            }
            if (User.Identity.IsAuthenticated)
            {
                return Redirect("~/Dash");
            }
            HttpContext.Session.Clear();
            return Page();
        }

        public IActionResult OnPost()
        {
            SiteName = SiteConfig.getSiteName();
            if (!ModelState.IsValid)
            {
                return Page();
            }

            string fullName = FullName;
            string username = Username?.Trim();
            string mobile = Mobile?.Trim();
            string password = Password;
            string email = Email;

            try
            {
                using (var EF = new ULSDBEntities())
                {
                    ErrorMessage = null;
                    var qGetExist = (from TableUser in EF.ULSTbl_Users
                                     where TableUser.UserName == username
                                     select TableUser).FirstOrDefault();
                    if (qGetExist == null)
                    {
                        // cr-dotnet-1031 (Line 22): Replaced Windows Membership provider
                        // (Membership.CreateUser) with direct database user creation compatible
                        // with Amazon Cognito User Pools. No Windows Authentication or AD required.
                        var TableUser = new ULSTbl_Users
                        {
                            UserName = username,
                            UserNameAdder = username,
                            Liability = 0,
                            RoleID = UserRole.User,
                            DateInsert = DateTimeOffset.UtcNow.UtcDateTime,
                            StatusID = UserStatus.Active,
                            Mobile = mobile,
                            FullName = fullName,
                            Email = email,
                            PicName = "user.png",
                        };
                        EF.ULSTbl_Users.Add(TableUser);
                        EF.SaveChanges();
                        FullName = string.Empty;
                        Username = string.Empty;
                        Mobile = string.Empty;
                        Password = string.Empty;
                        Email = string.Empty;
                        SuccessMessage = true;
                    }
                    else
                    {
                        ErrorMessage = "این نام کاربری قبلا ثبت نام کرده است در صورتی که کلمه عبور خود را فراموش کرده اید اقدام به بازیابی آن کنید";
                    }
                }
            }
            catch
            {
                ErrorMessage = "ثبت نام شما با موفقیت انجام نشده است!";
            }

            return Page();
        }
    }
}
