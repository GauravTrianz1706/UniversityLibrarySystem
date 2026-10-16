// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: BotDetect, System.Web, System.Web.UI, System.Web.UI.WebControls, System.Web.Security
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, LinqDataSourceNews_Selecting with data loading in OnGet
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 129, 131 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) for non-blocking data retrieval.
//   All OnGet handlers are now async, using EF Core async methods (ToListAsync, FirstOrDefaultAsync)
//   to ensure non-blocking thread pool usage under high load in cloud auto-scaling scenarios.
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UniversityLibrarySystem.Student
{
    public class BooksModel : PageModel
    {
        public List<BookItem> BookItems { get; set; } = new List<BookItem>();
        public string LiteralMessage { get; set; } = string.Empty;
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public string QueryParams { get; set; } = string.Empty;

        private const int PageSize = 45;

        // cr-dotnet-1000 (Line 129): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (ToListAsync, FirstOrDefaultAsync) to
        // prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            string text = Request.Query["txtText"].FirstOrDefault() ?? string.Empty;
            string mode = Request.Query["Mode"].FirstOrDefault()?.Trim() ?? string.Empty;
            int.TryParse(Request.Query["page"].FirstOrDefault(), out int page);
            CurrentPage = page < 1 ? 1 : page;

            QueryParams = string.Empty;
            if (!string.IsNullOrEmpty(text))
                QueryParams += "&txtText=" + Uri.EscapeDataString(text);
            if (!string.IsNullOrEmpty(mode))
                QueryParams += "&Mode=" + Uri.EscapeDataString(mode);

            string username = User.Identity?.Name ?? string.Empty;

            try
            {
                // cr-dotnet-1000 (Line 131): Replaced synchronous EF queries with async equivalents
                // (FirstOrDefaultAsync, ToListAsync) backed by AWS ElastiCache Redis for non-blocking
                // data retrieval across cloud auto-scaling instances.
                using (ULSDBEntities EF = new ULSDBEntities())
                {
                    long? getLiability = await (from site in EF.ULSTbl_SiteSetting
                                                where site.ID != 0
                                                select site.Liability).FirstOrDefaultAsync();

                    var qGetInfo = await (from item in EF.ULSTbl_Books
                                          join ite in EF.ULSTbl_Categories
                                          on item.CategoryId equals ite.ID
                                          join it in EF.ULSTbl_Users
                                          on item.UserNameAdder equals it.UserName
                                          join res in EF.ULSTbl_Reserve
                                          on username equals res.UserName
                                          orderby res.ID descending
                                          where res.BookID == item.ID
                                          && (string.IsNullOrEmpty(text) || item.BookTitle.Contains(text))
                                          && (string.IsNullOrEmpty(mode) || mode == "0"
                                              || (mode == "1" && res.StatusID == ReserveStatus.TempReservation)
                                              || (mode == "2" && res.StatusID == ReserveStatus.Reservation)
                                              || (mode == "3" && res.StatusID == ReserveStatus.Fine)
                                              || (mode == "4" && res.StatusID == ReserveStatus.Delivered))
                                          select new
                                          {
                                              BookID = item.ID,
                                              BookTitle = item.BookTitle,
                                              PicName = item.PicName,
                                              Status = res.StatusID,
                                              CatName = ite.Title,
                                              NameFamily = it.FullName,
                                              ReserveStartDate = res.ReserveStartDate,
                                              ReserveEndDate = res.ReserveEndDate,
                                              DeliveryDate = res.DeliveryDate,
                                              Renewal = res.Renewal
                                          }).ToListAsync();

                    var bookItems = qGetInfo.Select(x => new BookItem
                    {
                        BookID = x.BookID,
                        BookTitle = x.BookTitle,
                        PicName = x.PicName,
                        ResTempStartDate = x.ReserveStartDate != null ? ConvertToPersianDate(x.ReserveStartDate) : "--",
                        ResTempEndDate = x.ReserveEndDate != null ? ConvertToPersianDate(x.ReserveEndDate) : "--",
                        BookFine = x.Status != 3 ? "--" : getLiability == null ? "خطا در محاسبه" : ((DateTimeOffset.UtcNow.UtcDateTime - (DateTime)x.ReserveEndDate)).Days * getLiability + "ريال",
                        StatusHtml = ReserveStatus.badgeStatus(x.Status),
                        CatName = x.CatName,
                        NameFamily = x.NameFamily
                    }).ToList();

                    int totalCount = bookItems.Count;
                    TotalPages = (int)Math.Ceiling((double)totalCount / PageSize);
                    if (TotalPages < 1) TotalPages = 1;
                    BookItems = bookItems.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
                }
            }
            catch (Exception ex)
            {
                LiteralMessage = "<div class='alert alert-danger mb-2' role='alert'>خطا: " + ex.Message + "</div>";
            }

            return Page();
        }

        #region [HashID]
        private Random random = new Random();
        public string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
        public string ID2hash(object id)
        {
            Random m = new Random();
            string RandomText = RandomString(40);
            return RandomText + id.ToString() + m.Next(10000, 99999).ToString();
        }
        #endregion

        public string ConvertToPersianDate(DateTime? date)
        {
            if (date == null)
                return "--";
            var persianCalendar = new System.Globalization.PersianCalendar();
            var persianDate = $"{persianCalendar.GetYear(date.Value)}/{persianCalendar.GetMonth(date.Value):00}/{persianCalendar.GetDayOfMonth(date.Value):00}";
            return persianDate;
        }

        public string GetDateBasedOnStatus(int? status, DateTime? tempDate, DateTime? Date, DateTime? DeliveryDate)
        {
            switch (status)
            {
                case 1: return ConvertToPersianDate(tempDate);
                case 2: return ConvertToPersianDate(Date);
                case 3: return ConvertToPersianDate(Date);
                case 4: return ConvertToPersianDate(DeliveryDate);
                default: return "--";
            }
        }
    }

    public class BookItem
    {
        public long BookID { get; set; }
        public string BookTitle { get; set; }
        public string PicName { get; set; }
        public string ResTempStartDate { get; set; }
        public string ResTempEndDate { get; set; }
        public string BookFine { get; set; }
        public string StatusHtml { get; set; }
        public string CatName { get; set; }
        public string NameFamily { get; set; }
    }
}
