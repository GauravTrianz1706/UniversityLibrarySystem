// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI, System.Web.UI.WebControls, System.Web.UI.UserControl
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PAP
{
    public class NotFoundPageModel : PageModel
    {
        public IActionResult OnGet()
        {
            return Page();
        }
    }
}
