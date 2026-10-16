using Classes;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using UniversityLibrarySystem.Classes;

// Migrated from ASP.NET Web Forms MasterPage (System.Web.UI.MasterPage) to ASP.NET Core
// The MasterPage concept is replaced by a Razor Layout (_Layout.cshtml).
// Logic previously in Page_Load is now provided as a base controller or action filter
// that populates ViewBag/ViewData for the shared layout.
// Removed: System.Web, System.Web.Security.Membership, System.Web.UI.MasterPage
//
// cr-dotnet-1018: XSS Prevention - ViewBag HTML values sanitized via XssSanitizer (Ganss.Xss)
//   Lines 65, 74, 81, 82, 83, 84: ViewBag assignments containing HTML strings with dynamic
//   content (category titles, about-us text from DB) are now sanitized using
//   XssSanitizer.SanitizeHtml() before assignment. Sanitizer configuration (allowed
//   tags/attributes) is stored in AWS Secrets Manager under XSS_SANITIZER_CONFIG_SECRET_NAME
//   for centralized management across multi-tenant cloud environments.
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
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 55, 60:
//   Replaced synchronous OnActionExecuting override with async OnActionExecutionAsync to enable
//   non-blocking async/await throughout the action filter pipeline.
//   Replaced synchronous _cache.GetString / _cache.SetString (lines 55, 60 in original) with
//   async _cache.GetStringAsync / _cache.SetStringAsync backed by Amazon ElastiCache Redis,
//   ensuring non-blocking data retrieval and efficient thread pool usage under cloud auto-scaling.

namespace UniversityLibrarySystem
{
    /// <summary>
    /// Provides shared layout data (previously in Main.Master.cs MasterPage code-behind).
    /// Migrated to an ASP.NET Core base controller that populates ViewBag for the Razor layout.
    /// Inheriting controllers call base.OnActionExecutionAsync to populate shared layout data.
    /// cr-dotnet-0046: IMemoryCache replaced with IDistributedCache backed by Amazon ElastiCache Redis.
    /// All application instances share the same Redis cluster — consistent cache across pods.
    /// cr-dotnet-1000: OnActionExecuting replaced with async OnActionExecutionAsync; synchronous
    /// cache calls replaced with async equivalents for non-blocking cloud-ready operation.
    /// cr-dotnet-1018: ViewBag HTML values sanitized via XssSanitizer (Ganss.Xss) before assignment.
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

        // cr-dotnet-1000 (Lines 55, 60): Replaced synchronous OnActionExecuting override with
        // async OnActionExecutionAsync to enable non-blocking async/await throughout the action
        // filter pipeline. This prevents request thread blocking under cloud auto-scaling load.
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Site status check (replaces Page_Load site status logic)
            if (!SiteConfig.siteStatus())
            {
                if (!User.Identity.IsAuthenticated)
                {
                    // cr-dotnet-1000 (Line 55): Converted to async pipeline — result set
                    // non-blockingly within the async action filter.
                    context.Result = StatusCode(503);
                    return;
                }
                else if (User.IsInRole("User"))
                {
                    // cr-dotnet-1000 (Line 60): Converted to async pipeline — result set
                    // non-blockingly within the async action filter.
                    context.Result = StatusCode(503);
                    return;
                }
                else
                {
                    // cr-dotnet-1018 (Line 81): Sanitize HTML string assigned to ViewBag.SiteWarning
                    // using XssSanitizer (Ganss.Xss) to prevent XSS injection in rendered output.
                    // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                    ViewBag.SiteWarning = XssSanitizer.SanitizeHtml(@"<div class=""alert alert-warning text-center text-dark"" role=""alert"">
  سامانه غیرفعال است،شما می توانید از طریق <a href=""/Manager/SystemSettings"" class=""text-warning"">تنظیمات سامانه</a> آن را فعال کنید
</div>");
                }
            }
            else if (User.Identity.IsAuthenticated && User.IsInRole("Admin"))
            {
                if (!SiteConfig.siteCanRes())
                {
                    // cr-dotnet-1018 (Line 84): Sanitize HTML string assigned to ViewBag.SiteWarning
                    // using XssSanitizer (Ganss.Xss) to prevent XSS injection in rendered output.
                    // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                    ViewBag.SiteWarning = XssSanitizer.SanitizeHtml(@"<div class=""alert alert-warning text-center text-dark"" role=""alert"">
  سامانه رزرو در حال حاضر غیرفعال است،شما می توانید از طریق <a href=""/Manager/SystemSettings"" class=""text-warning"">تنظیمات سامانه</a> آن را فعال کنید
</div>");
                }
            }

            // cr-dotnet-1018 (Lines 65, 74, 82, 83): Sanitize HTML values assigned to ViewBag
            // using XssSanitizer (Ganss.Xss) before assignment. Dynamic content from the database
            // (ShortAboutUs, category titles) is sanitized to prevent stored XSS attacks.
            // Sanitizer config (allowed tags/attributes) loaded from AWS Secrets Manager
            // (XSS_SANITIZER_CONFIG_SECRET_NAME) for centralized multi-tenant management.
            string shortAboutUs = await GetShortAboutUsAsync().ConfigureAwait(false);
            ViewBag.ShortAboutUs = await XssSanitizer.SanitizeHtmlAsync(shortAboutUs).ConfigureAwait(false);
            ViewBag.CategoriesNavbar = XssSanitizer.SanitizeHtml(CategoriesNavbar());
            ViewBag.CategoriesGroup1 = XssSanitizer.SanitizeHtml(CategoriesGroup(1));
            ViewBag.CategoriesGroup2 = XssSanitizer.SanitizeHtml(CategoriesGroup(2));

            await next().ConfigureAwait(false);
        }

        public async Task<string> GetShortAboutUsAsync()
        {
            try
            {
                // cr-dotnet-0046 (Lines 62, 69): Replaced HttpRuntime.Cache["ShortAboutUs"] (IIS in-memory
                // cache check, line 62) and HttpRuntime.Cache.Insert("ShortAboutUs", ...) (IIS in-memory
                // cache write, line 69) with IDistributedCache backed by Amazon ElastiCache Redis.
                // All application instances share the same Redis cluster — no cache inconsistency.
                //
                // cr-dotnet-1000 (Lines 55, 60): Replaced synchronous _cache.GetString (line 55 in
                // original) and _cache.SetString (line 60 in original) with async
                // _cache.GetStringAsync / _cache.SetStringAsync to ensure non-blocking data retrieval
                // and efficient thread pool usage under cloud auto-scaling load.
                string result = await _cache.GetStringAsync("ShortAboutUs").ConfigureAwait(false);
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
                        await _cache.SetStringAsync("ShortAboutUs", qGetAbout.ShortAboutUs, cacheOptions).ConfigureAwait(false);
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
                        // cr-dotnet-1018: Category title from DB encoded to prevent stored XSS
                        html += @"<li><a href='Categories-" + category.ID + "-" + XssSanitizer.SanitizeText(category.Title) + @"'>" + XssSanitizer.SanitizeText(category.Title) + @"</a></li>";
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
                        // cr-dotnet-1018: Category title from DB encoded to prevent stored XSS
                        html += @"<li><a href='Categories-" + category.ID + "-" + XssSanitizer.SanitizeText(category.Title) + @"'>" + XssSanitizer.SanitizeText(category.Title) + @"</a></li>";
                }
                else
                {
                    foreach (var category in queryGetCategories.Skip(5).Take(5))
                        // cr-dotnet-1018: Category title from DB encoded to prevent stored XSS
                        html += @"<li><a href='Categories-" + category.ID + "-" + XssSanitizer.SanitizeText(category.Title) + @"'>" + XssSanitizer.SanitizeText(category.Title) + @"</a></li>";
                }
            }

            return html;
        }
    }
}
