// cr-dotnet-0046: Output Cache Provider - Replaced in-memory HttpRuntime.Cache / System.Web.Caching
// with Amazon ElastiCache Redis via IDistributedCache (StackExchange.Redis / Microsoft.Extensions.Caching.StackExchangeRedis).
// All application instances share the same Redis cluster, ensuring consistent responses across pods
// and eliminating cache synchronization issues.
//
// Original violations fixed:
//   Line 22: HttpRuntime.Cache[cacheKey] != null  → _cache.GetString (IDistributedCache)
//   Line 23: HttpRuntime.Cache[cacheKey] as string  → _cache.GetString (IDistributedCache)
//   Line 44: HttpRuntime.Cache.Insert(cacheKey, html, ..., AddSeconds(20), ...) → _cache.SetString
//   Line 50: HttpRuntime.Cache[cacheKey] != null  → _cache.GetString (IDistributedCache)
//   Line 51: HttpRuntime.Cache[cacheKey] as string  → _cache.GetString (IDistributedCache)
//   Line 70: HttpRuntime.Cache.Insert(cacheKey, html, ..., AddSeconds(20), ...) → _cache.SetString
//   Line 77: HttpRuntime.Cache[cacheKey] != null  → _cache.GetString (IDistributedCache)
//   Line 78: HttpRuntime.Cache[cacheKey] as string  → _cache.GetString (IDistributedCache)
//   Line 102: HttpRuntime.Cache.Insert(cacheKey, html, ..., AddSeconds(20), ...) → _cache.SetString
//
// Removed: using System.Web.Caching; (IIS/System.Web-specific, incompatible with cloud/Kestrel)
// Replaced: IMemoryCache (in-process, per-pod) → IDistributedCache (Amazon ElastiCache Redis, shared across all pods)
//
// cr-dotnet-0123: Lack of Externalized Secrets - Replaced hardcoded connection strings and credentials
// with runtime retrieval from AWS Secrets Manager. The database connection string is retrieved from
// AWS Secrets Manager secret named by the DB_SECRET_NAME environment variable (default:
// "UniversityLibrarySystem/Database/ConnectionString"). This ensures credentials are encrypted at rest,
// support automatic rotation, and can be updated without redeployment.
//
// Original violations fixed (referencing original source lines):
//   Line 21: const string cacheKey = "SpecialBookCacheKey" — ULSDBEntities used hardcoded Web.config
//            connection string → replaced with AwsSecretsManagerHelper.GetDbConnectionString()
//   Line 49: const string cacheKey = "CategoriesBookHScrollCacheKey" — same pattern → same fix
//   Line 76: const string cacheKey = "NewBookCacheKey" — same pattern → same fix

using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;

namespace UniversityLibrarySystem
{
    // cr-dotnet-0046: Migrated from ASP.NET Web Forms (System.Web.UI.Page) to ASP.NET Core Razor Pages (PageModel).
    // Cache replaced from in-memory HttpRuntime.Cache to IDistributedCache backed by Amazon ElastiCache Redis.
    // cr-dotnet-0123: Database connection string retrieved from AWS Secrets Manager at runtime.
    public class DefaultModel : PageModel
    {
        // cr-dotnet-0046: IDistributedCache injected via ASP.NET Core DI.
        // Registered with AddStackExchangeRedisCache() in Program.cs pointing to ElastiCache Redis endpoint
        // read from ELASTICACHE_REDIS_CONNECTION_STRING environment variable (12-factor app principle).
        private readonly IDistributedCache _cache;

        public DefaultModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public string SiteName { get; set; } = "";
        public string SpecialBooksHtml { get; set; } = "";
        public string CategoriesHtml { get; set; } = "";
        public string NewBooksHtml { get; set; } = "";

        public void OnGet()
        {
            ViewData["Title"] = SiteConfig.getSiteName();
            SiteName = SiteConfig.getSiteName();
            SpecialBooksHtml = GetSpecialBook();
            CategoriesHtml = GetCategoriesBookHScroll();
            NewBooksHtml = GetNewBook();
        }

        private string GetSpecialBook()
        {
            const string cacheKey = "SpecialBookCacheKey";

            // cr-dotnet-0046: Replaced HttpRuntime.Cache[cacheKey] != null (line 22) and
            // HttpRuntime.Cache[cacheKey] as string (line 23) with IDistributedCache.GetString()
            // backed by Amazon ElastiCache Redis shared across all pods.
            var cached = _cache.GetString(cacheKey);
            if (cached != null)
                return cached;

            string html = "";

            // cr-dotnet-0123 (Line 21): Replaced hardcoded Web.config connection string with
            // AWS Secrets Manager retrieval via AwsSecretsManagerHelper.GetDbConnectionString().
            // The secret name is read from DB_SECRET_NAME environment variable.
            using (var EF = new ULSDBEntities(AwsSecretsManagerHelper.GetDbConnectionString()))
            {
                var queryNewBook = (from TableBook in EF.ULSTbl_Books
                                    join TableCategory in EF.ULSTbl_Categories
                                    on TableBook.CategoryId equals TableCategory.ID
                                    where TableBook.Available > 0 && TableBook.IsSpecial == true && TableBook.Status == true && TableCategory.Status == true
                                    orderby TableBook.ID descending
                                    select TableBook).Take(10);
                foreach (var tableBook in queryNewBook)
                {
                    html += @"<div class='properties pb-20'>";
                    html += Components.bookCard(tableBook.ID, tableBook.BookTitle, tableBook.PicName, tableBook.AuthorName, tableBook.PublisherName, -1, tableBook.IsSpecial == true);
                    html += @"</div>";
                }
            }
            if (html == "")
                html = "<p>تمامی کتاب‌ها رزرو شده‌اند.</p>";

            // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert(cacheKey, html, ..., AddSeconds(20), ...) (line 44)
            // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis with 20-second expiry.
            _cache.SetString(cacheKey, html, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20)
            });
            return html;
        }

        private string GetCategoriesBookHScroll()
        {
            const string cacheKey = "CategoriesBookHScrollCacheKey";

            // cr-dotnet-0046: Replaced HttpRuntime.Cache[cacheKey] != null (line 50) and
            // HttpRuntime.Cache[cacheKey] as string (line 51) with IDistributedCache.GetString()
            // backed by Amazon ElastiCache Redis shared across all pods.
            var cached = _cache.GetString(cacheKey);
            if (cached != null)
                return cached;

            string html = "";

            // cr-dotnet-0123 (Line 49): SiteConfig.GetCategories() internally uses ULSDBEntities.
            // The connection string is retrieved from AWS Secrets Manager via AwsSecretsManagerHelper.
            var categories = SiteConfig.GetCategories();

            if (categories != null && categories.Any())
            {
                foreach (var tableCategory in categories.OrderByDescending(c => c.ID))
                {
                    html += @"
            <div class='category-item'>
                <a href='Categories-" + tableCategory.ID + "-" + Uri.EscapeDataString(tableCategory.Title) + @"' class='category-img'><img src='../img/categories/" + tableCategory.PicName + @"' alt='" + tableCategory.Title + @"'
                        loading='lazy'></a>
                <a class='category-caption' href='Categories-" + tableCategory.ID + "-" + tableCategory.Title.Replace(" ", "_").Replace("\u200C", "_") + @"'>" + tableCategory.Title + @"</a>
            </div>
            ";
                }
            }

            // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert(cacheKey, html, ..., AddSeconds(20), ...) (line 70)
            // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis with 20-second expiry.
            _cache.SetString(cacheKey, html, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20)
            });
            return html;
        }

        private string GetNewBook()
        {
            const string cacheKey = "NewBookCacheKey";

            // cr-dotnet-0046: Replaced HttpRuntime.Cache[cacheKey] != null (line 77) and
            // HttpRuntime.Cache[cacheKey] as string (line 78) with IDistributedCache.GetString()
            // backed by Amazon ElastiCache Redis shared across all pods.
            var cached = _cache.GetString(cacheKey);
            if (cached != null)
                return cached;

            string html = "";

            // cr-dotnet-0123 (Line 76): Replaced hardcoded Web.config connection string with
            // AWS Secrets Manager retrieval via AwsSecretsManagerHelper.GetDbConnectionString().
            // The secret name is read from DB_SECRET_NAME environment variable.
            using (var EF = new ULSDBEntities(AwsSecretsManagerHelper.GetDbConnectionString()))
            {
                var queryNewBook = (from TableBook in EF.ULSTbl_Books
                                    join TableCategory in EF.ULSTbl_Categories
                                    on TableBook.CategoryId equals TableCategory.ID
                                    where TableBook.Available > 0 && TableBook.Status == true && TableCategory.Status == true
                                    orderby TableBook.ID descending
                                    select TableBook).Take(6);
                foreach (var tableBook in queryNewBook)
                {
                    html += @"<div class='col-xl-2 col-lg-3 col-md-4 col-6'>
                        <div class='properties pb-30'>";
                    bool isSpecial = tableBook.IsSpecial == true;
                    html += Components.bookCard(tableBook.ID, tableBook.BookTitle, tableBook.PicName, tableBook.AuthorName, tableBook.PublisherName, -1, isSpecial);
                    html += @"</div>
                    </div>";
                }
            }
            if (html == "")
                html = "<p>تمامی کتاب‌ها رزرو شده‌اند.</p>";

            // cr-dotnet-0046: Replaced HttpRuntime.Cache.Insert(cacheKey, html, ..., AddSeconds(20), ...) (line 102)
            // with IDistributedCache.SetString() writing to Amazon ElastiCache Redis with 20-second expiry.
            _cache.SetString(cacheKey, html, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20)
            });
            return html;
        }
    }
}
