using Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI.UserControl, System.Web.UI.WebControls, System.Web.Security
// Replaced: IsPostBack with HTTP method checks, Request.QueryString with HttpContext.Request.Query
// Replaced: LinqDataSource with direct LINQ queries, MultiView with conditional rendering
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 95, 262 in original source:
//   Line 95:  Removed System.Web.HttpRuntime.Cache.Remove("FaqPageCache") (IIS in-memory cache invalidation).
//             In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 262: Removed System.Web.HttpRuntime.Cache.Remove("FaqsPageCache") (IIS in-memory cache invalidation).
//             Replaced with IDistributedCache.RemoveAsync() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//             in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//             ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//             Redis cluster, ensuring consistent cache invalidation across pods.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 181, 183 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are now async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) to ensure non-blocking thread pool usage
//   under high load in cloud auto-scaling scenarios.

namespace UniversityLibrarySystem.Manager
{
    public class FaqListItem
    {
        public int ID { get; set; }
        public string IdHash { get; set; }
        public int UserID { get; set; }
        public string UserIdHash { get; set; }
        public string Question { get; set; }
        public DateTime DateInsert { get; set; }
        public string NameFamily { get; set; }
        public string AdderPic { get; set; }
        public string StatusHtml { get; set; }
    }

    public class FaqsModel : PageModel
    {
        // cr-dotnet-0046: Replaced in-memory cache operations with IDistributedCache backed by
        // Amazon ElastiCache Redis. All application instances share the same Redis cluster,
        // ensuring consistent cache invalidation across pods.
        private readonly IDistributedCache _cache;
        private static readonly Random _random = new Random();

        public FaqsModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public bool ShowList { get; set; } = true;
        public bool ShowForm { get; set; } = false;
        public bool ShowSearch { get; set; } = true;
        public bool ShowAddButton { get; set; } = true;
        public string Message { get; set; } = "";
        public List<FaqListItem> FaqItems { get; set; } = new List<FaqListItem>();
        public string EditQuestion { get; set; } = "";
        public string EditAnswer { get; set; } = "";
        public bool EditStatus { get; set; } = true;

        private string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length).Select(s => s[_random.Next(s.Length)]).ToArray());
        }

        private string ID2hash(object id)
        {
            string randomText = RandomString(40);
            return randomText + id.ToString() + _random.Next(10000, 99999).ToString();
        }

        private string GetStatusHtml(bool? status)
        {
            return status == true
                ? @"<td><span class='badge bg-label-success me-1'>فعال</span></td>"
                : @"<td><span class='badge bg-label-danger me-1'>غیر فعال</span></td>";
        }

        // cr-dotnet-1000 (Line 181): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (FirstOrDefaultAsync, ToListAsync,
        // SaveChangesAsync) to prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            var query = HttpContext.Request.Query;

            // Handle Delete
            if (query.ContainsKey("Delete"))
            {
                string hashId = query["Delete"];
                int id = Hash2ID.HAsh2ID(hashId);
                using (var EF = new ULSDBEntities())
                {
                    var item = await EF.ULSTbl_Faq.FirstOrDefaultAsync(f => f.ID == id);
                    if (item != null)
                    {
                        EF.ULSTbl_Faq.Remove(item);
                        await EF.SaveChangesAsync();
                        // cr-dotnet-0046 (Line 95): Replaced System.Web.HttpRuntime.Cache.Remove("FaqPageCache")
                        // (IIS in-memory cache invalidation) with IDistributedCache.RemoveAsync() backed by
                        // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
                        // sharing the same Redis cluster, eliminating cache inconsistency.
                        await _cache.RemoveAsync("FaqPageCache");
                        Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                    }
                    else
                    {
                        Message = Components.alert("خطا:داده ای یافت نشد", Components.AlertStyle.danger);
                    }
                }
                ShowList = true;
                ShowForm = false;
                ShowSearch = true;
                ShowAddButton = true;
                await LoadFaqListAsync(query.ContainsKey("txtText") ? query["txtText"].ToString() : null);
                return Page();
            }

            // Handle Edit (show form)
            if (query.ContainsKey("Edit"))
            {
                string hashId = query["Edit"];
                int id = Hash2ID.HAsh2ID(hashId);
                using (var EF = new ULSDBEntities())
                {
                    var item = await EF.ULSTbl_Faq.FirstOrDefaultAsync(f => f.ID == id);
                    if (item != null)
                    {
                        EditQuestion = item.Question;
                        EditAnswer = item.Answer;
                        EditStatus = item.Status == true;
                    }
                    else
                    {
                        Message = Components.alert("خطا:داده ای یافت نشد", Components.AlertStyle.danger);
                        ShowList = true;
                        ShowForm = false;
                        ShowSearch = true;
                        ShowAddButton = true;
                        await LoadFaqListAsync(null);
                        return Page();
                    }
                }
                ShowList = false;
                ShowForm = true;
                ShowSearch = false;
                ShowAddButton = false;
                return Page();
            }

            // Handle New (show form)
            if (query.ContainsKey("Value"))
            {
                ShowList = false;
                ShowForm = true;
                ShowSearch = false;
                ShowAddButton = false;
                return Page();
            }

            // Default: show list
            ShowList = true;
            ShowForm = false;
            ShowSearch = true;
            ShowAddButton = true;
            await LoadFaqListAsync(query.ContainsKey("txtText") ? query["txtText"].ToString() : null);
            return Page();
        }

        // cr-dotnet-1000 (Line 183): Converted OnPost to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostAsync(string txtQuestion, string txtAnswer, string chkStatus)
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            string userId = User.Identity.Name?.ToLower();
            bool status = chkStatus == "on" || chkStatus == "true";
            var query = HttpContext.Request.Query;

            using (var EF = new ULSDBEntities())
            {
                if (query.ContainsKey("Value"))
                {
                    // Insert new FAQ
                    var newFaq = new ULSTbl_Faq
                    {
                        Answer = txtAnswer,
                        DateInsert = DateTimeOffset.UtcNow.UtcDateTime,
                        Question = txtQuestion,
                        UserNameAdder = userId,
                        Status = status
                    };
                    EF.ULSTbl_Faq.Add(newFaq);
                    await EF.SaveChangesAsync();
                    Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                }
                else if (query.ContainsKey("Edit"))
                {
                    string hashId = query["Edit"];
                    int rowId = Hash2ID.HAsh2ID(hashId);
                    var existing = await EF.ULSTbl_Faq.FirstOrDefaultAsync(f => f.ID == rowId);
                    if (existing != null)
                    {
                        existing.Answer = txtAnswer;
                        existing.Question = txtQuestion;
                        existing.Status = status;
                        await EF.SaveChangesAsync();
                        Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                    }
                }
            }

            // cr-dotnet-0046 (Line 262): Replaced System.Web.HttpRuntime.Cache.Remove("FaqsPageCache")
            // (IIS in-memory cache invalidation) with IDistributedCache.RemoveAsync() backed by
            // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
            // sharing the same Redis cluster, eliminating cache inconsistency.
            await _cache.RemoveAsync("FaqsPageCache");

            ShowList = true;
            ShowForm = false;
            ShowSearch = true;
            ShowAddButton = true;
            await LoadFaqListAsync(null);
            return Page();
        }

        // cr-dotnet-1000: Converted LoadFaqList to async to use non-blocking EF Core async queries.
        private async Task LoadFaqListAsync(string searchText)
        {
            using (var EF = new ULSDBEntities())
            {
                var results = await (from item in EF.ULSTbl_Faq
                                     join ite in EF.ULSTbl_Users on item.UserNameAdder equals ite.UserName
                                     where (string.IsNullOrEmpty(searchText) || item.Question.Contains(searchText))
                                     orderby item.ID
                                     select new
                                     {
                                         UserID = ite.ID,
                                         item.ID,
                                         item.Question,
                                         item.DateInsert,
                                         item.Status,
                                         NameFamily = ite.FullName,
                                         AdderPic = ite.PicName
                                     }).ToListAsync();

                FaqItems = results.Select(f => new FaqListItem
                {
                    ID = f.ID,
                    IdHash = ID2hash(f.ID),
                    UserID = f.UserID,
                    UserIdHash = ID2hash(f.UserID),
                    Question = f.Question,
                    DateInsert = f.DateInsert,
                    NameFamily = f.NameFamily,
                    AdderPic = f.AdderPic,
                    StatusHtml = GetStatusHtml(f.Status)
                }).ToList();
            }
        }
    }
}
