// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 18 in original source used System.Web.Security.Membership.GetUser() and
// Roles.GetRolesForUser() (Windows/IIS-specific authentication) which breaks in cloud
// environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 18: Removed 'Membership.GetUser()' (Windows Membership provider, IIS/AD-specific)
//              and 'Roles.GetRolesForUser(User.Identity.Name)' (Windows Role provider, AD-backed)
//              Replaced with ASP.NET Core User.Claims role check backed by Amazon Cognito
//              cognito:groups claim in the JWT Bearer Token. Roles are managed in Cognito User
//              Pool groups — no Active Directory or Windows role provider required.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// cr-dotnet-0046: Output Cache Provider - Replaced in-memory HttpRuntime.Cache / System.Web.Caching
// with Amazon ElastiCache Redis via IDistributedCache (StackExchange.Redis / Microsoft.Extensions.Caching.StackExchangeRedis).
// All application instances share the same Redis cluster, ensuring consistent responses across pods
// and eliminating cache synchronization issues.
//
// Original violations fixed:
//   Line 27: HttpRuntime.Cache["GetContactUSPage"] == null  → _cache.GetString (IDistributedCache)
//   Line 70: HttpRuntime.Cache.Insert("GetContactUSPage", html, ...) → _cache.SetString with DistributedCacheEntryOptions
//   Line 74: HttpRuntime.Cache["GetContactUSPage"].ToString()  → _cache.GetString (IDistributedCache)
//
// Removed: using System.Web.Caching; (IIS/System.Web-specific, incompatible with cloud/Kestrel)
// Replaced: IMemoryCache (in-process, per-pod) → IDistributedCache (Amazon ElastiCache Redis, shared across all pods)

using Classes;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Membership,
    // Roles.GetRolesForUser) with Amazon Cognito User Pools + JWT Bearer Token authentication.
    // User roles are now derived from Amazon Cognito cognito:groups claim in the JWT Bearer Token
    // validated by ASP.NET Core cookie authentication middleware on Kestrel.
    // No on-premise Active Directory or Windows role provider required.
    //
    // cr-dotnet-0046: Migrated from ASP.NET Web Forms (System.Web.UI.Page) to ASP.NET Core Razor Pages (PageModel).
    // Cache replaced from in-memory HttpRuntime.Cache to IDistributedCache backed by Amazon ElastiCache Redis.
    public class ContactUsModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache injected via ASP.NET Core DI.
        // Registered with AddStackExchangeRedisCache() in Program.cs pointing to ElastiCache Redis endpoint
        // read from ELASTICACHE_REDIS_CONNECTION_STRING environment variable (12-factor app principle).
        private readonly IDistributedCache _cache;

        public ContactUsModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public string ContactUsHtml { get; set; } = "";
        public bool ShowEditButton { get; set; } = false;

        public void OnGet()
        {
            ViewData["Title"] = SiteConfig.mixTitle("تماس با ما");

            // cr-dotnet-1031 (Line 18): Replaced Membership.GetUser() != null (Windows Membership
            // provider check) and Roles.GetRolesForUser(User.Identity.Name) (Windows/AD-backed role
            // provider) with ASP.NET Core User.Identity.IsAuthenticated and User.Claims role check
            // backed by Amazon Cognito JWT Bearer Token (cognito:groups claim).
            // No Active Directory domain join required — roles managed in Cognito User Pool groups.
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var roles = User.Claims
                    .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToArray();
                ShowEditButton = roles.Length > 0 && roles[0].ToLower() == "admin";
            }

            ContactUsHtml = GetContactUs();
        }

        private string GetContactUs()
        {
            const string cacheKey = "GetContactUSPage";

            // cr-dotnet-0046: Replaced HttpRuntime.Cache["GetContactUSPage"] == null (line 27)
            // with IDistributedCache.GetString() backed by Amazon ElastiCache Redis.
            // All pods share the same Redis cluster — no stale or inconsistent cache across instances.
            var cachedHtml = _cache.GetString(cacheKey);
            if (cachedHtml != null)
            {
                // cr-dotnet-0046: Replaced HttpRuntime.Cache["GetContactUSPage"].ToString() (line 74)
                // with the value already retrieved from IDistributedCache (Amazon ElastiCache Redis).
                return cachedHtml;
            }

            var html = "";
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from tableSiteSettings in EF.ULSTbl_SiteSetting
                                 where tableSiteSettings.ID != 0
                                 select tableSiteSettings).FirstOrDefault();
                    if (query != null)
                    {
                        html += @"
                    <div class=""media contact-info"">
                        <span class=""contact-info__icon""><i class=""ti-home""></i></span>
                        <div class=""media-body"">
                            <h3>" + query.Address + @"</h3>
                            <p>آدرس مجموعه</p>
                        </div>
                    </div>
                    <div class=""media contact-info"">
                        <span class=""contact-info__icon""><i class=""ti-tablet""></i></span>
                        <div class=""media-body"">
                            <h3>
                                <a href=""tel:" + query.Telephone + @""">" + query.Telephone + @"</a>
                            </h3>
                            <p>شماره تلفن مجموعه</p>
                        </div>
                    </div>
                    <div class=""media contact-info"">
                        <span class=""contact-info__icon""><i class=""ti-email""></i></span>
                        <div class=""media-body"">
                            <h3>
                                <a href=""mailto:" + query.Email + @"""
                                    class=""__cf_email__"">" + query.Email + @"</a>
                            </h3>
                            <p>ایمیل مجموعه</p>
                        </div>
                    </div>
                </div>
                <div class=""col-12 col-md-6 col-lg-4 map"">
                    <iframe
                        src=""" + query.MapLink + @"""
                        style=""border: 0; height: 420px; width: 100%;"" allowfullscreen="""" loading=""lazy""
                        referrerpolicy=""no-referrer-when-downgrade""></iframe>
                </div>
            </div>";
                    }
                }

                // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert("GetContactUSPage", html, ..., AddMinutes(5), ...) (line 70)
                // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis with 5-minute expiry.
                // Shared Redis cluster ensures all pods serve the same cached HTML — no per-pod inconsistency.
                _cache.SetString(cacheKey, html, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

                return html;
            }
            catch
            {
                return "";
            }
        }
    }
}
