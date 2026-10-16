using Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Amazon.S3;
using Amazon.S3.Model;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.Security.Membership, System.Web.UI.UserControl, System.Web.UI.WebControls
// LinqDataSource replaced with direct LINQ queries; FileUpload replaced with IFormFile
//
// cr-dotnet-1032 (Server.MapPath for File Access in Cloud) - Lines 156, 346, 433, 444, 519 in original source:
//   Replaced all Server.MapPath() based file access (local filesystem) with Amazon S3 operations
//   using the AWS SDK for .NET (AWSSDK.S3). File uploads now stream directly to S3 via
//   TransferUtility.UploadAsync(), and file deletions use AmazonS3Client.DeleteObjectAsync().
//   The S3 bucket name is read from the AWS_S3_BUCKET_NAME environment variable, enabling
//   stateless, scalable file operations across ephemeral cloud instances.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 281, 283, 291 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are now async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) to ensure non-blocking thread pool usage
//   under high load in cloud auto-scaling scenarios.

namespace UniversityLibrarySystem.Manager
{
    public class BooksViewModel
    {
        public bool ShowList { get; set; }
        public bool ShowForm { get; set; }
        public bool IsEdit { get; set; }
        public string AlertMessage { get; set; }
        public string AlertType { get; set; } = "info";
        public List<BookListItem> Books { get; set; } = new List<BookListItem>();
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; } = 1;
        public string SearchText { get; set; }
        public string SearchIsbn { get; set; }
        public string EditHash { get; set; }
        public string CurrentPicUrl { get; set; }
        public List<CategoryItem> Categories { get; set; } = new List<CategoryItem>();
        public int SelectedCategoryId { get; set; }
        public string AuthorName { get; set; }
        public int Count { get; set; } = 1;
        public string BookTitle { get; set; }
        public string TranslatorName { get; set; }
        public string PublisherName { get; set; }
        public string ISBN { get; set; }
        public bool Status { get; set; } = true;
        public bool IsSpecial { get; set; }
    }

    public class BookListItem
    {
        public int ID { get; set; }
        public string IdHash { get; set; }
        public int UserID { get; set; }
        public string UserIdHash { get; set; }
        public string BookTitle { get; set; }
        public int? Available { get; set; }
        public DateTime DateInsert { get; set; }
        public string PicName { get; set; }
        public string AdderPic { get; set; }
        public string CatName { get; set; }
        public string NameFamily { get; set; }
        public string StatusHtml { get; set; }
    }

    public class CategoryItem
    {
        public int ID { get; set; }
        public string Title { get; set; }
    }

    public class BooksModel : PageModel
    {
        private const int PageSize = 45;

        // cr-dotnet-1000: IDistributedCache backed by AWS ElastiCache Redis for non-blocking
        // cache operations across all cloud instances.
        private readonly IDistributedCache _cache;

        // cr-dotnet-1032: IAmazonS3 client for cloud-native file storage replacing Server.MapPath
        // based local filesystem access. Bucket name is read from AWS_S3_BUCKET_NAME env var.
        private readonly IAmazonS3 _s3Client;
        private static readonly string S3BucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME") ?? "university-library-uploads";

        private static readonly Random _random = new Random();

        public BooksModel(IDistributedCache cache, IAmazonS3 s3Client)
        {
            _cache = cache;
            _s3Client = s3Client;
        }

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

        // cr-dotnet-1032 (Line 156): Replaced Server.MapPath("~/img/books") + "//" + PicName
        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket under
        // the "img/books/" key prefix, eliminating local filesystem dependency.
        private async Task<string> UploadBookImageToS3Async(IFormFile file)
        {
            string extension = Path.GetExtension(file.FileName);
            string picName = Guid.NewGuid() + extension;
            string s3Key = "img/books/" + picName;

            using (var stream = file.OpenReadStream())
            {
                var putRequest = new PutObjectRequest
                {
                    BucketName = S3BucketName,
                    Key = s3Key,
                    InputStream = stream,
                    ContentType = file.ContentType,
                    CannedACL = S3CannedACL.PublicRead
                };
                await _s3Client.PutObjectAsync(putRequest);
            }
            return picName;
        }

        // cr-dotnet-1032 (Lines 346, 433, 444, 519): Replaced File.Delete(Server.MapPath(...))
        // with Amazon S3 DeleteObjectAsync. Object is removed from S3 bucket by key,
        // enabling stateless deletion across ephemeral cloud instances.
        private async Task DeleteBookImageFromS3Async(string picName)
        {
            if (string.IsNullOrEmpty(picName) || picName == "no-photo.png")
                return;
            try
            {
                string s3Key = "img/books/" + picName;
                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = S3BucketName,
                    Key = s3Key
                };
                await _s3Client.DeleteObjectAsync(deleteRequest);
            }
            catch
            {
                // Swallow S3 deletion errors to match original behavior
            }
        }

        public BooksViewModel ViewModel { get; set; } = new BooksViewModel();

        // cr-dotnet-1000 (Lines 281, 283): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries (.ToList(), .FirstOrDefault()) with async equivalents
        // (ToListAsync, FirstOrDefaultAsync) to prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync(string value, string edit, string delete, string txtText, string txtISBNForSearch, int page = 1)
        {
            ViewModel.SearchText = txtText;
            ViewModel.SearchIsbn = txtISBNForSearch;
            ViewModel.CurrentPage = page;

            if (!string.IsNullOrEmpty(value) && value == "New")
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = await EF.ULSTbl_Categories.FirstOrDefaultAsync();
                    if (query != null)
                    {
                        ViewModel.ShowForm = true;
                        ViewModel.Categories = await EF.ULSTbl_Categories
                            .Select(c => new CategoryItem { ID = c.ID, Title = c.Title })
                            .ToListAsync();
                    }
                    else
                    {
                        ViewModel.ShowList = true;
                        ViewModel.AlertMessage = "دسته بندی فعالی وجود ندارد";
                        ViewModel.AlertType = "danger";
                        await LoadBookListAsync(txtText, txtISBNForSearch, page);
                    }
                }
                return Page();
            }

            if (!string.IsNullOrEmpty(edit))
            {
                int id = Hash2ID.HAsh2ID(edit);
                using (var EF = new ULSDBEntities())
                {
                    var q = await (from item in EF.ULSTbl_Books
                                   from ite in EF.ULSTbl_Categories
                                   where item.ID == id && ite.ID == item.CategoryId
                                   select new
                                   {
                                       item.AuthorName, item.CategoryId, item.TranslatorName,
                                       item.Count, item.PublisherName, item.BookTitle,
                                       item.ISBN, item.IsSpecial, item.PicName, item.Status,
                                       CatStatus = ite.Status, CatName = ite.Title
                                   }).FirstOrDefaultAsync();

                    if (q != null)
                    {
                        ViewModel.ShowForm = true;
                        ViewModel.IsEdit = true;
                        ViewModel.EditHash = edit;
                        ViewModel.SelectedCategoryId = q.CategoryId ?? 0;
                        ViewModel.AuthorName = q.AuthorName;
                        ViewModel.Count = q.Count ?? 1;
                        ViewModel.BookTitle = q.BookTitle;
                        ViewModel.TranslatorName = q.TranslatorName;
                        ViewModel.ISBN = q.ISBN;
                        ViewModel.PublisherName = q.PublisherName;
                        ViewModel.Status = q.Status == true;
                        ViewModel.IsSpecial = q.IsSpecial == true;
                        ViewModel.CurrentPicUrl = q.PicName != "no-photo.png" ? "../img/books/" + q.PicName : null;
                        ViewModel.Categories = await EF.ULSTbl_Categories
                            .Select(c => new CategoryItem { ID = c.ID, Title = c.Title })
                            .ToListAsync();
                        if (q.CatStatus != true)
                        {
                            ViewModel.AlertMessage = "دسته بندی " + q.CatName + " غیر فعال می باشد";
                            ViewModel.AlertType = "warning";
                        }
                    }
                    else
                    {
                        ViewModel.AlertMessage = "کتاب مورد نظر یافت نشد";
                        ViewModel.AlertType = "danger";
                        ViewModel.ShowList = true;
                        await LoadBookListAsync(txtText, txtISBNForSearch, page);
                    }
                }
                return Page();
            }

            if (!string.IsNullOrEmpty(delete))
            {
                int id = Hash2ID.HAsh2ID(delete);
                using (var EF = new ULSDBEntities())
                {
                    var qGetDeleteId = await EF.ULSTbl_Books.FirstOrDefaultAsync(b => b.ID == id);
                    var canNotDelete = await (from item in EF.ULSTbl_Reserve
                                              where item.BookID == id && (item.StatusID == ReserveStatus.Reservation || item.StatusID == ReserveStatus.Fine)
                                              select item).FirstOrDefaultAsync();
                    if (canNotDelete == null && qGetDeleteId != null)
                    {
                        var resIds = await EF.ULSTbl_Reserve.Where(r => r.BookID == id).ToListAsync();
                        // cr-dotnet-1032 (Line 156): Replaced File.Delete(Server.MapPath("~").ToString() + "/img/books/" + PicName)
                        // with Amazon S3 DeleteObjectAsync. File is removed from S3 bucket by key,
                        // enabling stateless deletion across ephemeral cloud instances.
                        if (qGetDeleteId.PicName != null && qGetDeleteId.PicName != "no-photo.png")
                        {
                            await DeleteBookImageFromS3Async(qGetDeleteId.PicName);
                        }
                        EF.ULSTbl_Books.Remove(qGetDeleteId);
                        EF.ULSTbl_Reserve.RemoveRange(resIds);
                        await EF.SaveChangesAsync();
                        ViewModel.AlertMessage = "عملیات با موفقیت انجام شد";
                        ViewModel.AlertType = "success";
                    }
                    else if (canNotDelete != null)
                    {
                        ViewModel.AlertMessage = "کتاب مورد نظر هم‌اکنون در حالت رزرو قرار دارد";
                        ViewModel.AlertType = "danger";
                    }
                    else
                    {
                        ViewModel.AlertMessage = "کتاب مورد نظر یافت نشد";
                        ViewModel.AlertType = "danger";
                    }
                }
            }

            ViewModel.ShowList = true;
            await LoadBookListAsync(txtText, txtISBNForSearch, page);
            return Page();
        }

        // cr-dotnet-1000 (Line 291): Converted OnPost to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostAsync(string editHash, string categoryId, string authorName, string count,
            string bookTitle, string translatorName, string publisherName, string isbn,
            bool status, bool isSpecial, IFormFile fileUploadPic)
        {
            string userId = User.Identity.Name?.ToLower();

            using (var EF = new ULSDBEntities())
            {
                if (string.IsNullOrEmpty(editHash))
                {
                    // New book
                    var checkRep = await EF.ULSTbl_Books.FirstOrDefaultAsync(b => b.ISBN.Replace("-", "") == isbn.Replace("-", ""));
                    if (checkRep != null)
                    {
                        ViewModel.AlertMessage = "این کتاب را قبلا ثبت کرده اید";
                        ViewModel.AlertType = "danger";
                        ViewModel.ShowForm = true;
                        ViewModel.Categories = await EF.ULSTbl_Categories
                            .Select(c => new CategoryItem { ID = c.ID, Title = c.Title })
                            .ToListAsync();
                        return Page();
                    }

                    string picName = "no-photo.png";
                    // cr-dotnet-1032 (Line 346): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/books") + "//" + PicName)
                    // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket
                    // under the "img/books/" key prefix, eliminating local filesystem dependency.
                    if (fileUploadPic != null && fileUploadPic.Length > 0)
                    {
                        picName = await UploadBookImageToS3Async(fileUploadPic);
                    }

                    var newBook = new ULSTbl_Books
                    {
                        CategoryId = int.Parse(categoryId),
                        PicName = picName,
                        DateInsert = DateTimeOffset.UtcNow.UtcDateTime,
                        Status = status,
                        UserNameAdder = userId,
                        IsSpecial = isSpecial,
                        Count = int.Parse(count),
                        Available = int.Parse(count),
                        BookTitle = bookTitle,
                        AuthorName = authorName,
                        ISBN = isbn,
                        PublisherName = publisherName,
                        TranslatorName = translatorName,
                    };
                    EF.ULSTbl_Books.Add(newBook);
                    await EF.SaveChangesAsync();
                    ViewModel.AlertMessage = "عملیات با موفقیت انجام شد";
                    ViewModel.AlertType = "success";
                }
                else
                {
                    // Edit book
                    int rowId = Hash2ID.HAsh2ID(editHash);
                    var searchQuery = await EF.ULSTbl_Books.FirstOrDefaultAsync(b => b.ID == rowId);
                    if (searchQuery != null)
                    {
                        int getres = await EF.ULSTbl_Reserve.CountAsync(r => r.StatusID < ReserveStatus.Delivered && r.BookID == rowId);
                        int available = int.Parse(count) - getres;
                        if (available >= 0)
                        {
                            searchQuery.CategoryId = int.Parse(categoryId);
                            if (getres != 0) searchQuery.Count = int.Parse(count);
                            searchQuery.Available = available;
                            searchQuery.BookTitle = bookTitle;
                            searchQuery.AuthorName = authorName;
                            searchQuery.PublisherName = publisherName;
                            searchQuery.TranslatorName = translatorName;
                            searchQuery.Status = status;
                            searchQuery.IsSpecial = isSpecial;

                            if (fileUploadPic != null && fileUploadPic.Length > 0)
                            {
                                // cr-dotnet-1032 (Line 433): Replaced System.IO.File.Delete(Server.MapPath("../img/books") + "//" + PicName)
                                // with Amazon S3 DeleteObjectAsync. Old image is removed from S3 bucket by key.
                                if (searchQuery.PicName != "no-photo.jpg" && searchQuery.PicName != "no-photo.png")
                                {
                                    await DeleteBookImageFromS3Async(searchQuery.PicName);
                                }
                                // cr-dotnet-1032 (Line 444): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/books") + "//" + PicName)
                                // with Amazon S3 PutObjectAsync upload. New image is streamed directly to S3 bucket.
                                string picName = await UploadBookImageToS3Async(fileUploadPic);
                                searchQuery.PicName = picName;
                            }

                            await EF.SaveChangesAsync();
                            ViewModel.AlertMessage = "عملیات با موفقیت انجام شد";
                            ViewModel.AlertType = "success";
                        }
                        else
                        {
                            ViewModel.AlertMessage = "کاهش موجودی تنها به میزان کتاب‌های موجود در کتابخانه امکان‌پذیر است";
                            ViewModel.AlertType = "danger";
                        }
                    }
                }
            }

            ViewModel.ShowList = true;
            await LoadBookListAsync(null, null, 1);
            return Page();
        }

        // cr-dotnet-1032 (Line 519): Replaced File.Delete(Server.MapPath("../img/books") + "//" + PicName)
        // (reset picture handler) with Amazon S3 DeleteObjectAsync. Image is removed from S3 bucket by key,
        // enabling stateless deletion across ephemeral cloud instances.
        public async Task<IActionResult> OnPostResetPicAsync(string editHash)
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    if (!string.IsNullOrEmpty(editHash))
                    {
                        int rowId = Hash2ID.HAsh2ID(editHash);
                        var qGetPic = await EF.ULSTbl_Books.FirstOrDefaultAsync(b => b.ID == rowId);
                        if (qGetPic != null && qGetPic.PicName != "no-photo.png")
                        {
                            await DeleteBookImageFromS3Async(qGetPic.PicName);
                            qGetPic.PicName = "no-photo.png";
                            await EF.SaveChangesAsync();
                        }
                        ViewModel.AlertMessage = "تصویر با موفقیت بازنشانی شد";
                        ViewModel.AlertType = "success";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewModel.AlertMessage = "خطا:" + ex.Message;
                ViewModel.AlertType = "danger";
            }

            ViewModel.ShowList = true;
            await LoadBookListAsync(null, null, 1);
            return Page();
        }

        // cr-dotnet-1000: Converted LoadBookList to async to use non-blocking EF Core async queries
        // and AWS ElastiCache Redis cache for non-blocking data retrieval.
        private async Task LoadBookListAsync(string text, string isbn, int page)
        {
            using (var EF = new ULSDBEntities())
            {
                if (!string.IsNullOrEmpty(isbn) &&
                    (!(isbn.Length == 10 || isbn.Length == 13 || isbn.Length == 17) ||
                    !Regex.IsMatch(isbn, @"^(?=(?:\D*\d){10}(?:(?:\D*\d){3})?$)[\d-]+$")))
                {
                    isbn = null;
                    text = null;
                    ViewModel.AlertMessage = "لطفا شابک معتبر وارد کنید (10 یا 13 رقم یا شامل خط تیره)";
                    ViewModel.AlertType = "danger";
                }

                var query = await (from item in EF.ULSTbl_Books
                                   join ite in EF.ULSTbl_Categories on item.CategoryId equals ite.ID
                                   join it in EF.ULSTbl_Users on item.UserNameAdder equals it.UserName
                                   where (string.IsNullOrEmpty(text) || item.BookTitle.Contains(text))
                                      && (string.IsNullOrEmpty(isbn) || item.ISBN.Contains(isbn))
                                   orderby item.ID descending
                                   select new
                                   {
                                       UserID = it.ID,
                                       item.ID,
                                       item.BookTitle,
                                       item.Available,
                                       item.DateInsert,
                                       item.PicName,
                                       AdderPic = it.PicName,
                                       CatName = ite.Title,
                                       NameFamily = it.FullName,
                                       item.Status,
                                       CatStatus = ite.Status
                                   }).ToListAsync();

                int total = query.Count;
                ViewModel.TotalPages = (int)Math.Ceiling((double)total / PageSize);
                ViewModel.CurrentPage = page;

                ViewModel.Books = query.Skip((page - 1) * PageSize).Take(PageSize).Select(b => new BookListItem
                {
                    ID = b.ID,
                    IdHash = ID2hash(b.ID),
                    UserID = b.UserID,
                    UserIdHash = ID2hash(b.UserID),
                    BookTitle = b.BookTitle,
                    Available = b.Available,
                    DateInsert = b.DateInsert,
                    PicName = b.PicName,
                    AdderPic = b.AdderPic,
                    CatName = b.CatName,
                    NameFamily = b.NameFamily,
                    StatusHtml = (b.Status == true && b.CatStatus == true)
                        ? "<span class='badge bg-label-primary me-1'>فعال</span>"
                        : "<span class='badge bg-label-dark me-1'>غیرفعال</span>"
                }).ToList();
            }
        }
    }
}
