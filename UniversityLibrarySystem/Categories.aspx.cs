using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

namespace UniversityLibrarySystem
{
    public class CategoriesModel : PageModel
    {
        private int CatID = 0;
        private const int NumCard = 12;

        public string LinksTitle { get; set; } = "";
        public string CategoriesLinksHtml { get; set; } = "";
        public string BooksHtml { get; set; } = "";
        public string SearchLabel { get; set; } = "";
        public bool ShowNextPage { get; set; } = false;
        public int CurrentPage { get; set; } = 1;

        public IActionResult OnGet(string categoryID, int page = 1)
        {
            CurrentPage = page;

            if (!string.IsNullOrEmpty(categoryID))
            {
                try
                {
                    CatID = int.Parse(categoryID);
                    using (var EF = new ULSDBEntities())
                    {
                        var query = (from Table in EF.ULSTbl_Categories
                                     where Table.ID == CatID && Table.Status == true
                                     select Table).FirstOrDefault();
                        if (query != null)
                        {
                            ViewData["Title"] = SiteConfig.mixTitle("دسته بندی");
                        }
                        else
                        {
                            Response.StatusCode = 404;
                        }
                    }
                }
                catch
                {
                    CatID = 0;
                    return Redirect("/categories");
                }
            }
            else
            {
                CatID = 0;
                ViewData["Title"] = SiteConfig.mixTitle("دسته بندی");
            }

            LinksTitle = GetLinksTitle();
            CategoriesLinksHtml = GetLinksCategories();
            BooksHtml = GetBook(page);

            return Page();
        }

        private string GetLinksTitle()
        {
            if (CatID != 0)
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from Table in EF.ULSTbl_Categories
                                 where Table.ID == CatID && Table.Status == true
                                 select Table).FirstOrDefault();
                    if (query != null)
                        return @"<li>
                        <a href=""/Categories"" class=""breadcrumb-link"">دسته بندی</a>
                    </li>
                    <li class=""chevron""><span class=""fa fa-chevron-left""></span></li>
                    <li>
                        <span class=""breadcrumb-active"">" + query.Title + @"</span>
                    </li>";
                }
            }
            return @"<li>
                        <span class='breadcrumb-active'>دسته بندی</span>
                    </li>";
        }

        private string GetLinksCategories()
        {
            string html = "";
            var categories = SiteConfig.GetCategories();

            if (categories != null && categories.Any())
            {
                foreach (var item in categories)
                {
                    html += @"
                <a href='/Categories-" + item.ID + @"-" + item.Title + @"' class='container'>
                    <svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='currentColor' class='bi bi-chevron-left' viewBox='0 0 16 16'>
                        <path fill-rule='evenodd' d='M11.354 1.646a.5.5 0 0 1 0 .708L5.707 8l5.647 5.646a.5.5 0 0 1-.708.708l-6-6a.5.5 0 0 1 0-.708l6-6a.5.5 0 0 1 .708 0'/>
                    </svg>
                    " + item.Title + @"
                </a>";
                }
            }

            return html;
        }

        private string GetBook(int page)
        {
            string html = "";
            long lastID = long.MaxValue;

            if (page > 1)
            {
                // For pagination, calculate offset
                int skip = (page - 1) * NumCard;
                using (var EF = new ULSDBEntities())
                {
                    var allBooks = (from TableBook in EF.ULSTbl_Books
                                    join TableCategory in EF.ULSTbl_Categories
                                    on TableBook.CategoryId equals TableCategory.ID
                                    where TableBook.Status == true && TableCategory.Status == true && (CatID == 0 || TableBook.CategoryId == CatID)
                                    orderby TableBook.ID descending
                                    select TableBook).Skip(skip).Take(NumCard + 1).ToList();

                    ShowNextPage = allBooks.Count == NumCard + 1;
                    if (ShowNextPage) allBooks.RemoveAt(NumCard);

                    foreach (var tableBook in allBooks)
                    {
                        bool isSpecial = tableBook.IsSpecial == true;
                        int Available = -1;
                        if (tableBook.Available != null)
                        {
                            Available = tableBook.Available == 0 ? 0 : 1;
                        }
                        html += @"<div class=""col-xl-3 col-lg-4 col-md-4 col-6"">
                            <div class='properties pb-30'>";
                        html += Components.bookCard(tableBook.ID, tableBook.BookTitle, tableBook.PicName, tableBook.AuthorName, tableBook.PublisherName, Available, isSpecial);
                        html += @"</div>
                        </div>";
                    }
                }
            }
            else
            {
                using (var EF = new ULSDBEntities())
                {
                    var queryNewBook = (from TableBook in EF.ULSTbl_Books
                                        join TableCategory in EF.ULSTbl_Categories
                                        on TableBook.CategoryId equals TableCategory.ID
                                        where lastID >= TableBook.ID && TableBook.Status == true && TableCategory.Status == true && (CatID == 0 || TableBook.CategoryId == CatID)
                                        orderby TableBook.ID descending
                                        select TableBook).Take(NumCard + 1).ToList();

                    ShowNextPage = queryNewBook.Count == NumCard + 1;
                    if (ShowNextPage) queryNewBook.RemoveAt(NumCard);

                    foreach (var tableBook in queryNewBook)
                    {
                        bool isSpecial = tableBook.IsSpecial == true;
                        int Available = -1;
                        if (tableBook.Available != null)
                        {
                            Available = tableBook.Available == 0 ? 0 : 1;
                        }
                        html += @"<div class=""col-xl-3 col-lg-4 col-md-4 col-6"">
                            <div class='properties pb-30'>";
                        html += Components.bookCard(tableBook.ID, tableBook.BookTitle, tableBook.PicName, tableBook.AuthorName, tableBook.PublisherName, Available, isSpecial);
                        html += @"</div>
                        </div>";
                    }
                }
            }

            return html;
        }
    }
}
