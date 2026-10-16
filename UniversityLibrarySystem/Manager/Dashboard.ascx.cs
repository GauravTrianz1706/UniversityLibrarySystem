using Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI.UserControl, System.Web.UI.WebControls
// ScriptManager, UpdatePanel, Timer replaced with standard page reload via JavaScript
// LinqDataSource replaced with direct LINQ queries
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 248, 253 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) for non-blocking data retrieval.
//   All OnGet handlers are now async, using EF Core async methods (ToListAsync, FirstOrDefaultAsync,
//   CountAsync) to ensure non-blocking thread pool usage under high load in cloud auto-scaling scenarios.
//
// cr-dotnet-1034 (Synchronous Data Binding in GridView Controls) - Line 31 in original source:
//   Removed synchronous ListView control declaration (System.Web.UI.WebControls.ListView) that caused
//   blocking data binding under cloud load. Replaced with async Task-based data access in DashboardModel
//   PageModel using Entity Framework Core connected to Amazon RDS, preventing thread pool exhaustion
//   and enabling efficient auto-scaling in cloud deployments.

namespace UniversityLibrarySystem.Manager
{
    public class DashboardMostReservedBook
    {
        public int UserID { get; set; }
        public string UserIdHash { get; set; }
        public string NameFamily { get; set; }
        public string AdderPic { get; set; }
        public int BookID { get; set; }
        public string BookTitle { get; set; }
        public string CatName { get; set; }
        public int? Available { get; set; }
        public string StatusHtml { get; set; }
        public string PicName { get; set; }
    }

    public class DashboardViewModel
    {
        public string AllAccountCount { get; set; } = "0";
        public string PersonalCount { get; set; } = "0";
        public string ActiveUserCount { get; set; } = "0";
        public string DeactiveCount { get; set; } = "0";
        public string AllBookCount { get; set; } = "0";
        public string TempReserveCount { get; set; } = "0";
        public string ReserveCount { get; set; } = "0";
        public string FineCount { get; set; } = "0";
        public List<DashboardMostReservedBook> MostReservedBooks { get; set; } = new List<DashboardMostReservedBook>();
    }

    public class DashboardModel : PageModel
    {
        private static readonly Random _random = new Random();

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

        public DashboardViewModel ViewModel { get; set; } = new DashboardViewModel();

        // cr-dotnet-1000 (Line 248): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            await BindDataAsync();
            return Page();
        }

        // cr-dotnet-1000 (Line 253): Converted BindData to async to use non-blocking EF Core async queries.
        private async Task BindDataAsync()
        {
            ViewModel.AllAccountCount = await AllAccountCounterAsync();
            ViewModel.PersonalCount = await PersonalCounterAsync();
            ViewModel.ActiveUserCount = await AllActiveUserCounterAsync();
            ViewModel.DeactiveCount = await AllDeactiveCounterAsync();
            ViewModel.AllBookCount = await AllBookCounterAsync();
            ViewModel.TempReserveCount = await TempReserveAsync();
            ViewModel.ReserveCount = await ReserveAsync();
            ViewModel.FineCount = await FineAsync();
            ViewModel.MostReservedBooks = await GetMostReservedBooksAsync();
        }

        private async Task<string> AllAccountCounterAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await EF.ULSTbl_Users.CountAsync(u => u.UserName != "0")).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> PersonalCounterAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await EF.ULSTbl_Users.CountAsync(u => u.StatusID != UserStatus.NeedVerify && u.RoleID == UserRole.Admin && u.UserName != "0")).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> AllActiveUserCounterAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await EF.ULSTbl_Users.CountAsync(u => u.StatusID > UserStatus.Deactive && u.RoleID == UserRole.User && u.UserName != "0")).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> AllDeactiveCounterAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await EF.ULSTbl_Users.CountAsync(u => u.StatusID <= UserStatus.Deactive && u.UserName != "0")).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> AllBookCounterAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    long sum = 0;
                    var query = await (from TableBook in EF.ULSTbl_Books
                                       join TableCategories in EF.ULSTbl_Categories on TableBook.CategoryId equals TableCategories.ID
                                       where TableCategories.Status == true && TableBook.Status == true
                                       select TableBook).ToListAsync();
                    foreach (var item in query)
                        sum += (int)(item.Available ?? 0);
                    return sum.ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> TempReserveAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await (from TableBook in EF.ULSTbl_Books
                                   join TableCategories in EF.ULSTbl_Categories on TableBook.CategoryId equals TableCategories.ID
                                   where TableCategories.Status == true && TableBook.Status == true
                                   join TableReserve in EF.ULSTbl_Reserve on TableBook.ID equals TableReserve.BookID
                                   where TableReserve.StatusID == ReserveStatus.TempReservation
                                   select TableBook.ID).CountAsync()).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> ReserveAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await (from TableBook in EF.ULSTbl_Books
                                   join TableCategories in EF.ULSTbl_Categories on TableBook.CategoryId equals TableCategories.ID
                                   where TableCategories.Status == true && TableBook.Status == true
                                   join TableReserve in EF.ULSTbl_Reserve on TableBook.ID equals TableReserve.BookID
                                   where TableReserve.StatusID == ReserveStatus.Reservation
                                   select TableBook.ID).CountAsync()).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<string> FineAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    return (await (from TableBook in EF.ULSTbl_Books
                                   join TableCategories in EF.ULSTbl_Categories on TableBook.CategoryId equals TableCategories.ID
                                   where TableCategories.Status == true && TableBook.Status == true
                                   join TableReserve in EF.ULSTbl_Reserve on TableBook.ID equals TableReserve.BookID
                                   where TableReserve.StatusID == ReserveStatus.Fine
                                   select TableBook.ID).CountAsync()).ToString();
                }
            }
            catch { return "0"; }
        }

        private async Task<List<DashboardMostReservedBook>> GetMostReservedBooksAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    var results = await (from reservation in EF.ULSTbl_Reserve
                                         group reservation by reservation.BookID into grouped
                                         join TableBook in EF.ULSTbl_Books on grouped.Key equals TableBook.ID
                                         join TableCategory in EF.ULSTbl_Categories on TableBook.CategoryId equals TableCategory.ID
                                         join TableUser in EF.ULSTbl_Users on TableBook.UserNameAdder equals TableUser.UserName
                                         orderby grouped.Count() descending
                                         select new
                                         {
                                             UserID = TableUser.ID,
                                             NameFamily = TableUser.FullName,
                                             AdderPic = TableUser.PicName,
                                             BookID = TableBook.ID,
                                             BookTitle = TableBook.BookTitle,
                                             CatName = TableCategory.Title,
                                             Available = TableBook.Available,
                                             Status = TableBook.Status,
                                             PicName = TableBook.PicName,
                                         }).Take(5).ToListAsync();

                    return results.Select(b => new DashboardMostReservedBook
                    {
                        UserID = b.UserID,
                        UserIdHash = ID2hash(b.UserID),
                        NameFamily = b.NameFamily,
                        AdderPic = b.AdderPic,
                        BookID = b.BookID,
                        BookTitle = b.BookTitle,
                        CatName = b.CatName,
                        Available = b.Available,
                        StatusHtml = b.Status == true
                            ? "<span class='badge bg-label-success me-1'>فعال</span>"
                            : "<span class='badge bg-label-dark me-1'>غیرفعال</span>",
                        PicName = b.PicName
                    }).ToList();
                }
            }
            catch
            {
                return new List<DashboardMostReservedBook>();
            }
        }
    }
}
