using Classes;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace UniversityLibrarySystem
{
    public class _404Model : PageModel
    {
        public void OnGet()
        {
            ViewData["Title"] = SiteConfig.mixTitle("صفحه مورد نظر یافت نشد!");
        }
    }
}
