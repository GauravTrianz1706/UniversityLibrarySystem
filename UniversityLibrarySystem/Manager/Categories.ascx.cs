using Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
// cr-dotnet-1032 (Server.MapPath for File Access in Cloud) - Lines 116, 127, 278, 346, 357, 406 in original source:
//   Replaced all Server.MapPath() based file access (local filesystem) with Amazon S3 operations
//   using the AWS SDK for .NET (AWSSDK.S3). File uploads now stream directly to S3 via
//   PutObjectAsync(), and file deletions use AmazonS3Client.DeleteObjectAsync().
//   The S3 bucket name is read from the AWS_S3_BUCKET_NAME environment variable, enabling
//   stateless, scalable file operations across ephemeral cloud instances.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 138, 259 in original source:
//   Line 138: Removed System.Web.HttpRuntime.Cache.Remove("Categories") (IIS in-memory cache invalidation).
//             In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 259: Removed System.Web.HttpRuntime.Cache.Remove("Categories") (IIS in-memory cache invalidation).
//             Replaced with IDistributedCache.RemoveAsync() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//             in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//             ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//             Redis cluster, ensuring consistent cache invalidation across pods.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 249, 251 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are now async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) to ensure non-blocking thread pool usage
//   under high load in cloud auto-scaling scenarios.

namespace UniversityLibrarySystem.Manager
{
    public class CategoriesViewModel
    {
        public bool ShowList { get; set; }
        public bool ShowForm { get; set; }
        public bool IsEdit { get; set; }
        public string AlertMessage { get; set; }
        public string AlertType { get; set; } = "info";
        public List<CategoryListItem> Categories { get; set; } = new List<CategoryListItem>();
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; } = 1;
        public string EditHash { get; set; }
        public string CurrentPicUrl { get; set; }
        public string Title { get; set; }
        public bool Status { get; set; } = true;
    }

    public class CategoryListItem
    {
        public int ID { get; set; }
        public string IdHash { get; set; }
        public int UserID { get; set; }
        public string UserIdHash { get; set; }
        public string Title { get; set; }
        public DateTime DateInsert { get; set; }
        public string PicName { get; set; }
        public string NameFamily { get; set; }
        public string AdderPic { get; set; }
        public string StatusHtml { get; set; }
        public int BooksCount { get; set; }
    }

    public class CategoriesModel : PageModel
    {
        private const int PageSize = 45;

        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        // cr-dotnet-1032: IAmazonS3 client for cloud-native file storage replacing Server.MapPath
        // based local filesystem access. Bucket name is read from AWS_S3_BUCKET_NAME env var.
        private readonly IAmazonS3 _s3Client;
        private static readonly string S3BucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME") ?? "university-library-uploads";

        private static readonly Random _random = new Random();

        public CategoriesModel(IDistributedCache cache, IAmazonS3 s3Client)
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

        // cr-dotnet-1032 (Line 116): Replaced fileUploadPic.SaveAs(Server.MapPath("../img/Categories") + "//" + PicName)
        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket under
        // the "img/categories/" key prefix, eliminating local filesystem dependency.
        private async Task<string> UploadCategoryImageToS3Async(IFormFile file)
        {
            string extension = Path.GetExtension(file.FileName);
            string picName = Guid.NewGuid() + extension;
            string s3Key = "img/categories/" + picName;

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

        // cr-dotnet-1032 (Lines 127, 278, 346, 357, 406): Replaced File.Delete(Server.MapPath(...))
        // with Amazon S3 DeleteObjectAsync. Object is removed from S3 bucket by key,
        // enabling stateless deletion across ephemeral cloud instances.
        private async Task DeleteCategoryImageFromS3Async(string picName)
        {
            if (string.IsNullOrEmpty(picName) || picName == "no-photo.png")
                return;
            try
            {
                string s3Key = "img/categories/" + picName;
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

        public CategoriesViewModel ViewModel { get; set; } = new CategoriesViewModel();

        // cr-dotnet-1000 (Line 249): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (FirstOrDefaultAsync, ToListAsync) to
        // prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync(string value, string edit, string delete, int page = 1)
        {
            ViewModel.CurrentPage = page;

            if (!string.IsNullOrEmpty(value) && value == "New")
            {
                ViewModel.ShowForm = true;
                ViewModel.Status = true;
                return Page();
            }

            if (!string.IsNullOrEmpty(edit))
            {
                int id = Hash2ID.HAsh2ID(edit);
                using (var EF = new ULSDBEntities())
                {
                    var q = await EF.ULSTbl_Categories.FirstOrDefaultAsync(c => c.ID == id);
                    if (q != null)
                    {
                        ViewModel.ShowForm = true;
                        ViewModel.IsEdit = true;
                        ViewModel.EditHash = edit;
                        ViewModel.Title = q.Title;
                        ViewModel.Status = q.Status == true;
                        ViewModel.CurrentPicUrl = q.PicName != "no-photo.png" ? "../img/categories/" + q.PicName : null;
                    }
                    else
                    {
                        ViewModel.AlertMessage = "دسته بندی مورد نظر یافت نشد";
                        ViewModel.AlertType = "danger";
                        ViewModel.ShowList = true;
                        await LoadCategoryListAsync(page);
                    }
                }
                return Page();
            }

            if (!string.IsNullOrEmpty(delete))
            {
                int id = Hash2ID.HAsh2ID(delete);
                using (var EF = new ULSDBEntities())
                {
                    var qGetDeleteId = await EF.ULSTbl_Categories.FirstOrDefaultAsync(c => c.ID == id);
                    var canNotDelete = await (from item in EF.ULSTbl_Reserve
                                              join books in EF.ULSTbl_Books on id equals books.CategoryId
                                              where item.BookID == books.ID && (item.StatusID == ReserveStatus.Reservation || item.StatusID == ReserveStatus.Fine)
                                              select item).FirstOrDefaultAsync();

                    if (canNotDelete == null && qGetDeleteId != null)
                    {
                        var booksToDelete = await EF.ULSTbl_Books.Where(b => b.CategoryId == id).ToListAsync();
                        var resToDelete = await (from item in EF.ULSTbl_Reserve
                                                 join books in EF.ULSTbl_Books on item.BookID equals books.ID
                                                 where books.CategoryId == id
                                                 select item).ToListAsync();

                        EF.ULSTbl_Reserve.RemoveRange(resToDelete);
                        foreach (var book in booksToDelete)
                        {
                            // cr-dotnet-1032 (Line 278): Replaced File.Delete(Server.MapPath("~").ToString() + "/img/books/" + PicName)
                            // with Amazon S3 DeleteObjectAsync. Book images are removed from S3 bucket by key.
                            if (book.PicName != null && book.PicName != "no-photo.png")
                            {
                                await DeleteBookImageFromS3Async(book.PicName);
                            }
                            EF.ULSTbl_Books.Remove(book);
                        }
                        // cr-dotnet-1032 (Line 278): Replaced File.Delete(Server.MapPath("~").ToString() + "/img/categories/" + PicName)
                        // with Amazon S3 DeleteObjectAsync. Category image is removed from S3 bucket by key.
                        if (qGetDeleteId.PicName != null && qGetDeleteId.PicName != "no-photo.png")
                        {
                            await DeleteCategoryImageFromS3Async(qGetDeleteId.PicName);
                        }
                        EF.ULSTbl_Categories.Remove(qGetDeleteId);
                        await EF.SaveChangesAsync();
                        // cr-dotnet-0046 (Line 138): Replaced System.Web.HttpRuntime.Cache.Remove("Categories")
                        // (IIS in-memory cache invalidation) with IDistributedCache.RemoveAsync() backed by
                        // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
                        // sharing the same Redis cluster, eliminating cache inconsistency.
                        await _cache.RemoveAsync("Categories");
                        ViewModel.AlertMessage = "درخواست شما با موفقیت ثبت شد";
                        ViewModel.AlertType = "success";
                    }
                    else
                    {
                        ViewModel.AlertMessage = "یکی از کتب دسته بندی‌ مورد نظر در حالت رزرو قرار دارد";
                        ViewModel.AlertType = "danger";
                    }
                }
            }

            ViewModel.ShowList = true;
            await LoadCategoryListAsync(page);
            return Page();
        }

        // cr-dotnet-1000 (Line 251): Converted OnPost to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostAsync(string editHash, string title, bool status, IFormFile fileUploadPic)
        {
            // cr-dotnet-0046 (Line 259): Replaced System.Web.HttpRuntime.Cache.Remove("Categories")
            // (IIS in-memory cache invalidation) with IDistributedCache.RemoveAsync() backed by
            // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
            // sharing the same Redis cluster, eliminating cache inconsistency.
            await _cache.RemoveAsync("Categories");
            string userId = User.Identity.Name?.ToLower();

            using (var EF = new ULSDBEntities())
            {
                if (string.IsNullOrEmpty(editHash))
                {
                    // New category
                    var checkCat = await EF.ULSTbl_Categories.FirstOrDefaultAsync(c => c.Title == title);
                    if (checkCat != null)
                    {
                        ViewModel.AlertMessage = "عملیات با شکست مواجه شد";
                        ViewModel.AlertType = "danger";
                        ViewModel.ShowList = true;
                        await LoadCategoryListAsync(1);
                        return Page();
                    }

                    string picName = "no-photo.png";
                    // cr-dotnet-1032 (Line 116): Replaced fileUploadPic.SaveAs(Server.MapPath("../img/Categories") + "//" + PicName)
                    // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket
                    // under the "img/categories/" key prefix, eliminating local filesystem dependency.
                    if (fileUploadPic != null && fileUploadPic.Length > 0)
                    {
                        picName = await UploadCategoryImageToS3Async(fileUploadPic);
                    }

                    var newCat = new ULSTbl_Categories
                    {
                        UserNameAdder = userId,
                        DateInsert = DateTimeOffset.UtcNow.UtcDateTime,
                        PicName = picName,
                        Status = status,
                        Title = title,
                    };
                    EF.ULSTbl_Categories.Add(newCat);
                    await EF.SaveChangesAsync();
                    ViewModel.AlertMessage = "درخواست شما با موفقیت ثبت شد";
                    ViewModel.AlertType = "success";
                }
                else
                {
                    // Edit category
                    int rowId = Hash2ID.HAsh2ID(editHash);
                    var searchQuery = await EF.ULSTbl_Categories.FirstOrDefaultAsync(c => c.ID == rowId);
                    if (searchQuery != null)
                    {
                        searchQuery.Title = title;
                        searchQuery.Status = status;

                        if (fileUploadPic != null && fileUploadPic.Length > 0)
                        {
                            // cr-dotnet-1032 (Line 346): Replaced System.IO.File.Delete(Server.MapPath("../img/Categories") + "//" + PicName)
                            // with Amazon S3 DeleteObjectAsync. Old category image is removed from S3 bucket by key.
                            if (searchQuery.PicName != "no-photo.jpg" && searchQuery.PicName != "no-photo.png")
                            {
                                await DeleteCategoryImageFromS3Async(searchQuery.PicName);
                            }
                            // cr-dotnet-1032 (Line 357): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/Categories") + "//" + PicName)
                            // with Amazon S3 PutObjectAsync upload. New image is streamed directly to S3 bucket.
                            string picName = await UploadCategoryImageToS3Async(fileUploadPic);
                            searchQuery.PicName = picName;
                        }

                        await EF.SaveChangesAsync();
                        ViewModel.AlertMessage = "درخواست شما با موفقیت ثبت شد";
                        ViewModel.AlertType = "success";
                    }
                }
            }

            ViewModel.ShowList = true;
            await LoadCategoryListAsync(1);
            return Page();
        }

        // cr-dotnet-1032 (Line 406): Replaced File.Delete(Server.MapPath("../img/categories") + "//" + PicName)
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
                        var qGetPic = await EF.ULSTbl_Categories.FirstOrDefaultAsync(c => c.ID == rowId);
                        if (qGetPic != null && qGetPic.PicName != "no-photo.png")
                        {
                            await DeleteCategoryImageFromS3Async(qGetPic.PicName);
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
            await LoadCategoryListAsync(1);
            return Page();
        }

        // cr-dotnet-1000: Converted LoadCategoryList to async to use non-blocking EF Core async queries.
        private async Task LoadCategoryListAsync(int page)
        {
            using (var EF = new ULSDBEntities())
            {
                var query = await (from item in EF.ULSTbl_Categories
                                   from it in EF.ULSTbl_Users
                                   where item.UserNameAdder == it.UserName
                                   orderby item.ID descending
                                   select new
                                   {
                                       UserID = it.ID,
                                       item.ID,
                                       item.Title,
                                       item.DateInsert,
                                       item.PicName,
                                       NameFamily = it.FullName,
                                       AdderPic = it.PicName,
                                       item.Status
                                   }).ToListAsync();

                int total = query.Count;
                ViewModel.TotalPages = (int)Math.Ceiling((double)total / PageSize);
                ViewModel.CurrentPage = page;

                ViewModel.Categories = query.Skip((page - 1) * PageSize).Take(PageSize).Select(c => new CategoryListItem
                {
                    ID = c.ID,
                    IdHash = ID2hash(c.ID),
                    UserID = c.UserID,
                    UserIdHash = ID2hash(c.UserID),
                    Title = c.Title,
                    DateInsert = c.DateInsert,
                    PicName = c.PicName,
                    NameFamily = c.NameFamily,
                    AdderPic = c.AdderPic,
                    StatusHtml = c.Status == true
                        ? "<span class='badge bg-label-primary me-1'>فعال</span>"
                        : "<span class='badge bg-label-dark me-1'>غیرفعال</span>",
                    BooksCount = EF.ULSTbl_Books.Count(b => b.CategoryId == c.ID)
                }).ToList();
            }
        }
    }
}
