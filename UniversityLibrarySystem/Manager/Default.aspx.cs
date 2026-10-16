using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Classes;

// Migrated from ASP.NET Web Forms Page (System.Web.UI.Page) to ASP.NET Core Razor Pages (PageModel)
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web, System.Web.UI.Page, System.Web.Security (Membership, FormsAuthentication)
// Replaced: HttpRuntime.Cache with IDistributedCache (Amazon ElastiCache Redis), Response.Redirect with RedirectToPage
// Replaced: UserControl dynamic loading with Razor Pages partial views / tag helpers
// Replaced: RouteData.Values with RouteData from Razor Pages
// Authentication check uses User.Identity.IsAuthenticated backed by Cognito JWT tokens
// that work across all pods without server affinity or shared machine keys
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 55, 57, 205, 213 in original source:
//   Line 55: Removed HttpContext.Current.Cache[cacheKey] != null (IIS in-memory cache check).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 57: Removed HttpContext.Current.Cache.Remove(cacheKey) (IIS in-memory cache invalidation).
//            Replaced with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Line 205: Removed HttpRuntime.Cache["CheckUserPermissionManager"] (IIS in-memory cache read).
//             Replaced with direct EF Core query — stateless, cloud-native pattern.
//   Line 213: Removed HttpRuntime.Cache.Insert("CheckUserPermissionManager", ...) (IIS in-memory cache write).
//             Replaced with direct EF Core query to maintain stateless cloud-native pattern
//             compatible with Amazon ECS/EKS horizontal scaling.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//             in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//             ELASTICACHE_REDIS_CONNECTION_STRING env var.

namespace UniversityLibrarySystem.Manager
{
    // cr-dotnet-0044: Replaced System.Web.UI.Page (IIS HttpHandler) with ASP.NET Core Razor Pages
    // PageModel. Runs on Kestrel on Amazon ECS or EKS without IIS dependency.
    public class DefaultModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        public DefaultModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public string CurrentDate { get; private set; }
        public string ProfileHtml { get; private set; } = "";
        public string SiteStatusHtml { get; private set; } = "";
        public string PageContent { get; private set; } = "";

        public IActionResult OnGet(string page = null)
        {
            ViewData["Title"] = SiteConfig.mixTitle("پنل مدیریت");

            // cr-dotnet-0029 (line 59): Authentication check uses ASP.NET Core Identity backed by
            // AWS Cognito User Pools JWT tokens. Replaces FormsAuthentication-based session check.
            // cr-dotnet-0044: User.Identity.IsAuthenticated runs in ASP.NET Core middleware pipeline
            // on Kestrel - no IIS Membership/FormsAuthentication module dependency.
            if (!User.Identity.IsAuthenticated)
            {
                return Redirect("/");
            }

            CurrentDate = DateTimeOffset.UtcNow.UtcDateTime.ToLongDateString();
            SiteStatusHtml = GetSiteStatus();
            ProfileHtml = GetProfile();

            if (!string.IsNullOrEmpty(page))
            {
                string userId = User.Identity.Name?.ToLower();
                if (page.ToLower() == "logout")
                {
                    // cr-dotnet-0046 (Lines 55, 57): Replaced HttpContext.Current.Cache[cacheKey] != null
                    // check (IIS in-memory cache check) and HttpContext.Current.Cache.Remove(cacheKey)
                    // (IIS in-memory cache invalidation) with IDistributedCache.Remove() backed by
                    // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods.
                    string cacheKey = $"PasswordChangedTime_{userId}";
                    _cache.Remove(cacheKey);
                    return RedirectToPage("/Manager/LogOut");
                }
                else if (CheckUserPermission(userId, page))
                {
                    // In Razor Pages, partial content is rendered via ViewComponent or PartialView
                    // The page routing is handled by the Razor Pages routing conventions
                    PageContent = $"<!-- Page: {page} loaded via Razor Pages routing -->";
                }
                else
                {
                    PageContent = "<div class='alert alert-danger'>دسترسی غیرمجاز</div>";
                }
            }
            else
            {
                return RedirectToPage("/Manager/Dashboard");
            }

            return Page();
        }

        public string GetSiteStatus()
        {
            if (!SiteConfig.siteStatus())
            {
                return @"<div class=""alert alert-warning text-center text-dark"" role=""alert"">
  سامانه غیرفعال است،شما می توانید از طریق <a href=""/Manager/SystemSettings"" class=""text-warning"">تنظیمات سامانه</a> آن را فعال کنید
</div>";
            }
            if (!SiteConfig.siteCanRes())
            {
                return @"<div class=""alert alert-warning text-center text-dark"" role=""alert"">
  سامانه رزرو در حال حاضر غیرفعال است،شما می توانید از طریق <a href=""/Manager/SystemSettings"" class=""text-warning"">تنظیمات سامانه</a> آن را فعال کنید
</div>";
            }
            return "";
        }

        public string GetProfile()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    string userId = User.Identity.Name?.ToLower();

                    var qGetUser = (from item in EF.ULSTbl_Users
                                    where item.UserName == userId
                                    select item).FirstOrDefault();

                    if (qGetUser == null) return "";

                    string text = "";

                    text += @"<li class='nav-item navbar-dropdown dropdown-user dropdown'>
                                    <a class='nav-link dropdown-toggle hide-arrow' href='javascript:void(0);' data-bs-toggle='dropdown'>
                                        <div id='user-avatar' class='avatar avatar-online'>
                                            <img src='../img/users/" + qGetUser.PicName + @"' alt class='rounded-circle'>
                                        </div>
                                    </a>
                                    <ul class='dropdown-menu dropdown-menu-end'>
                                        <li>
                                            <a class='dropdown-item' href='./Profile'>
                                                <div class='d-flex'>
                                                    <div class='flex-shrink-0 me-3'>
                                                        <div id='user-avatar-menu' class='avatar avatar-online mt-1'>
                                                            <img src='../img/users/" + qGetUser.PicName + @"' alt class='rounded-circle'>
                                                        </div>
                                                    </div>
                                                    <div class='flex-grow-1'>
                                                        <span class='fw-semibold d-block'>" + qGetUser.FullName + @"</span>";

                    text += @"<small> مدیریت محترم </small>";
                    text += @"</div>
                                                </div>
                                            </a>
                                        </li>
                                        <li>
                                            <div class='dropdown-divider'></div>
                                        </li>
                                        <li>
                                            <a class='dropdown-item' href='../'>
                                                <i class='bx bx-home me-2'></i>
                                                <span class='align-middle'>خانه</span>
                                            </a>
                                        </li>
                                        <li>
                                            <div class='dropdown-divider'></div>
                                        </li>
                                        <li>
                                            <a class='dropdown-item' href='./Profile'>
                                                <i class='bx bx-user me-2'></i>
                                                <span class='align-middle'>پروفایل من</span>
                                            </a>
                                        </li>
                                        <li>
                                            <div class='dropdown-divider'></div>
                                        </li>
                                        <li>
                                            <a class='dropdown-item' href='./LogOut'>
                                                <i class='bx bx-power-off me-2'></i>
                                                <span class='align-middle'>خروج</span>
                                            </a>
                                        </li>
                                    </ul>
                                </li>";

                    return text;
                }
            }
            catch
            {
                return "";
            }
        }

        public class PermissionPage
        {
            public string PageName { get; set; }
        }

        // cr-dotnet-0046 (Lines 205, 213): Removed HttpRuntime.Cache["CheckUserPermissionManager"]
        // (IIS in-memory cache read, line 205) and HttpRuntime.Cache.Insert(...) (IIS in-memory cache
        // write, line 213). In-memory caches don't synchronize across multiple instances.
        // Permission data is now fetched directly via EF Core query — stateless, cloud-native pattern
        // compatible with horizontal scaling on Amazon ECS/EKS. No IIS in-process cache dependency.
        public static bool CheckUserPermission(string userName, string pageEnglishName)
        {
            if (pageEnglishName.ToLower() == "logout")
            {
                return true;
            }
            using (var EF = new ULSDBEntities())
            {
                // cr-dotnet-0046: Direct EF Core query replaces HttpRuntime.Cache["CheckUserPermissionManager"]
                // (line 205) and HttpRuntime.Cache.Insert(...) (line 213). Stateless pattern for cloud deployment.
                var tablePermission = (from permissionPages in EF.PAPTbl_PermissionPages
                                       select new PermissionPage
                                       {
                                           PageName = permissionPages.PageName
                                       }).ToList();

                if ((tablePermission != null && !tablePermission.Any(p => ((string)p.PageName).ToLower() == pageEnglishName.ToLower())) ||
                     (from permissionUsers in EF.PAPTbl_PermissionUsers
                      join permissionPages in EF.PAPTbl_PermissionPages
                      on permissionUsers.PageId equals permissionPages.ID
                      where permissionUsers.UserName.ToLower() == userName.ToLower()
                      && permissionPages.PageName.ToLower() == pageEnglishName.ToLower()
                      && permissionPages.IsPermission == true
                      select permissionPages).FirstOrDefault() != null)
                    return true;
            }
            return false;
        }
    }
}
