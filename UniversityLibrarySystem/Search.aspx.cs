// Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI, System.Web.UI.WebControls
// Replaced: System.Web.UI.Page with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, NextPage_Click with OnGetNextPage
//
// cr-dotnet-1018: XSS Prevention - Request.Query values sanitized via XssSanitizer (Ganss.Xss)
//   Lines 44-49, 58-59, 61: All Request.Query values (BookName, Author, Translator, Publisher,
//   ISBN, Category, isSpecial, isAvailable, lastId) are sanitized with XssSanitizer.SanitizeText()
//   before use to prevent XSS injection. Sanitizer configuration (allowed tags/attributes) is
//   stored in AWS Secrets Manager under XSS_SANITIZER_CONFIG_SECRET_NAME for centralized
//   management across multi-tenant cloud environments.
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UniversityLibrarySystem.Classes;

namespace UniversityLibrarySystem
{
    public class SearchModel : PageModel
    {
        int numCards = 12;
        string bookName;
        string author;
        string translator;
        string publisher;
        string isbn;
        int category = 0;
        bool isSpecial;
        bool isAvailable;
        // LSI Config
        bool sortZA = true; // descending

        public string BooksHtml { get; set; } = string.Empty;
        public string ErrorText { get; set; } = string.Empty;
        public string SearchLabel { get; set; } = string.Empty;
        public string LastBookId { get; set; } = string.Empty;
        public bool ShowNextPage { get; set; }

        public IActionResult OnGet()
        {
            try
            {
                bookName = string.Empty;
                author = string.Empty;
                translator = string.Empty;
                publisher = string.Empty;
                isbn = string.Empty;
                // cr-dotnet-1018 (Lines 44-49): Sanitize all user-supplied query parameters using
                // XssSanitizer.SanitizeText() before use to prevent XSS injection.
                // Sanitizer config loaded from AWS Secrets Manager (XSS_SANITIZER_CONFIG_SECRET_NAME)
                // for centralized management across multi-tenant cloud environments.
                bookName = XssSanitizer.SanitizeText(Request.Query["BookName"].FirstOrDefault() ?? string.Empty);
                author = XssSanitizer.SanitizeText(Request.Query["Author"].FirstOrDefault() ?? string.Empty);
                translator = XssSanitizer.SanitizeText(Request.Query["Translator"].FirstOrDefault() ?? string.Empty);
                publisher = XssSanitizer.SanitizeText(Request.Query["Publisher"].FirstOrDefault() ?? string.Empty);
                isbn = XssSanitizer.SanitizeText(Request.Query["ISBN"].FirstOrDefault() ?? string.Empty);
                // cr-dotnet-1018 (Line 49): Sanitize Category query parameter before parsing.
                string categoryStr = XssSanitizer.SanitizeText(Request.Query["Category"].FirstOrDefault());
                if (!string.IsNullOrEmpty(categoryStr))
                {
                    int.TryParse(categoryStr, out category);
                }
                else
                {
                    category = 0;
                }
                // cr-dotnet-1018 (Lines 58-59): Sanitize isSpecial and isAvailable query parameters
                // before use to prevent XSS injection via query string manipulation.
                isSpecial = XssSanitizer.SanitizeText(Request.Query["isSpecial"].FirstOrDefault()) == "on";
                isAvailable = XssSanitizer.SanitizeText(Request.Query["isAvailable"].FirstOrDefault()) == "on";

                // cr-dotnet-1018 (Line 61): Sanitize lastId query parameter before use.
                string lastIdStr = XssSanitizer.SanitizeText(Request.Query["lastId"].FirstOrDefault());
                if (string.IsNullOrEmpty(lastIdStr))
                {
                    BooksHtml = NewBook();
                }
                else
                {
                    LastBookId = lastIdStr;
                    BooksHtml = NewBook();
                }
            }
            catch (Exception ex)
            {
                ErrorText = "یک خطای غیرمنتظره رخ داده است: " + ex.Message;
            }

            if (!RegExVars())
            {
                ErrorText = "خطا:مقادیر به درستی وارد نشده اند";
            }

            ViewData["Title"] = SiteConfig.mixTitle("جستو جو " + bookName);
            return Page();
        }

        public bool RegExVars()
        {
            if (!string.IsNullOrEmpty(bookName) && !Regex.IsMatch(bookName, @"^.{2,100}$"))
                return false;
            if (!string.IsNullOrEmpty(author) && !Regex.IsMatch(author, @"^.{2,50}$"))
                return false;
            if (!string.IsNullOrEmpty(translator) && !Regex.IsMatch(translator, @"^.{2,50}$"))
                return false;
            if (!string.IsNullOrEmpty(publisher) && !Regex.IsMatch(publisher, @"^.{2,50}$"))
                return false;
            if (!string.IsNullOrEmpty(isbn) &&
                (!(isbn.Length == 10 || isbn.Length == 13 || isbn.Length == 17) ||
                !Regex.IsMatch(isbn, @"^(?=(?:\D*\d){10}(?:(?:\D*\d){3})?$)[\d-]+$")))
                return false;
            if (category < 0)
                return false;
            return true;
        }

        public string NewBook()
        {
            if (!RegExVars())
            {
                ShowNextPage = false;
                return string.Empty;
            }

            string html = string.Empty;
            long.TryParse(LastBookId, out var lsID);
            using (var EF = new ULSDBEntities())
            {
                var queryNewBook = (from TableBook in EF.ULSTbl_Books
                                    join TableCategory in EF.ULSTbl_Categories
                                    on TableBook.CategoryId equals TableCategory.ID
                                    where (lsID == 0 || ((sortZA && lsID > TableBook.ID) || lsID < TableBook.ID))
                                    && (isAvailable == false || TableBook.Available > 0)
                                    && TableBook.Status == true
                                    && TableCategory.Status == true
                                    && (string.IsNullOrEmpty(bookName) || TableBook.BookTitle.Contains(bookName))
                                    && (string.IsNullOrEmpty(author) || TableBook.AuthorName.Contains(author))
                                    && (string.IsNullOrEmpty(publisher) || TableBook.PublisherName.Contains(publisher))
                                    && (string.IsNullOrEmpty(isbn) || TableBook.ISBN.Contains(isbn))
                                    && (!isSpecial || TableBook.IsSpecial == isSpecial)
                                    && (category == 0 || TableBook.CategoryId == category)
                                    orderby TableBook.ID descending
                                    select TableBook).Take(numCards + 1).ToList();

                ShowNextPage = queryNewBook.Count == numCards + 1;
                if (ShowNextPage) queryNewBook.RemoveAt(numCards);
                LastBookId = queryNewBook.LastOrDefault()?.ID.ToString();
                foreach (var tableBook in queryNewBook)
                {
                    html += @"
                        <div class=""col-xl-3 col-lg-4 col-md-4 col-6"">
                            <div class='properties pb-30'>";
                    bool isSpec = false;
                    if (tableBook.IsSpecial != null)
                        if (tableBook.IsSpecial == true)
                            isSpec = true;
                    int Available = -1;
                    if (tableBook.Available != null)
                        Available = tableBook.Available == 0 ? 0 : 1;
                    html += Components.bookCard(tableBook.ID, tableBook.BookTitle, tableBook.PicName, tableBook.AuthorName, tableBook.PublisherName, Available, isSpec);
                    html += @"                            </div>
                        </div>
";
                }
            }
            return html;
        }

        public string GetCategories()
        {
            string html = string.Empty;
            var categories = SiteConfig.GetCategories();
            if (categories != null && categories.Any())
            {
                foreach (var item in categories)
                    html += "<option value='" + item.ID + "'>" + item.Title + "</option>";
            }
            return html;
        }
    }
}
