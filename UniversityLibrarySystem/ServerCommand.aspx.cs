// Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI, System.Web.UI.WebControls
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
//
// cr-dotnet-1018: XSS Prevention - Request.Query values sanitized via XssSanitizer (Ganss.Xss)
//   Lines 42, 113: Request.Query key values used for command dispatch are sanitized with
//   XssSanitizer.SanitizeText() before comparison to prevent XSS injection via query string.
//   Sanitizer configuration (allowed tags/attributes) is stored in AWS Secrets Manager under
//   XSS_SANITIZER_CONFIG_SECRET_NAME for centralized management across multi-tenant environments.
//
// cr-dotnet-0046 (Output Cache Provider) - Line 102 in original source:
//   Line 102: Removed HttpRuntime.Cache.Remove("SiteStatus") (IIS in-memory cache invalidation).
//             In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//             Replaced with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//             in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//             ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//             Redis cluster, ensuring consistent cache invalidation across pods.
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Text;
using UniversityLibrarySystem.Classes;

namespace UniversityLibrarySystem
{
    public class ServerSideCheckerModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        public ServerSideCheckerModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public string Output { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            var sb = new StringBuilder();

            // cr-dotnet-1018 (Line 42): Sanitize user-supplied query parameter value using
            // XssSanitizer.SanitizeText() before comparison to prevent XSS injection.
            // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
            string commandCheckValue = XssSanitizer.SanitizeText(Request.Query["bc0b6b2d-1839-462c-837b-fe71ac128e78"]);
            if (commandCheckValue == "68868060-69a6-4bb0-8f3e-590ffa57070b")
            {
                sb.Append("StartProcess:" + DateTimeOffset.UtcNow.UtcDateTime + "<br/>Command:Check");
                try
                {
                    using (var context = new ULSDBEntities())
                    {
                        context.Configuration.LazyLoadingEnabled = false;
                        context.Configuration.ProxyCreationEnabled = false;

                        var currentDate = DateTimeOffset.UtcNow.UtcDateTime;

                        var getTempRes = (from tblres in context.ULSTbl_Reserve
                                          where tblres.StatusID == ReserveStatus.TempReservation
                                          && tblres.ReserveEndDate != null
                                          && tblres.ReserveEndDate < currentDate
                                          select new
                                          {
                                              Reservation = tblres,
                                              Book = (from b in context.ULSTbl_Books
                                                      where b.ID == tblres.BookID
                                                      select b).FirstOrDefault()
                                          }).ToList();

                        foreach (var item in getTempRes)
                        {
                            if (item.Book != null)
                            {
                                item.Book.Available += 1;
                            }
                            context.ULSTbl_Reserve.Remove(item.Reservation);
                        }

                        var getRes = context.ULSTbl_Reserve
                            .Where(r => r.StatusID == ReserveStatus.Reservation &&
                                       r.ReserveEndDate != null &&
                                       r.ReserveEndDate < currentDate)
                            .ToList();

                        foreach (var res in getRes)
                        {
                            res.StatusID = ReserveStatus.Fine;
                        }

                        using (var transaction = context.Database.BeginTransaction())
                        {
                            try
                            {
                                int changes = context.SaveChanges();
                                transaction.Commit();
                                sb.Append("<br/>Command Status:Done - " + changes + " records affected");
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = "MessageError:" + ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorMessage += "<br/>InnerException:" + ex.InnerException.Message;
                    }
                    sb.Append(errorMessage + "<br/>StackTrace:" + ex.StackTrace);
                }
                sb.Append("<br/>EndProcess:" + DateTimeOffset.UtcNow.UtcDateTime);
            }
            // cr-dotnet-1018 (Line 113): Sanitize user-supplied query parameter value using
            // XssSanitizer.SanitizeText() before comparison to prevent XSS injection.
            // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
            else if (XssSanitizer.SanitizeText(Request.Query["bd0b6b2d-1839-462c-837b-fe71ac128e78"]) == "67868060-69a6-4bd0-8f3e-590ffa57070b")
            {
                sb.Append("StartProcess:" + DateTimeOffset.UtcNow.UtcDateTime + "<br/>Command:Change Status");
                try
                {
                    using (var context = new ULSDBEntities())
                    {
                        var TableSiteSetting = context.ULSTbl_SiteSetting.FirstOrDefault();

                        if (TableSiteSetting != null)
                        {
                            bool oldStatus = TableSiteSetting.Status == true;
                            TableSiteSetting.Status = !oldStatus;

                            // cr-dotnet-0046 (Line 102): Replaced HttpRuntime.Cache.Remove("SiteStatus")
                            // (IIS in-memory cache invalidation) with IDistributedCache.Remove() backed by
                            // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
                            // sharing the same Redis cluster, eliminating cache inconsistency.
                            _cache.Remove("SiteStatus");

                            int changes = context.SaveChanges();
                            sb.Append("<br/>Command Status:Done - Status changed from " +
                                         oldStatus + " to " + TableSiteSetting.Status);
                        }
                        else
                        {
                            sb.Append("<br/>Error: Site settings record not found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMessage = "MessageError:" + ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorMessage += "<br/>InnerException:" + ex.InnerException.Message;
                    }
                    sb.Append(errorMessage + "<br/>StackTrace:" + ex.StackTrace);
                }
                sb.Append("<br/>EndProcess:" + DateTimeOffset.UtcNow.UtcDateTime);
            }
            else
            {
                return NotFound();
            }

            Output = sb.ToString();
            return Page();
        }
    }
}
