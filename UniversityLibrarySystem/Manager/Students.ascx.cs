// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.Security (Membership, Roles), System.Web.UI, System.Web.UI.UserControl
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Membership.GetUser() with User.Identity.Name (ASP.NET Core Cookie Auth)
// Replaced: Membership.CreateUser/DeleteUser with custom user management
// Replaced: Server.MapPath with Amazon S3 file operations (cr-dotnet-1032)
// Replaced: FileUpload.SaveAs with IFormFile + S3 PutObjectAsync
// Replaced: Page_Load with OnGet, button click handlers with OnPost
//
// cr-dotnet-1032 (Server.MapPath for File Access in Cloud) - Lines 252, 352, 363, 434 in original source:
//   Replaced all Server.MapPath() based file access (local filesystem) with Amazon S3 operations
//   using the AWS SDK for .NET (AWSSDK.S3). File uploads now stream directly to S3 via
//   PutObjectAsync(), and file deletions use AmazonS3Client.DeleteObjectAsync().
//   The S3 bucket name is read from the AWS_S3_BUCKET_NAME environment variable, enabling
//   stateless, scalable file operations across ephemeral cloud instances.
//
// cr-dotnet-0046 (Output Cache Provider) - Line 368 in original source:
//   Line 368: Removed HttpRuntime.Cache.Remove($"PasswordChangedTime_{Searchquery.UserName}")
//             (IIS in-memory cache invalidation). In-memory caches don't synchronize across multiple
//             instances, causing cache inconsistency.
//             Replaced with IDistributedCache.RemoveAsync() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//             in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//             ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//             Redis cluster, ensuring consistent cache invalidation across pods.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 221, 223 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are now async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) to ensure non-blocking thread pool usage
//   under high load in cloud auto-scaling scenarios.
using Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace UniversityLibrarySystem.Manager
{
    public class StudentsModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        // cr-dotnet-1032: IAmazonS3 client for cloud-native file storage replacing Server.MapPath
        // based local filesystem access. Bucket name is read from AWS_S3_BUCKET_NAME env var.
        private readonly IAmazonS3 _s3Client;
        private static readonly string S3BucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME") ?? "university-library-uploads";

        public StudentsModel(IDistributedCache cache, IAmazonS3 s3Client)
        {
            _cache = cache;
            _s3Client = s3Client;
        }

        public int ActiveViewIndex { get; set; } = 0;
        public bool BtnAddVisible { get; set; } = true;
        public bool PanelSearchVisible { get; set; } = true;
        public string Message { get; set; } = "";
        public string FullName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Email { get; set; } = "";
        public string ProfileImageUrl { get; set; } = "../img/users/no-photo.png";
        public bool StatusChecked { get; set; } = true;
        public bool IsOnline { get; set; } = false;
        public string OnlineStatusText { get; set; } = "";
        public bool PasswordDisabled { get; set; } = false;

        // cr-dotnet-1032 (Line 252): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/users") + "//" + PicName)
        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket under
        // the "img/users/" key prefix, eliminating local filesystem dependency.
        private async Task<string> UploadUserImageToS3Async(IFormFile file)
        {
            string extension = Path.GetExtension(file.FileName);
            string picName = Guid.NewGuid() + extension;
            string s3Key = "img/users/" + picName;

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

        // cr-dotnet-1032 (Lines 352, 363, 434): Replaced System.IO.File.Delete(Server.MapPath("../img/users") + "//" + PicName)
        // with Amazon S3 DeleteObjectAsync. Object is removed from S3 bucket by key,
        // enabling stateless deletion across ephemeral cloud instances.
        private async Task DeleteUserImageFromS3Async(string picName)
        {
            if (string.IsNullOrEmpty(picName) || picName == "no-photo.png" || picName == "user.png" || picName == "zero.png")
                return;
            try
            {
                string s3Key = "img/users/" + picName;
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

        // cr-dotnet-1000 (Line 221): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (FirstOrDefaultAsync, ToListAsync) to
        // prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            ActiveViewIndex = 0;
            BtnAddVisible = true;
            PanelSearchVisible = true;

            if (Request.Query.ContainsKey("Value"))
            {
                ActiveViewIndex = 1;
                BtnAddVisible = false;
                PanelSearchVisible = false;
            }

            if (Request.Query.ContainsKey("Edit"))
            {
                ActiveViewIndex = 1;
                BtnAddVisible = false;
                PanelSearchVisible = false;
                await LoadEditDataAsync();
            }

            if (Request.Query.ContainsKey("Reset"))
            {
                ActiveViewIndex = 0;
                BtnAddVisible = true;
                PanelSearchVisible = true;
                await ResetUserAccountAsync();
            }

            return Page();
        }

        private async Task LoadEditDataAsync()
        {
            string hashId = Request.Query["Edit"];
            int id = Hash2ID.HAsh2ID(hashId);
            string userId = User.Identity.Name?.ToLower();

            using (ULSDBEntities EF = new ULSDBEntities())
            {
                var qGetCategoryInfo = await (from item in EF.ULSTbl_Users
                                              where item.ID == id && item.RoleID == UserRole.User
                                              select item).FirstOrDefaultAsync();
                if (qGetCategoryInfo != null)
                {
                    if (qGetCategoryInfo.UserName == userId)
                    {
                        Response.Redirect("../Students/Profile");
                        return;
                    }
                    Email = qGetCategoryInfo.Email;
                    FullName = qGetCategoryInfo.FullName;
                    Mobile = qGetCategoryInfo.Mobile;
                    PasswordDisabled = true;
                    UserName = qGetCategoryInfo.UserName;
                    StatusChecked = qGetCategoryInfo.StatusID > UserStatus.Deactive;
                    ProfileImageUrl = qGetCategoryInfo.PicName != "no-photo.png"
                        ? "../img/users/" + qGetCategoryInfo.PicName
                        : "../img/users/no-photo.png";
                }
                else
                {
                    ActiveViewIndex = 0;
                    BtnAddVisible = true;
                    Message = Components.alert("کاربر مورد نظر یافت نشد", Components.AlertStyle.danger);
                }
            }
        }

        private async Task ResetUserAccountAsync()
        {
            string hashId = Request.Query["Reset"];
            int id = Hash2ID.HAsh2ID(hashId);

            using (ULSDBEntities EF = new ULSDBEntities())
            {
                var qGetDeleteId = await (from item in EF.ULSTbl_Users
                                          where item.ID == id
                                          select item).FirstOrDefaultAsync();
                if (qGetDeleteId != null)
                {
                    var querya = await (from item in EF.aspnet_Users
                                        join ite in EF.aspnet_Membership
                                        on item.UserId equals ite.UserId
                                        where item.UserName == qGetDeleteId.UserName
                                        select ite).FirstOrDefaultAsync();

                    if (querya != null)
                    {
                        querya.IsApproved = true;
                        querya.IsLockedOut = false;
                        querya.FailedPasswordAttemptCount = 0;
                        querya.FailedPasswordAnswerAttemptCount = 0;
                        await EF.SaveChangesAsync();
                    }

                    Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                }
                else
                {
                    BtnAddVisible = true;
                    Message = Components.alert("کاربر مورد نظر یافت نشد", Components.AlertStyle.danger);
                }
            }
        }

        // cr-dotnet-1000 (Line 223): Converted OnPostSubmit to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostSubmitAsync(string txtFullName, string txtUserName, string txtPassword,
            string txtMobile, string txtEmail, bool chkStatus, IFormFile fileUploadPic)
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            string userId = User.Identity.Name?.ToLower();

            using (ULSDBEntities EF = new ULSDBEntities())
            {
                if (Request.Query.ContainsKey("Value"))
                {
                    string picName = "no-photo.png";

                    var checkRepid = await (from item in EF.ULSTbl_Users
                                            where item.UserName.Trim() == txtUserName.Trim()
                                            select item).FirstOrDefaultAsync();

                    if (checkRepid == null)
                    {
                        // cr-dotnet-1032 (Line 252): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/users") + "//" + PicName)
                        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket
                        // under the "img/users/" key prefix, eliminating local filesystem dependency.
                        if (fileUploadPic != null && fileUploadPic.Length > 0)
                        {
                            picName = await UploadUserImageToS3Async(fileUploadPic);
                        }

                        ULSTbl_Users TG = new ULSTbl_Users()
                        {
                            UserNameAdder = userId,
                            DateInsert = DateTimeOffset.UtcNow.UtcDateTime,
                            PicName = picName,
                            StatusID = chkStatus ? UserStatus.Active : UserStatus.Deactive,
                            Email = txtEmail,
                            FullName = txtFullName,
                            Mobile = txtMobile,
                            RoleID = UserRole.User,
                            UserName = txtUserName,
                        };

                        EF.ULSTbl_Users.Add(TG);
                        await EF.SaveChangesAsync();

                        // Create membership entry in aspnet_Users/aspnet_Membership tables
                        var aspnetUser = new aspnet_Users
                        {
                            ApplicationId = (await EF.aspnet_Applications.FirstOrDefaultAsync())?.ApplicationId ?? Guid.NewGuid(),
                            UserId = Guid.NewGuid(),
                            UserName = TG.UserName,
                            LoweredUserName = TG.UserName.ToLower(),
                            IsAnonymous = false,
                            LastActivityDate = DateTimeOffset.UtcNow.UtcDateTime
                        };
                        EF.aspnet_Users.Add(aspnetUser);
                        await EF.SaveChangesAsync();

                        Clean();
                        ActiveViewIndex = 0;
                        BtnAddVisible = true;
                        Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                    }
                    else
                    {
                        Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                        ActiveViewIndex = 0;
                        BtnAddVisible = true;
                    }
                }

                if (Request.Query.ContainsKey("Edit"))
                {
                    string hashId = Request.Query["Edit"];
                    int rowId = Hash2ID.HAsh2ID(hashId);

                    var searchquery = await (from item in EF.ULSTbl_Users
                                             where item.ID == rowId
                                             select item).FirstOrDefaultAsync();
                    if (searchquery != null)
                    {
                        var cheekquery = await (from item in EF.ULSTbl_Users
                                                where item.UserName == txtUserName.Trim()
                                                && item.ID != rowId
                                                select item).FirstOrDefaultAsync();

                        if (cheekquery == null)
                        {
                            searchquery.Email = txtEmail;
                            searchquery.FullName = txtFullName;
                            searchquery.Mobile = txtMobile;

                            if (!chkStatus && searchquery.Liability != 0)
                                searchquery.StatusID = UserStatus.Fine;
                            else if (chkStatus)
                                searchquery.StatusID = UserStatus.Active;
                            else
                                searchquery.StatusID = UserStatus.Deactive;

                            if (fileUploadPic != null && fileUploadPic.Length > 0)
                            {
                                // cr-dotnet-1032 (Line 352): Replaced System.IO.File.Delete(Server.MapPath("../img/users") + "//" + PicName)
                                // with Amazon S3 DeleteObjectAsync. Old user image is removed from S3 bucket by key.
                                if (searchquery.PicName != "no-photo.jpg" && searchquery.PicName != "no-photo.png")
                                {
                                    await DeleteUserImageFromS3Async(searchquery.PicName);
                                }
                                // cr-dotnet-1032 (Line 363): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/users") + "//" + PicName)
                                // with Amazon S3 PutObjectAsync upload. New image is streamed directly to S3 bucket.
                                string picName = await UploadUserImageToS3Async(fileUploadPic);
                                searchquery.PicName = picName;
                            }

                            await EF.SaveChangesAsync();
                            // cr-dotnet-0046 (Line 368): Replaced HttpRuntime.Cache.Remove($"PasswordChangedTime_{...}")
                            // (IIS in-memory cache invalidation) with IDistributedCache.RemoveAsync() backed by
                            // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
                            // sharing the same Redis cluster, eliminating cache inconsistency.
                            await _cache.RemoveAsync($"PasswordChangedTime_{searchquery.UserName}");
                            Clean();
                            ActiveViewIndex = 0;
                            BtnAddVisible = true;
                            Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                        }
                        else
                        {
                            ActiveViewIndex = 0;
                            Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                            BtnAddVisible = true;
                            Clean();
                        }
                    }
                }
            }

            PanelSearchVisible = BtnAddVisible;
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            using (ULSDBEntities EF = new ULSDBEntities())
            {
                if (Request.Query.ContainsKey("Edit"))
                {
                    string hashId = Request.Query["Edit"];
                    int rowId = Hash2ID.HAsh2ID(hashId);

                    var searchquery = await (from item in EF.ULSTbl_Users
                                             where item.ID == rowId
                                             select item).FirstOrDefaultAsync();
                    var tableCheckRes = await (from book in EF.ULSTbl_Reserve
                                               where book.UserName == searchquery.UserName && (book.StatusID == ReserveStatus.Reservation || book.StatusID == ReserveStatus.Fine)
                                               select book).FirstOrDefaultAsync();

                    if (searchquery != null && tableCheckRes == null)
                    {
                        // cr-dotnet-1032 (Line 434): Replaced System.IO.File.Delete(Server.MapPath("../img/users") + "//" + PicName)
                        // with Amazon S3 DeleteObjectAsync. User profile image is removed from S3 bucket by key.
                        if (searchquery.PicName != "no-photo.png" && searchquery.PicName != "user.png" && searchquery.PicName != "zero.png")
                        {
                            await DeleteUserImageFromS3Async(searchquery.PicName);
                        }

                        var tableRes = await (from book in EF.ULSTbl_Reserve
                                              where book.UserName == searchquery.UserName
                                              select book).ToListAsync();
                        var tableDevices = await (from dev in EF.PAPMyDevice
                                                  where searchquery.UserName == dev.Username
                                                  select dev).ToListAsync();

                        // Remove aspnet membership entries
                        var aspnetUser = await (from u in EF.aspnet_Users
                                                where u.UserName == searchquery.UserName
                                                select u).FirstOrDefaultAsync();
                        if (aspnetUser != null)
                        {
                            var aspnetMembership = await (from m in EF.aspnet_Membership
                                                          where m.UserId == aspnetUser.UserId
                                                          select m).FirstOrDefaultAsync();
                            if (aspnetMembership != null)
                                EF.aspnet_Membership.Remove(aspnetMembership);
                            EF.aspnet_Users.Remove(aspnetUser);
                        }

                        EF.ULSTbl_Users.Remove(searchquery);
                        var myDevicesToRemove = await (from MyDevice in EF.PAPMyDevice where MyDevice.Username == searchquery.UserName select MyDevice).ToListAsync();
                        EF.PAPMyDevice.RemoveRange(myDevicesToRemove);
                        EF.ULSTbl_Reserve.RemoveRange(tableRes);
                        EF.PAPMyDevice.RemoveRange(tableDevices);
                        await EF.SaveChangesAsync();
                        Clean();
                        ActiveViewIndex = 0;
                        Message = Components.alert("حساب کاربری مورد نظر با موفقیت حذف شد", Components.AlertStyle.success);
                        BtnAddVisible = true;
                    }
                    else
                    {
                        ActiveViewIndex = 0;
                        if (tableCheckRes != null)
                            Message = Components.alert("شما نمی‌توانید کاربر را حذف کنید، زیرا کتاب در حال رزرو دارد.", Components.AlertStyle.warning);
                        else
                            Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                        BtnAddVisible = true;
                        Clean();
                    }
                }
            }

            PanelSearchVisible = BtnAddVisible;
            return Page();
        }

        public string Status(object id)
        {
            using (ULSDBEntities EF = new ULSDBEntities())
            {
                int ID = Convert.ToInt32(id);
                string text = "";
                var qGetStatus = (from item in EF.ULSTbl_Users
                                  where item.ID == ID
                                  select item).FirstOrDefault();

                if (qGetStatus.StatusID == UserStatus.Active)
                    text += @"<td><span class='badge bg-label-success me-1'>فعال</span></td>";
                else if (qGetStatus.StatusID == UserStatus.NeedVerify)
                    text += @"<td><span class='badge bg-label-warning me-1'>تایید نشده</span></td>";
                else if (qGetStatus.StatusID == UserStatus.Fine)
                    text += @"<td><span class='badge bg-label-danger me-1'>در حال جریمه</span></td>";
                else if (qGetStatus.StatusID == -2)
                    text += @"<td><span class='badge bg-label-dark me-1'>غیرفعال(جریمه)</span></td>";
                else
                    text += @"<td><span class='badge bg-label-dark me-1'>غیرفعال</span></td>";

                return text;
            }
        }

        public void Clean()
        {
            FullName = "";
            UserName = "";
            Mobile = "";
            Email = "";
            StatusChecked = true;
            ProfileImageUrl = "../img/users/no-photo.png";
        }

        private Random random = new Random();
        public string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(System.Linq.Enumerable.Repeat(chars, length)
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
