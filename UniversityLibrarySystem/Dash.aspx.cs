// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Lines 13 and 15 in original source used System.Web.Security.Roles.GetRolesForUser()
// (Windows/IIS-specific role provider backed by Active Directory) which breaks in cloud
// environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 13: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership/Roles API)
//   - Line 15: Removed 'Roles.GetRolesForUser(username)' (Windows/AD-backed role provider)
//              Replaced with ASP.NET Core User.Claims role check backed by Amazon Cognito
//              cognito:groups claim in the JWT Bearer Token. Roles are managed in Cognito User
//              Pool groups — no Active Directory or Windows role provider required.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.

using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Roles.GetRolesForUser,
    // System.Web.UI.Page) with Amazon Cognito User Pools + JWT Bearer Token authentication.
    // User roles are now derived from Amazon Cognito cognito:groups claim in the JWT Bearer Token
    // validated by ASP.NET Core cookie authentication middleware on Kestrel.
    // No on-premise Active Directory or Windows role provider required.
    public class DashModel : PageModel
    {
        public IActionResult OnGet()
        {
            ViewData["Title"] = SiteConfig.mixTitle("داشبورد");
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                // cr-dotnet-1031 (Line 13): Replaced 'using System.Web.Security;' (Windows/IIS-specific
                // Membership/Roles API) — removed entirely. No Windows Authentication dependency.
                //
                // cr-dotnet-1031 (Line 15): Replaced Roles.GetRolesForUser(username) (Windows/AD-backed
                // role provider) with ASP.NET Core User.Claims role check backed by Amazon Cognito
                // cognito:groups claim in the JWT Bearer Token. Roles are managed in Cognito User
                // Pool groups — no Active Directory domain join required.
                string username = User.Identity.Name;
                var roles = User.Claims
                    .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToArray();
                if (roles.Length > 0)
                {
                    string role = roles[0];
                    if (role.ToLower() == "user")
                        return Redirect("/Student/Books");
                    else
                        return Redirect("/Manager/Dashboard");
                }
            }
            return Redirect("/Login");
        }
    }
}
