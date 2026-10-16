using Classes;
using System;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;

// Migrated from ASP.NET Web Forms MasterPage (System.Web.UI.MasterPage) to ASP.NET Core
// The MasterPage concept is replaced by a Razor Layout (_Layout.cshtml).
// Logic previously in Page_Load is now provided as a base controller or action filter
// that populates ViewBag/ViewData for the shared layout.
// Removed: System.Web, System.Web.Security.Membership, System.Web.UI.MasterPage
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 62, 69 in original source:
//   Line 62: Removed HttpRuntime.Cache["ShortAboutUs"] (IIS in-memory cache check).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 69: Removed HttpRuntime.Cache.Insert("ShortAboutUs", ...) (IIS in-memory cache write).
//            Replaced with IDistributedCache backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//            in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//            ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//            Redis cluster, ensuring consistent cache state across pods.

namespace UniversityLibrarySystem
{
    /// <summary>
    /// Provides shared layout data (previously in Main.Master.cs MasterPage code-behind).
    /// Migrated to an ASP.NET Core base controller that populates ViewBag for the Razor layout.
    /// Inheriting controllers call base.OnActionExecuting to populate shared layout data.
    /// cr-dotnet-0046: IMemoryCache replaced with IDistributedCache backed by Amazon ElastiCache Redis.
    /// All application instances share the same Redis cluster — consistent cache across pods.
    /// </summary>
    public abstract class MainLayoutController : Controller
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        protected MainLayoutController(IDistributedCache cache)
        {
            _cache = cache;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            // Site status check (replaces Page_Load site status logic)
            if (!SiteConfig.siteStatus())
            {
                if (!User.Identity.IsAuthenticated)
                {
                    context.Result = StatusCode(503);
                    return;
                }
                else if (User.IsInRole("User"))
                {
                    context.Result = StatusCode(503);
                    return;
                }
                else
                {
                    ViewBag.SiteWarning = @"<div class=""alert alert-warning text-center text-dark"" role=""alert"">
  سامانه غیرفعال است،شما می توانید از طریق <a href=""/Manager/SystemSettings"" class=""text-warning"">تنظیمات سامانه</a> آن را فعال کنید
</div>";
                }
            }
            else if (User.Identity.IsAuthenticated && User.IsInRole("Admin"))
            {
                if (!SiteConfig.siteCanRes())
                {
                    ViewBag.SiteWarning = @"<div class=""alert alert-warning text-center text-dark"" role=""alert"">
  سامانه رزرو در حال حاضر غیرفعال است،شما می توانید از طریق <a href=""/Manager/SystemSettings"" class=""text-warning"">تنظیمات سامانه</a> آن را فعال کنید
</div>";
                }
            }

            // Populate shared layout data
            ViewBag.ShortAboutUs = GetShortAboutUs();
            ViewBag.CategoriesNavbar = CategoriesNavbar();
            ViewBag.CategoriesGroup1 = CategoriesGroup(1);
            ViewBag.CategoriesGroup2 = CategoriesGroup(2);
        }

        public string GetShortAboutUs()
        {
            try
            {
                // cr-dotnet-0046 (Lines 62, 69): Replaced HttpRuntime.Cache["ShortAboutUs"] (IIS in-memory
                // cache check, line 62) and HttpRuntime.Cache.Insert("ShortAboutUs", ...) (IIS in-memory
                // cache write, line 69) with IDistributedCache backed by Amazon ElastiCache Redis.
                // All application instances share the same Redis cluster — no cache inconsistency.
                string result = _cache.GetString("ShortAboutUs");
                if (result != null)
                    return result;

                using (var EF = new ULSDBEntities())
                {
                    var qGetAbout = (from item in EF.ULSTbl_SiteSetting
                                     select item).FirstOrDefault();
                    if (qGetAbout != null)
                    {
                        var cacheOptions = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                        };
                        _cache.SetString("ShortAboutUs", qGetAbout.ShortAboutUs, cacheOptions);
                        return qGetAbout.ShortAboutUs;
                    }
                }
                return "";
            }
            catch
            {
                return "متن مورد نظر یافت نشد";
            }
        }

        public string CategoriesNavbar()
        {
            try
            {
                string html = "";
                var categories = SiteConfig.GetCategories().Take(12);

                if (categories != null && categories.Any())
                {
                    int counter = 1;
                    var queryGetCategories = categories
                        .OrderByDescending(c => c.ID)
                        .Take(12)
                        .Select(c => new { c.ID, c.Title });

                    foreach (var category in queryGetCategories)
                    {
                        if ((counter - 1) % 3 == 0)
                        {
                            if (counter != 1)
                                html += "</ul></div>";
                            html += "<div class='col'><ul>";
                        }
                        html += @"<li><a href='Categories-" + category.ID + "-" + category.Title + @"'>" + category.Title + @"</a></li>";
                        counter++;
                    }

                    if ((counter - 1) % 3 != 0)
                        html += "</ul></div>";
                }

                return html;
            }
            catch
            {
                return "";
            }
        }

        public string CategoriesGroup(int index)
        {
            string html = "";
            var categories = SiteConfig.GetCategories().Take(5);

            if (categories != null && categories.Any())
            {
                var queryGetCategories = categories
                    .OrderByDescending(c => c.ID)
                    .Select(c => new { c.ID, c.Title });

                if (index == 1)
                {
                    foreach (var category in queryGetCategories.Take(5))
                        html += @"<li><a href='Categories-" + category.ID + "-" + category.Title + @"'>" + category.Title + @"</a></li>";
                }
                else
                {
                    foreach (var category in queryGetCategories.Skip(5).Take(5))
                        html += @"<li><a href='Categories-" + category.ID + "-" + category.Title + @"'>" + category.Title + @"</a></li>";
                }
            }

            return html;
        }
    }
}
