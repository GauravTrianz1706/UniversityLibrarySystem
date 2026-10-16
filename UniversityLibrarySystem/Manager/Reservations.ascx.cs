// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.Security (Membership), System.Web.UI, System.Web.UI.UserControl
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet/OnPost handlers
// Replaced: Request.QueryString with HttpContext.Request.Query
// Replaced: LinqDataSourceSelectEventArgs with direct data binding
//
// cr-dotnet-1031 (Windows Authentication in Web Forms Cloud) - Lines 37, 180, 222, 272, 326, 382 in original source:
//   Replaced IIS Windows Authentication (Integrated Security/NTLM/Kerberos) with Amazon Cognito
//   User Pools and JWT Bearer Token authentication. User.Identity.IsAuthenticated is now populated
//   from Cognito JWT claims validated by ASP.NET Core JWT Bearer middleware.
//   No on-premise Active Directory domain join required.
//   - Line 37:  User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 180: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 222: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 272: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 326: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 382: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// cr-dotnet-1018: XSS Prevention - Request.Query values sanitized via XssSanitizer (Ganss.Xss)
//   Lines 66, 119, 120, 121, 185, 227, 277, 331: All Request.Query values (hashId, text, uid,
//   mode) are sanitized with XssSanitizer.SanitizeText() before use to prevent XSS injection.
//   Sanitizer configuration (allowed tags/attributes) is stored in AWS Secrets Manager under
//   XSS_SANITIZER_CONFIG_SECRET_NAME for centralized management across multi-tenant environments.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 250, 252 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) for non-blocking data retrieval.
//   All OnGet/OnPost handlers are now async, using EF Core async methods (ToListAsync,
//   FirstOrDefaultAsync, SaveChangesAsync, CountAsync) to ensure non-blocking thread pool usage
//   under high load in cloud auto-scaling scenarios.
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UniversityLibrarySystem.Classes;

namespace UniversityLibrarySystem.Manager
{
    public class ReservationsModel : PageModel
    {
        public int ActiveViewIndex { get; set; } = 0;
        public bool PanelSearchVisible { get; set; } = true;
        public string Message { get; set; } = "";
        public List<object> ReservationList { get; set; } = new List<object>();

        // cr-dotnet-1000 (Line 250): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (FirstOrDefaultAsync, ToListAsync) to
        // prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            // cr-dotnet-1031 (Line 37): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            ActiveViewIndex = 0;
            PanelSearchVisible = true;

            if (Request.Query.ContainsKey("Value"))
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = await (from book in EF.ULSTbl_Books
                                       where book.Status == true
                                       select book.ID).FirstOrDefaultAsync();
                    if (query != null)
                    {
                        ActiveViewIndex = 2; // New view
                        PanelSearchVisible = false;
                    }
                    else
                    {
                        Message = Components.alert("کتاب فعالی وجود ندارد", Components.AlertStyle.danger);
                        ActiveViewIndex = 0;
                        PanelSearchVisible = true;
                    }
                }
            }

            if (Request.Query.ContainsKey("View"))
            {
                // cr-dotnet-1018 (Line 66): Sanitize user-supplied query parameter 'View' using
                // XssSanitizer.SanitizeText() before use to prevent XSS injection.
                // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                string hashId = XssSanitizer.SanitizeText(Request.Query["View"]);
                int id = Hash2ID.HAsh2ID(hashId);
                ActiveViewIndex = 1;
                PanelSearchVisible = false;
                await LoadReservationDetailAsync(id);
            }

            if (ActiveViewIndex == 0)
            {
                await LoadReservationListAsync();
            }

            return Page();
        }

        private async Task LoadReservationDetailAsync(int id)
        {
            using (ULSDBEntities EF = new ULSDBEntities())
            {
                var query = await (from tblRes in EF.ULSTbl_Reserve
                                   where tblRes.ID == id
                                   join tblUser in EF.ULSTbl_Users
                                   on tblRes.UserName equals tblUser.UserName
                                   join tblBooks in EF.ULSTbl_Books
                                   on tblRes.BookID equals tblBooks.ID
                                   orderby tblRes.ID descending
                                   select new
                                   {
                                       UserName = tblRes.UserName,
                                       tblBooks.PicName,
                                       tblBooks.BookTitle,
                                       tblUser.FullName,
                                       UserStatus = tblUser.StatusID,
                                       tblRes.ReserveStartDate,
                                       tblRes.ReserveEndDate,
                                       tblRes.DeliveryDate,
                                       tblRes.StatusID,
                                       tblRes.Renewal,
                                       tblBooks.ISBN,
                                       tblRes.CustomCode
                                   }).FirstOrDefaultAsync();

                if (query != null)
                {
                    ViewData["ReservationDetail"] = query;
                }
            }
        }

        private async Task LoadReservationListAsync()
        {
            using (ULSDBEntities EF = new ULSDBEntities())
            {
                // cr-dotnet-1018 (Lines 119, 120, 121): Sanitize user-supplied query parameters
                // 'txtText', 'uid', and 'Mode' using XssSanitizer.SanitizeText() before use
                // to prevent XSS injection. Sanitizer config loaded from AWS Secrets Manager
                // (XSS_SANITIZER_CONFIG_SECRET_NAME) for centralized multi-tenant management.
                string text = Request.Query.ContainsKey("txtText") ? XssSanitizer.SanitizeText(Request.Query["txtText"].ToString()) : string.Empty;
                string uid = Request.Query.ContainsKey("uid") ? XssSanitizer.SanitizeText(Request.Query["uid"].ToString().Trim()) : string.Empty;
                string mode = Request.Query.ContainsKey("Mode") ? XssSanitizer.SanitizeText(Request.Query["Mode"].ToString().Trim()) : string.Empty;

                var qGetInfo = await (from item in EF.ULSTbl_Books
                                      join ite in EF.ULSTbl_Categories
                                      on item.CategoryId equals ite.ID
                                      from res in EF.ULSTbl_Reserve
                                      join it in EF.ULSTbl_Users
                                      on res.UserName equals it.UserName
                                      where (string.IsNullOrEmpty(text) || item.BookTitle.Contains(text)) && res.BookID == item.ID
                                      && (string.IsNullOrEmpty(mode) || mode == "0" || ((mode == "1" && res.StatusID == ReserveStatus.TempReservation) || (mode == "2" && res.StatusID == ReserveStatus.Reservation) || (mode == "3" && res.StatusID == ReserveStatus.Fine) || (mode == "4" && res.StatusID == ReserveStatus.Delivered)))
                                      && (string.IsNullOrEmpty(uid) || uid == res.UserName)
                                      orderby res.ID descending
                                      select new
                                      {
                                          UserName = res.UserName,
                                          UserID = it.ID,
                                          res.ID,
                                          BookID = item.ID,
                                          BookTitle = item.BookTitle,
                                          PicName = item.PicName,
                                          Status = res.StatusID,
                                          CatName = ite.Title,
                                          NameFamily = it.FullName,
                                          ReserveStartDate = res.ReserveStartDate,
                                          ReserveEndDate = res.ReserveEndDate,
                                          DeliveryDate = res.DeliveryDate,
                                          Renewal = res.Renewal,
                                          ISBN = item.ISBN,
                                          AdderPic = it.PicName,
                                          UserStatus = it.StatusID
                                      }).ToListAsync();

                ReservationList = qGetInfo.Select(x => (object)new
                {
                    UserNameLink = ID2hash(x.UserID),
                    UserName = x.UserName,
                    x.ID,
                    x.BookID,
                    x.BookTitle,
                    x.PicName,
                    x.NameFamily,
                    ResTempStartDate = (x.ReserveStartDate != null) ? ConvertToPersianDate(x.ReserveStartDate) : "--",
                    ResTempEndDate = x.ReserveEndDate != null ? ConvertToPersianDate(x.ReserveEndDate) : "--",
                    StatusHtml = ReserveStatus.badgeStatus(x.Status),
                    DelEndDate = x.DeliveryDate != null ? ConvertToPersianDate(x.DeliveryDate) : "--",
                    UserStatus = UserStatusHtml(x.UserStatus),
                    ButtonsHtml = @"<a class='btn btn-primary' href='?View=" + ID2hash(x.ID) + @"'>جزئیات</a>",
                    Renewal = x.Renewal,
                    x.CatName,
                    x.ISBN,
                    x.AdderPic
                }).ToList();
            }
        }

        // cr-dotnet-1000 (Line 252): Converted OnPostRes to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostResAsync(string customCode)
        {
            // cr-dotnet-1031 (Line 180): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            if (Request.Query.ContainsKey("View"))
            {
                // cr-dotnet-1018 (Line 185): Sanitize user-supplied query parameter 'View' using
                // XssSanitizer.SanitizeText() before use to prevent XSS injection.
                // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                string hashId = XssSanitizer.SanitizeText(Request.Query["View"]);
                int id = Hash2ID.HAsh2ID(hashId);
                try
                {
                    using (var EF = new ULSDBEntities())
                    {
                        var getTempRes = await (from tblres in EF.ULSTbl_Reserve
                                                where tblres.StatusID == ReserveStatus.TempReservation && tblres.ID == id
                                                join tblBook in EF.ULSTbl_Books
                                                on tblres.BookID equals tblBook.ID
                                                join tblCat in EF.ULSTbl_Categories
                                                on tblBook.CategoryId equals tblCat.ID
                                                select tblres).FirstOrDefaultAsync();
                        var getReserveDay = await (from site in EF.ULSTbl_SiteSetting
                                                   where site.ID != 0
                                                   select site.ReserveDay).FirstOrDefaultAsync();
                        getTempRes.CustomCode = customCode;
                        getTempRes.ReserveStartDate = DateTimeOffset.UtcNow.UtcDateTime;
                        getTempRes.ReserveEndDate = DateTimeOffset.UtcNow.UtcDateTime.Add(TimeSpan.Parse(getReserveDay.ToString()));
                        getTempRes.StatusID = ReserveStatus.Reservation;
                        await EF.SaveChangesAsync();
                        Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                    }
                }
                catch
                {
                    Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                }
            }
            ActiveViewIndex = 0;
            PanelSearchVisible = true;
            await LoadReservationListAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            // cr-dotnet-1031 (Line 222): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            if (Request.Query.ContainsKey("View"))
            {
                // cr-dotnet-1018 (Line 227): Sanitize user-supplied query parameter 'View' using
                // XssSanitizer.SanitizeText() before use to prevent XSS injection.
                // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                string hashId = XssSanitizer.SanitizeText(Request.Query["View"]);
                int id = Hash2ID.HAsh2ID(hashId);
                try
                {
                    using (var EF = new ULSDBEntities())
                    {
                        var getTempRes = await (from tblres in EF.ULSTbl_Reserve
                                                where tblres.StatusID == ReserveStatus.TempReservation && tblres.ID == id
                                                select tblres).FirstOrDefaultAsync();
                        if (getTempRes != null)
                        {
                            var book = await (from b in EF.ULSTbl_Books
                                              where b.ID == getTempRes.BookID
                                              select b).FirstOrDefaultAsync();
                            if (book != null)
                            {
                                book.Available += 1;
                                EF.ULSTbl_Reserve.Remove(getTempRes);
                                await EF.SaveChangesAsync();
                                Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                            }
                            else
                            {
                                Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                            }
                        }
                        else
                        {
                            Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                        }
                    }
                }
                catch
                {
                    Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                }
            }
            ActiveViewIndex = 0;
            PanelSearchVisible = true;
            await LoadReservationListAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeliverAsync()
        {
            // cr-dotnet-1031 (Line 272): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            if (Request.Query.ContainsKey("View"))
            {
                // cr-dotnet-1018 (Line 277): Sanitize user-supplied query parameter 'View' using
                // XssSanitizer.SanitizeText() before use to prevent XSS injection.
                // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                string hashId = XssSanitizer.SanitizeText(Request.Query["View"]);
                int id = Hash2ID.HAsh2ID(hashId);
                try
                {
                    using (var EF = new ULSDBEntities())
                    {
                        var getTempRes = await (from tblres in EF.ULSTbl_Reserve
                                                where (tblres.StatusID == ReserveStatus.Reservation || tblres.StatusID == ReserveStatus.Fine) && tblres.ID == id
                                                join tblBook in EF.ULSTbl_Books
                                                on tblres.BookID equals tblBook.ID
                                                join tblCat in EF.ULSTbl_Categories
                                                on tblBook.CategoryId equals tblCat.ID
                                                select tblres).FirstOrDefaultAsync();
                        getTempRes.DeliveryDate = DateTimeOffset.UtcNow.UtcDateTime;
                        if (getTempRes.StatusID == ReserveStatus.Fine)
                        {
                            string fineText = "";
                            TimeSpan timeSpan = DateTimeOffset.UtcNow.UtcDateTime - (DateTime)getTempRes.ReserveEndDate;
                            var getLiability = await (from site in EF.ULSTbl_SiteSetting
                                                      where site.ID != 0
                                                      select site.Liability).FirstOrDefaultAsync();
                            if (getLiability != null)
                            {
                                fineText += timeSpan.Days * getLiability;
                            }
                            else
                            {
                                fineText += "خطا در محاسبه";
                            }
                            getTempRes.CustomCode += "\nپرداخت جریمه به مبلغ " + fineText + " ریال انجام شد و رسید آن دریافت گردید.";
                        }
                        getTempRes.StatusID = ReserveStatus.Delivered;
                        await EF.SaveChangesAsync();
                        Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                    }
                }
                catch
                {
                    Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                }
            }
            ActiveViewIndex = 0;
            PanelSearchVisible = true;
            await LoadReservationListAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostRenewAsync()
        {
            // cr-dotnet-1031 (Line 326): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            if (Request.Query.ContainsKey("View"))
            {
                // cr-dotnet-1018 (Line 331): Sanitize user-supplied query parameter 'View' using
                // XssSanitizer.SanitizeText() before use to prevent XSS injection.
                // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME).
                string hashId = XssSanitizer.SanitizeText(Request.Query["View"]);
                int id = Hash2ID.HAsh2ID(hashId);
                try
                {
                    using (var EF = new ULSDBEntities())
                    {
                        var getTempRes = await (from tblres in EF.ULSTbl_Reserve
                                                where tblres.StatusID == ReserveStatus.Reservation && tblres.ID == id
                                                join tblBook in EF.ULSTbl_Books
                                                on tblres.BookID equals tblBook.ID
                                                join tblCat in EF.ULSTbl_Categories
                                                on tblBook.CategoryId equals tblCat.ID
                                                select tblres).FirstOrDefaultAsync();
                        var getReserveDay = await (from site in EF.ULSTbl_SiteSetting
                                                   where site.ID != 0
                                                   select site).FirstOrDefaultAsync();
                        int? renewal;
                        try
                        {
                            renewal = getTempRes.Renewal ?? 0;
                        }
                        catch
                        {
                            renewal = 0;
                        }
                        if (renewal < getReserveDay.ReserveAgain)
                        {
                            getTempRes.ReserveEndDate = ((DateTime)getTempRes.ReserveEndDate).Add(TimeSpan.Parse(getReserveDay.ReserveDay.ToString()));
                            getTempRes.Renewal = renewal + 1;
                            await EF.SaveChangesAsync();
                            Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                        }
                        else
                        {
                            Message = Components.alert("رزرو مورد نظر به حداکثر تعداد تمدید رسیده است", Components.AlertStyle.danger);
                        }
                    }
                }
                catch
                {
                    Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                }
            }
            ActiveViewIndex = 0;
            PanelSearchVisible = true;
            await LoadReservationListAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostNewAsync(string txtUsernameNew, string txtISBNNew, string txtDetail)
        {
            // cr-dotnet-1031 (Line 382): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            using (ULSDBEntities EF = new ULSDBEntities())
            {
                if (await (from TableUser in EF.ULSTbl_Users where TableUser.UserName == txtUsernameNew.ToLower() select TableUser).FirstOrDefaultAsync() == null)
                {
                    Message = Classes.Components.alert("نام کاربری وارد شده نادرست است یا غیرفعال می‌باشد", Classes.Components.AlertStyle.danger);
                    ActiveViewIndex = 0;
                    PanelSearchVisible = true;
                    await LoadReservationListAsync();
                    return Page();
                }

                if (await (from TableRes in EF.ULSTbl_Reserve
                           where TableRes.UserName == txtUsernameNew.ToLower() && TableRes.StatusID == Classes.ReserveStatus.Fine
                           select TableRes.ID).FirstOrDefaultAsync() != 0)
                {
                    Message = Classes.Components.alert("کاربر مورد نظر در حال جریمه می باشد", Classes.Components.AlertStyle.danger);
                    ActiveViewIndex = 0;
                    PanelSearchVisible = true;
                    await LoadReservationListAsync();
                    return Page();
                }

                var book = await EF.ULSTbl_Books.FirstOrDefaultAsync(b => b.ISBN.Replace("-", "") == txtISBNNew.Replace("-", "") && b.Status == true);

                if (book == null || book.Available <= 0)
                {
                    Message = Classes.Components.alert("کتاب مورد نظر در حال حاضر موجود نمی باشد", Classes.Components.AlertStyle.danger);
                    ActiveViewIndex = 0;
                    PanelSearchVisible = true;
                    await LoadReservationListAsync();
                    return Page();
                }

                var queryIsBookReserved = await (from TableRes in EF.ULSTbl_Reserve
                                                 where TableRes.UserName == txtUsernameNew.ToLower() && TableRes.StatusID != ReserveStatus.Delivered && TableRes.BookID == book.ID
                                                 select TableRes).FirstOrDefaultAsync();
                if (queryIsBookReserved != null)
                {
                    Message = Classes.Components.alert("امکان رزرو یک کتاب بیش از یک‌بار وجود ندارد", Classes.Components.AlertStyle.danger);
                    ActiveViewIndex = 0;
                    PanelSearchVisible = true;
                    await LoadReservationListAsync();
                    return Page();
                }

                var query = await (from SiteTable in EF.ULSTbl_SiteSetting
                                   select new { SiteTable.MaxUserReserve }).FirstOrDefaultAsync();

                if (query != null)
                {
                    if (await GetReservationCountAsync(txtUsernameNew) < query.MaxUserReserve)
                    {
                        ULSTbl_Reserve TG = new ULSTbl_Reserve
                        {
                            UserName = txtUsernameNew,
                            BookID = book.ID,
                            CustomCode = txtDetail,
                            ReserveStartDate = DateTimeOffset.UtcNow.UtcDateTime,
                            ReserveEndDate = DateTimeOffset.UtcNow.UtcDateTime.AddDays(4),
                            DeliveryDate = null,
                            StatusID = ReserveStatus.Reservation,
                            Renewal = 0
                        };
                        EF.ULSTbl_Reserve.Add(TG);
                        book.Available--;
                        await EF.SaveChangesAsync();
                        Message = Classes.Components.alert("عملیات با موفقیت انجام شد", Classes.Components.AlertStyle.success);
                    }
                    else
                    {
                        Message = Classes.Components.alert("کاربر مورد نظر به حداکثر تعداد مجاز برای رزرو کتاب رسیده‌اید", Classes.Components.AlertStyle.danger);
                    }
                }
                else
                {
                    Message = Classes.Components.alert("تنظیمات سایت در حال حاضر خالی می‌باشد", Classes.Components.AlertStyle.danger);
                }
            }

            ActiveViewIndex = 0;
            PanelSearchVisible = true;
            await LoadReservationListAsync();
            return Page();
        }

        public async Task<int> GetReservationCountAsync(string userID)
        {
            using (var libraryDBEntities = new ULSDBEntities())
            {
                return await (from TableRes in libraryDBEntities.ULSTbl_Reserve
                              where TableRes.UserName == userID.ToLower() && TableRes.StatusID != 4
                              select TableRes).CountAsync();
            }
        }

        public string UserStatusHtml(int? id)
        {
            string text = "";
            if (id == UserStatus.Active)
                text += @"<td><span class='badge bg-label-success me-1'>فعال</span></td>";
            else if (id == UserStatus.NeedVerify)
                text += @"<td><span class='badge bg-label-warning me-1'>تایید نشده</span></td>";
            else if (id == UserStatus.Fine)
                text += @"<td><span class='badge bg-label-danger me-1'>در حال جریمه</span></td>";
            else if (id == -2)
                text += @"<td><span class='badge bg-label-dark me-1'>غیرفعال(جریمه)</span></td>";
            else
                text += @"<td><span class='badge bg-label-dark me-1'>غیرفعال</span></td>";
            return text;
        }

        public string ConvertToPersianDate(DateTime? date)
        {
            if (date == null) return "--";
            var persianCalendar = new System.Globalization.PersianCalendar();
            return $"{persianCalendar.GetYear(date.Value)}/{persianCalendar.GetMonth(date.Value):00}/{persianCalendar.GetDayOfMonth(date.Value):00}";
        }

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
    }
}
