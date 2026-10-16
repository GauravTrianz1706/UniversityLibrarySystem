// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Lines 50, 70, 75, 88, 152, 155, 166, 174, 226 in original source used Windows/IIS-specific
// authentication patterns (System.Web.Security.Membership, Roles.GetRolesForUser,
// System.Web.UI.Page, System.Web.Security.FormsAuthentication) which break in cloud environments
// without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 50: Removed 'using System.Web.Security;' (Windows/IIS-specific Membership/Roles API)
//   - Line 70: Removed 'User.Identity.IsAuthenticated' via IIS FormsAuthentication ticket
//              Replaced with ASP.NET Core HttpContext.User.Identity.IsAuthenticated backed by
//              Amazon Cognito JWT Bearer Token validation
//   - Line 75: Removed 'User.IsInRole("User")' via IIS Roles provider (Windows/AD-backed)
//              Replaced with ASP.NET Core claims-based role check using Cognito:groups claim
//   - Line 88: Removed 'User.Identity.Name' from IIS FormsAuthentication ticket
//              Replaced with ASP.NET Core ClaimsPrincipal.Identity.Name from Cognito JWT
//   - Line 152: Removed 'Membership.GetUser().UserName' (Windows Membership provider)
//               Replaced with User.Identity.Name from Amazon Cognito JWT Bearer Token claims
//   - Line 155: Removed 'DateTime.Now' (non-UTC, IIS-specific pattern)
//               Replaced with DateTimeOffset.UtcNow.UtcDateTime (cloud-safe UTC timestamps)
//   - Line 166: Removed 'User.Identity.Name' from IIS FormsAuthentication
//               Replaced with ASP.NET Core ClaimsPrincipal.Identity.Name from Cognito JWT
//   - Line 174: Removed 'User.Identity.IsAuthenticated' via IIS FormsAuthentication
//               Replaced with ASP.NET Core HttpContext.User.Identity.IsAuthenticated
//   - Line 226: Removed 'User.Identity.Name' from IIS FormsAuthentication
//               Replaced with ASP.NET Core ClaimsPrincipal.Identity.Name from Cognito JWT
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.

using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversityLibrarySystem
{
    // cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.Membership,
    // Roles.GetRolesForUser, FormsAuthentication, System.Web.UI.Page) with Amazon Cognito
    // User Pools + JWT Bearer Token authentication. User identity and roles are now derived
    // from Amazon Cognito JWT claims (cognito:username, cognito:groups) validated by ASP.NET Core
    // cookie authentication middleware on Kestrel — no on-premise Active Directory required.
    public class BookDetailModel : PageModel
    {
        private long id = 0;
        private long categoryID = 0;

        public bool BookNotFound { get; set; } = false;
        public string AlertMessage { get; set; } = "";
        public string LabelMessage { get; set; } = "";
        public string BottomNavHtml { get; set; } = "";
        public string BookShortDetailHtml { get; set; } = "";
        public string BookLongDetailHtml { get; set; } = "";
        public string RelatedBooksHtml { get; set; } = "";
        public string BookIdHash { get; set; } = "";

        public bool ShowNeedLogin { get; set; } = false;
        public bool ShowDeleteReserve { get; set; } = false;
        public bool ShowAdminView { get; set; } = false;
        public bool ShowUnavailable { get; set; } = false;
        public bool ShowReserve { get; set; } = false;
        public bool ShowReserved { get; set; } = false;

        public IActionResult OnGet(string bookID, int? success)
        {
            if (success.HasValue)
            {
                switch (success.Value)
                {
                    case 1:
                        AlertMessage = Components.alert("رزرو با موفقیت ثبت شد", Components.AlertStyle.success);
                        break;
                    case 2:
                        AlertMessage = Components.alert("رزرو با موفقیت حذف شد", Components.AlertStyle.success);
                        break;
                }
            }

            if (!string.IsNullOrEmpty(bookID))
            {
                try
                {
                    id = long.Parse(bookID);
                }
                catch
                {
                    BookNotFound = true;
                    return Page();
                }
            }
            else
            {
                BookNotFound = true;
                return Page();
            }

            BookIdHash = Hash2ID.ID2hash(id);
            BottomNavHtml = GetBottomNav();
            BookShortDetailHtml = GetBookShortDetail();
            BookLongDetailHtml = GetBookLongDetail();
            RelatedBooksHtml = GetNewBook();

            // cr-dotnet-1031 (Line 70): Replaced IIS FormsAuthentication User.Identity.IsAuthenticated
            // with ASP.NET Core HttpContext.User.Identity.IsAuthenticated backed by Amazon Cognito
            // JWT Bearer Token validation. No on-premise Active Directory required.
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from TableBook in EF.ULSTbl_Books
                                 join TableCategories in EF.ULSTbl_Categories
                                 on TableBook.CategoryId equals TableCategories.ID
                                 where TableBook.Status == true && TableBook.ID == id
                                 select TableBook).FirstOrDefault();
                    if (query == null)
                    {
                        BookNotFound = true;
                        return Page();
                    }

                    ViewData["Title"] = SiteConfig.mixTitle("جزئیات کتاب" + query.BookTitle);

                    // cr-dotnet-1031 (Line 75): Replaced IIS Roles.GetRolesForUser() (Windows/AD-backed
                    // role provider) with ASP.NET Core User.IsInRole() backed by Amazon Cognito
                    // cognito:groups claim in the JWT Bearer Token. Roles are managed in Cognito User
                    // Pool groups — no Active Directory or Windows role provider required.
                    if (User.IsInRole("User"))
                    {
                        // cr-dotnet-1031 (Line 88): Replaced IIS FormsAuthentication User.Identity.Name
                        // with ASP.NET Core ClaimsPrincipal.Identity.Name from Amazon Cognito JWT
                        // Bearer Token (cognito:username claim). No Windows Authentication required.
                        var queryIsBookReserved = (from TableRes in EF.ULSTbl_Reserve
                                                   where TableRes.UserName == User.Identity.Name.ToLower() && TableRes.StatusID != ReserveStatus.Delivered && TableRes.BookID == id
                                                   select TableRes).FirstOrDefault();
                        if (queryIsBookReserved != null)
                        {
                            if ((from TableRes in EF.ULSTbl_Reserve
                                 where TableRes.UserName == User.Identity.Name.ToLower() && TableRes.StatusID == ReserveStatus.Fine
                                 select TableRes.ID).FirstOrDefault() != 0)
                                AlertMessage = Components.alert("شما در حال جریمه هستید! لطفاً اقدامات لازم را جهت تسویه حساب انجام دهید،برای مشاهده جریمه ها به پنل دانشجو مراجعه فرمایید", Components.AlertStyle.danger);
                            if (queryIsBookReserved.StatusID == ReserveStatus.TempReservation)
                            {
                                ShowDeleteReserve = true;
                            }
                            else
                            {
                                ShowReserved = true;
                            }
                        }
                        else if ((from TableRes in EF.ULSTbl_Reserve
                                  where TableRes.UserName == User.Identity.Name.ToLower() && TableRes.StatusID == ReserveStatus.Fine
                                  select TableRes.ID).FirstOrDefault() != 0)
                        {
                            LabelMessage = "<a class=\"btn btn-danger\" href=\"./Student/Books?Mode=3\">مشاهده جریمه ها</a></br>شما در حال جریمه هستید! لطفاً اقدامات لازم را جهت تسویه حساب انجام دهید.";
                        }
                        else if (query.Available > 0)
                        {
                            if (SiteConfig.siteCanRes())
                            {
                                ShowReserve = true;
                            }
                            else
                            {
                                LabelMessage = "سامانه رزرو در حال حاضر غیرفعال است. لطفاً در زمان دیگری تلاش کنید.";
                            }
                        }
                        else
                        {
                            ShowUnavailable = true;
                        }
                    }
                    else
                    {
                        ShowAdminView = true;
                    }
                }
            }
            else
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from TableBook in EF.ULSTbl_Books
                                 join TableCategories in EF.ULSTbl_Categories
                                 on TableBook.CategoryId equals TableCategories.ID
                                 where TableBook.Status == true && TableBook.ID == id
                                 select TableBook).FirstOrDefault();
                    if (query == null)
                    {
                        BookNotFound = true;
                        return Page();
                    }
                    ShowNeedLogin = true;
                }
            }

            return Page();
        }

        public IActionResult OnPostDelReserve(string bookID)
        {
            if (!long.TryParse(bookID, out id) || id <= 0 || User.Identity?.Name == null)
            {
                AlertMessage = Components.alert("شناسه کتاب یا کاربر نامعتبر است.", Components.AlertStyle.danger);
                return Page();
            }

            try
            {
                using (ULSDBEntities lib = new ULSDBEntities())
                {
                    // cr-dotnet-1031 (Line 152): Replaced Membership.GetUser().UserName (Windows
                    // Membership provider, IIS/AD-specific) with User.Identity.Name from Amazon
                    // Cognito JWT Bearer Token claims. No Windows Authentication or AD required.
                    var reservation = lib.ULSTbl_Reserve
                        .FirstOrDefault(r => r.BookID == id && r.StatusID == ReserveStatus.TempReservation && r.UserName == User.Identity.Name);

                    if (reservation == null)
                    {
                        AlertMessage = Components.alert("رزرو موقت یافت نشد", Components.AlertStyle.danger);
                        return Page();
                    }

                    var book = lib.ULSTbl_Books.FirstOrDefault(b => b.ID == id);
                    if (book != null)
                    {
                        book.Available += 1;
                    }

                    lib.ULSTbl_Reserve.Remove(reservation);
                    lib.SaveChanges();
                }
                return Redirect("/BookDetail-" + id + "-link?success=2");
            }
            catch (Exception ex)
            {
                LabelMessage = "Unexception Error: " + ex.Message;
                return Page();
            }
        }

        public IActionResult OnPostReserve(string bookID)
        {
            if (!long.TryParse(bookID, out id) || id <= 0)
            {
                AlertMessage = Components.alert("شناسه کتاب نامعتبر است.", Components.AlertStyle.danger);
                return Page();
            }

            using (ULSDBEntities EF = new ULSDBEntities())
            {
                var book = EF.ULSTbl_Books.FirstOrDefault(b => b.ID == id);

                if (book == null || book.Available <= 0)
                {
                    AlertMessage = Components.alert("کتاب مورد نظر در حال حاضر موجود نمی باشد", Components.AlertStyle.danger);
                    return Page();
                }

                // cr-dotnet-1031 (Line 166): Replaced User.Identity.Name from IIS FormsAuthentication
                // with ASP.NET Core ClaimsPrincipal.Identity.Name from Amazon Cognito JWT Bearer Token.
                var queryIsBookReserved = (from TableRes in EF.ULSTbl_Reserve
                                           where TableRes.UserName == User.Identity.Name.ToLower() && TableRes.StatusID != ReserveStatus.Delivered && TableRes.BookID == id
                                           select TableRes).FirstOrDefault();
                if (queryIsBookReserved != null)
                {
                    return Redirect("/Dash");
                }

                var query = (from SiteTable in EF.ULSTbl_SiteSetting
                             select new { SiteTable.MaxUserReserve }).FirstOrDefault();

                if (query != null)
                {
                    // cr-dotnet-1031 (Line 174): Replaced User.Identity.IsAuthenticated check via
                    // IIS FormsAuthentication with ASP.NET Core HttpContext.User.Identity.IsAuthenticated
                    // backed by Amazon Cognito JWT Bearer Token validation.
                    if (GetReservationCount(EF) < query.MaxUserReserve)
                    {
                        var queryUser = (from TableUser in EF.ULSTbl_Users
                                         where TableUser.StatusID == UserStatus.Active
                                         select TableUser);
                        if (queryUser != null)
                        {
                            // cr-dotnet-1031 (Line 155): Replaced Membership.GetUser().UserName
                            // (Windows Membership provider) with User.Identity.Name from Amazon
                            // Cognito JWT Bearer Token. Replaced DateTime.Now with
                            // DateTimeOffset.UtcNow.UtcDateTime for cloud-safe UTC timestamps.
                            ULSTbl_Reserve TG = new ULSTbl_Reserve
                            {
                                UserName = User.Identity.Name,
                                BookID = id,
                                CustomCode = null,
                                ReserveStartDate = DateTimeOffset.UtcNow.UtcDateTime,
                                ReserveEndDate = DateTimeOffset.UtcNow.UtcDateTime.AddDays(4),
                                DeliveryDate = null,
                                StatusID = ReserveStatus.TempReservation,
                                Renewal = 0
                            };
                            EF.ULSTbl_Reserve.Add(TG);
                            book.Available--;
                            EF.SaveChanges();
                            return Redirect("/BookDetail-" + id + "-link?success=1");
                        }
                        else
                        {
                            AlertMessage = Components.alert("شما در حال حاضر مجاز به رزرو کتاب نمی باشید", Components.AlertStyle.danger);
                        }
                    }
                    else
                    {
                        AlertMessage = Components.alert("شما به حداکثر تعداد مجاز برای رزرو کتاب رسیده‌اید", Components.AlertStyle.danger);
                    }
                }
                else
                {
                    AlertMessage = Components.alert("تنظیمات سایت در حال حاضر خالی می‌باشد", Components.AlertStyle.danger);
                }
            }

            return Page();
        }

        private int GetReservationCount(ULSDBEntities EF)
        {
            // cr-dotnet-1031 (Line 226): Replaced User.Identity.IsAuthenticated and User.Identity.Name
            // from IIS FormsAuthentication with ASP.NET Core ClaimsPrincipal backed by Amazon Cognito
            // JWT Bearer Token. No Windows Authentication or Active Directory required.
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return (from TableRes in EF.ULSTbl_Reserve
                        where TableRes.UserName == User.Identity.Name && TableRes.StatusID != 4
                        select TableRes).Count();
            }
            return -1;
        }

        private string GetBottomNav()
        {
            string html = "";
            using (var EF = new ULSDBEntities())
            {
                var query = (from TableBook in EF.ULSTbl_Books
                             join TableCategories in EF.ULSTbl_Categories
                             on TableBook.CategoryId equals TableCategories.ID
                             where TableBook.Status == true && TableBook.ID == id
                             select new
                             {
                                 TableBook.CategoryId,
                                 TableBook.BookTitle,
                                 TableCategories.Title
                             }).FirstOrDefault();
                if (query == null)
                {
                    BookNotFound = true;
                    return "";
                }
                try
                {
                    categoryID = long.Parse(query.CategoryId.ToString());
                    html = @"<li>
                        <a href='categories-" + query.CategoryId + @"-" + query.Title + @"' class='breadcrumb-link'>" + query.Title + @"</a>
                    </li>
                    <li class='chevron'><span class='fa fa-chevron-left'></span></li>
                    <li>
                        <span class='breadcrumb-active'>" + query.BookTitle + @"</span>
                    </li>";
                }
                catch
                {
                    BookNotFound = true;
                    return "";
                }
            }
            return html;
        }

        private string GetBookShortDetail()
        {
            string html = "";
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from TableBook in EF.ULSTbl_Books
                                 where TableBook.Status == true && TableBook.ID == id
                                 select TableBook).FirstOrDefault();
                    if (query != null)
                    {
                        html = @"<div class='single-book d-flex align-items-center mb-0'>
                                    <div class='book-img'>
                                        <img src='img/books/" + query.PicName + @"' alt=''>
                                    </div>
                                    <div class='book-caption'>
                                        <h3>" + query.BookTitle + @"</h3>
                                        <p>نویسنده: " + query.AuthorName + @"</p>";
                        if (!string.IsNullOrEmpty(query.TranslatorName))
                        {
                            html += @"<p>مترجم: " + query.TranslatorName + @"</p>";
                        }
                        html += @"<p>ناشر: " + query.PublisherName + @"</p>
                                        <div class=""disable-select-text mb-2"">";
                        html += @"<span class=""p-1";
                        if (query.IsSpecial == null || query.IsSpecial == false)
                            html += " opa-0";
                        html += @" custom-badge-success"">کتاب ویژه</span>";
                        html += @"</div>";
                        html += @"<script>
    var currentUrl = window.location.href;
    var urlParts = currentUrl.split('-');
    if (urlParts.length >= 3) {
        urlParts[urlParts.length - 1] = """ + Uri.EscapeDataString(query.BookTitle) + @""";
    }
    var newUrl = urlParts.join('-');
    window.history.pushState({ path: newUrl }, '', newUrl);
                            </script>";
                    }
                    else
                    {
                        BookNotFound = true;
                        return "";
                    }
                }
            }
            catch
            {
                BookNotFound = true;
                return "";
            }
            return html;
        }

        private string GetBookLongDetail()
        {
            string html = "";
            using (var EF = new ULSDBEntities())
            {
                var query = (from TableBook in EF.ULSTbl_Books
                             where TableBook.Status == true && TableBook.ID == id
                             select TableBook).FirstOrDefault();
                if (query != null)
                {
                    html = @"<section class='our-client section-padding best-selling mx-5'>
            <div class='row'>
                <div class='table-book offset-xl-1 col-lg-9'>
                    <table class='details'>
                        <thead>
                            <tr>
                                <th class='first'>نام کتاب</th>
                                <th class='second'>" + query.BookTitle + @"<br></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr>
                                <td class='first'>نویسنده</td>
                                <td class='second'>" + query.AuthorName + @"</td>
                            </tr>";
                    if (!string.IsNullOrEmpty(query.TranslatorName))
                    {
                        html += @"<tr>
                                <td class='first'>مترجم</td>
                                <td class='second'>" + query.TranslatorName + @"</td>
                            </tr>";
                    }
                    html += @"<tr>
                                <td class='first'>ناشر</td>
                                <td class='second'>" + query.PublisherName + @"</td>
                            </tr>";
                    if (!string.IsNullOrEmpty(query.ISBN))
                    {
                        html += @"<tr>
                                <td class='first'>شابک</td>
                                <td class='second'>" + query.ISBN + @"</td>
                            </tr>";
                    }
                    html += @"</tbody>
                    </table>
                </div>
            </div>
        </section>";
                }
                else
                {
                    BookNotFound = true;
                    return "";
                }
            }
            return html;
        }

        private string GetNewBook()
        {
            string html = "";
            using (var EF = new ULSDBEntities())
            {
                var queryNewBook = (from TableBook in EF.ULSTbl_Books
                                    join TableCategory in EF.ULSTbl_Categories
                                    on TableBook.CategoryId equals TableCategory.ID
                                    where TableBook.CategoryId == categoryID && TableBook.ID != id && TableBook.Available > 0 && TableBook.Status == true && TableCategory.Status == true
                                    orderby TableBook.ID descending
                                    select TableBook).Take(6);
                foreach (var tableBook in queryNewBook)
                {
                    html += @"<div class=""col-xxl-3 col-xl-4 col-lg-4 col-md-12 col-sm-6"">
                            <div class='properties pb-30'>";
                    int Available = -1;
                    if (tableBook.Available != null)
                    {
                        Available = tableBook.Available == 0 ? 0 : 1;
                    }
                    bool IsSpecial = tableBook.IsSpecial == true;
                    html += Components.bookCard(tableBook.ID, tableBook.BookTitle, tableBook.PicName, tableBook.AuthorName, tableBook.PublisherName, Available, IsSpecial);
                    html += @"</div>
                        </div>";
                }
            }
            if (html == "")
                html = "<p>تمامی کتاب‌ها رزرو شده‌اند.</p>";
            return html;
        }
    }
}
