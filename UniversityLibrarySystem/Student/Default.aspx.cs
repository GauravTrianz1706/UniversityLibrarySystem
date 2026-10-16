// Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.Security, System.Web.UI, System.Web.UI.WebControls
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
// Replaced: Membership API with ASP.NET Core Identity / HttpContext.User
// Replaced: Response.Redirect with RedirectToPage
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Linq;

namespace UniversityLibrarySystem.Student
{
    public class DefaultModel : PageModel
    {
        public string CurrentDate { get; set; } = string.Empty;
        public string ProfileHtml { get; set; } = string.Empty;
        public string FinanceAlert { get; set; } = string.Empty;
        public string PageContent { get; set; } = string.Empty;

        public IActionResult OnGet(string page = null)
        {
            if (!SiteConfig.siteStatus())
            {
                Response.StatusCode = 503;
                return StatusCode(503);
            }

            ViewData["Title"] = SiteConfig.mixTitle("پنل داشنجو");

            string username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Redirect("/");
            }

            CurrentDate = DateTimeOffset.UtcNow.UtcDateTime.ToLongDateString();
            ProfileHtml = BuildProfile(username);
            FinanceAlert = CheckFinance(username);

            if (!string.IsNullOrEmpty(page))
            {
                PageContent = $"<partial name='Student/{page}' />";
            }
            else
            {
                return RedirectToPage("./Books");
            }

            return Page();
        }

        private string BuildProfile(string username)
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    var qGetUser = (from item in EF.ULSTbl_Users
                                    where item.UserName == username
                                    select item).FirstOrDefault();

                    if (qGetUser == null) return string.Empty;

                    string text = @"<li class='nav-item navbar-dropdown dropdown-user dropdown'>
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

                    text += @"<small> دانشجو محترم </small>";
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
                return string.Empty;
            }
        }

        private string CheckFinance(string username)
        {
            try
            {
                using (ULSDBEntities EF = new ULSDBEntities())
                {
                    var query = (from res in EF.ULSTbl_Reserve
                                 where res.UserName == username && res.StatusID == ReserveStatus.Fine
                                 select res).Count();
                    if (query > 0)
                    {
                        return @"<div class=""alert alert-danger"" role=""alert"">
  شما در حال جریمه هستید! لطفاً اقدامات لازم را جهت تسویه حساب انجام دهید. <a id=""btnGoFine"" class=""btn btn-danger"" href=""./Books?Mode=3"">مشاهده جریمه ها</a>
</div>";
                    }
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
