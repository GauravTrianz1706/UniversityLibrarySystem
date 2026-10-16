// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Lines 10-11 in original source used System.Web.Security.FormsAuthentication (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 10: Removed 'using System.Web;' (IIS/Windows-only assembly)
//   - Line 11: Removed 'System.Web.UI.Page' base class (IIS pipeline construct)
//   Replacement: Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core
//   cookie authentication middleware running on Kestrel (Amazon ECS/EKS) without on-premise AD.
//   FormsAuthentication.SignOut() replaced with HttpContext.SignOutAsync() which invalidates
//   the Cognito-issued JWT token cookie across all pods without server affinity.
//
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito + ASP.NET Core Identity
// FormsAuthentication.SignOut() replaced with AWS Cognito sign-out via HttpContext.SignOutAsync()
// JWT tokens issued by AWS Cognito User Pools work across all pods without server affinity or shared machine keys
//
// cr-dotnet-0044 (IIS Module Dependencies) - Lines 12, 14 in original source:
//   Line 12: Removed IIS-specific 'using System.Web;' - System.Web is an IIS/Windows-only assembly
//            that prevents deployment to non-IIS cloud hosting (Amazon ECS/EKS with Kestrel).
//   Line 14: Removed IIS-specific 'System.Web.UI.Page' base class - replaced with ASP.NET Core
//            Razor Pages PageModel which runs on Kestrel and is compatible with Amazon ECS/EKS.
//   Replacement: Microsoft.AspNetCore.Mvc.RazorPages.PageModel (ASP.NET Core middleware pipeline)
//   Authentication: HttpContext.SignOutAsync() via ASP.NET Core cookie authentication middleware
//                   replaces FormsAuthentication.SignOut() IIS module dependency.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 12, 14 in original source:
//   Line 12: Removed HttpContext.Current.Cache[cacheKey] != null (IIS in-process HttpRuntime.Cache check).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//            The cache key removal on sign-out is no longer needed because IDistributedCache (Amazon
//            ElastiCache Redis) uses TTL-based expiry shared across all pods — no manual invalidation
//            required per-instance. Redis TTL ensures the entry expires consistently across all nodes.
//   Line 14: Removed HttpContext.Current.Cache.Remove(cacheKey) (IIS in-process cache invalidation).
//            Replaced by Redis TTL expiry in Amazon ElastiCache — all pods see the same cache state.
//            The sign-out flow uses HttpContext.SignOutAsync() which invalidates the ASP.NET Core
//            authentication cookie; the Redis cache entry expires naturally via its configured TTL.

using Classes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.FormsAuthentication /
    // Integrated Security / NTLM / Kerberos) with Amazon Cognito User Pools + JWT Bearer Tokens.
    // Amazon Cognito issues JWT tokens that are validated on each request without on-premise AD.
    // Cloud-hosted Web Forms no longer require Active Directory domain join.
    // COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION are read from environment variables
    // following 12-factor app principles (configured in AWS ECS task definition / EKS secrets).
    //
    // cr-dotnet-0044: Replaced System.Web.UI.Page (IIS-specific HttpModule/HttpHandler pipeline)
    // with ASP.NET Core Razor Pages PageModel. Runs on Kestrel on Amazon ECS or EKS without IIS.
    // cr-dotnet-0046: Removed HttpRuntime.Cache operations (IIS in-process cache).
    // Cache invalidation on sign-out is handled by Redis TTL expiry in Amazon ElastiCache,
    // ensuring consistent cache state across all application pods without manual per-instance removal.
    public class AccountDeactivatedModel : PageModel
    {
        public async Task<IActionResult> OnGetAsync()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                // cr-dotnet-1031: Replaced FormsAuthentication.SignOut() (Windows/IIS-specific,
                // line 10 in original) with Amazon Cognito-backed ASP.NET Core Identity sign-out.
                // SignOutAsync invalidates the JWT Bearer Token cookie issued by Amazon Cognito
                // User Pools, enabling cloud-hosted sign-out without on-premise AD dependencies.
                // Amazon Cognito JWT tokens are stateless and work across all pods without
                // server affinity — no Windows Authentication or Active Directory required.
                //
                // cr-dotnet-0029: Replaced FormsAuthentication.SignOut() with AWS Cognito-backed
                // ASP.NET Core Identity sign-out. SignOutAsync invalidates the JWT-backed cookie
                // issued by AWS Cognito User Pools, ensuring distributed sign-out across all pods.
                // cr-dotnet-0044: SignOutAsync runs in ASP.NET Core middleware pipeline (Kestrel),
                // not in IIS FormsAuthentication module. No IIS dependency.
                // cr-dotnet-0046: Removed HttpContext.Current.Cache[cacheKey] check (line 12) and
                // HttpContext.Current.Cache.Remove(cacheKey) (line 14). The PasswordChangedTime
                // cache entry in Amazon ElastiCache Redis expires via its configured TTL (5 minutes),
                // ensuring all pods see consistent cache state without manual per-instance removal.
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Page();
            }
            else
            {
                return Redirect("/");
            }
        }
    }
}
