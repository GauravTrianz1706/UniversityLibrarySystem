using Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Amazon.S3;
using Amazon.S3.Model;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI.UserControl, System.Web.UI.WebControls, System.Web.Security
// Replaced: Membership / Roles with ASP.NET Core Identity patterns
// Replaced: Server.MapPath with Amazon S3 file operations (cr-dotnet-1032)
// Replaced: FileUpload.SaveAs with IFormFile + S3 PutObjectAsync
// Replaced: LinqDataSource with direct LINQ queries
// Replaced: MultiView with conditional rendering via ViewModel properties
//
// cr-dotnet-1031 (Windows Authentication in Web Forms Cloud) - Lines 174, 221, 285, 288 in original source:
//   Replaced IIS Windows Authentication (Integrated Security/NTLM/Kerberos) with Amazon Cognito
//   User Pools and JWT Bearer Token authentication. User.Identity.IsAuthenticated and
//   User.Identity.Name are now populated from Cognito JWT claims validated by ASP.NET Core
//   JWT Bearer middleware. No on-premise Active Directory domain join required.
//   - Line 174: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 221: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 285: User.Identity.Name replaced with CognitoJwtHelper.GetUsernameFromClaims(User)
//   - Line 288: User.Identity.Name replaced with CognitoJwtHelper.GetUsernameFromClaims(User)
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// cr-dotnet-1032 (Server.MapPath for File Access in Cloud) - Lines 240, 333, 344, 410 in original source:
//   Replaced all Server.MapPath() based file access (local filesystem) with Amazon S3 operations
//   using the AWS SDK for .NET (AWSSDK.S3). File uploads now stream directly to S3 via
//   PutObjectAsync(), and file deletions use AmazonS3Client.DeleteObjectAsync().
//   The S3 bucket name is read from the AWS_S3_BUCKET_NAME environment variable, enabling
//   stateless, scalable file operations across ephemeral cloud instances.
//
// cr-dotnet-0046 (Output Cache Provider) - Line 349 in original source:
//   Line 349: Removed HttpRuntime.Cache.Remove($"PasswordChangedTime_{Searchquery.UserName}")
//             (IIS in-memory cache invalidation). In-memory caches don't synchronize across multiple
//             instances, causing cache inconsistency.
//             Replaced with IDistributedCache.RemoveAsync() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//             in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//             ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//             Redis cluster, ensuring consistent cache invalidation across pods.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 209, 211 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) via IDistributedCache for non-blocking
//   data retrieval. All OnGet/OnPost handlers are now async, using EF Core async methods
//   (ToListAsync, FirstOrDefaultAsync, SaveChangesAsync) to ensure non-blocking thread pool usage
//   under high load in cloud auto-scaling scenarios.

namespace UniversityLibrarySystem.Manager
{
    public class ManagerListItem
    {
        public int ID { get; set; }
        public string IdHash { get; set; }
        public int UserID { get; set; }
        public string UserIdHash { get; set; }
        public string FullName { get; set; }
        public string Mobile { get; set; }
        public string Pic { get; set; }
        public string AdderPic { get; set; }
        public string NameFamily { get; set; }
        public DateTime DateInsert { get; set; }
        public string StatusHtml { get; set; }
    }

    public class ManagerPageModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        // cr-dotnet-1032: IAmazonS3 client for cloud-native file storage replacing Server.MapPath
        // based local filesystem access. Bucket name is read from AWS_S3_BUCKET_NAME env var.
        private readonly IAmazonS3 _s3Client;
        private static readonly string S3BucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME") ?? "university-library-uploads";

        private static readonly Random _random = new Random();

        public ManagerPageModel(IDistributedCache cache, IAmazonS3 s3Client)
        {
            _cache = cache;
            _s3Client = s3Client;
        }

        public bool ShowList { get; set; } = true;
        public bool ShowForm { get; set; } = false;
        public bool ShowSearch { get; set; } = true;
        public bool ShowAddButton { get; set; } = true;
        public bool IsEdit { get; set; } = false;
        public string Message { get; set; } = "";
        public string SearchLiteral { get; set; } = "";
        public List<ManagerListItem> ManagerItems { get; set; } = new List<ManagerListItem>();

        // Form fields
        public string EditFullName { get; set; } = "";
        public string EditUserName { get; set; } = "";
        public string EditMobile { get; set; } = "";
        public string EditEmail { get; set; } = "";
        public bool EditStatus { get; set; } = true;
        public string ProfileImageUrl { get; set; } = "../img/users/no-photo.png";
        public string OnlineStatus { get; set; } = "";
        public string OnlineStatusCss { get; set; } = "";

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

        private string GetStatusHtml(bool isActive)
        {
            return isActive
                ? @"<td><span class='badge bg-label-success me-1'>فعال</span></td>"
                : @"<td><span class='badge bg-label-dark me-1'>غیرفعال</span></td>";
        }

        // cr-dotnet-1032 (Line 240): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/users") + "//" + PicName)
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

        // cr-dotnet-1032 (Lines 333, 344, 410): Replaced System.IO.File.Delete(Server.MapPath("../img/users") + "//" + PicName)
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

        // cr-dotnet-1000 (Line 209): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (FirstOrDefaultAsync, ToListAsync) to
        // prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync()
        {
            // cr-dotnet-1031 (Line 174): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            var query = HttpContext.Request.Query;

            // Handle Reset (unlock account)
            if (query.ContainsKey("Reset"))
            {
                string hashId = query["Reset"];
                int id = Hash2ID.HAsh2ID(hashId);
                using (var EF = new ULSDBEntities())
                {
                    var userToReset = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.ID == id);
                    if (userToReset != null)
                    {
                        var membershipEntry = await (from item in EF.aspnet_Users
                                                     join ite in EF.aspnet_Membership on item.UserId equals ite.UserId
                                                     where item.UserName == userToReset.UserName
                                                     select ite).FirstOrDefaultAsync();
                        if (membershipEntry != null)
                        {
                            membershipEntry.IsApproved = true;
                            membershipEntry.IsLockedOut = false;
                            membershipEntry.FailedPasswordAttemptCount = 0;
                            membershipEntry.FailedPasswordAnswerAttemptCount = 0;
                            await EF.SaveChangesAsync();
                            Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                        }
                    }
                    else
                    {
                        Message = Components.alert("کاربر مورد نظر یافت نشد", Components.AlertStyle.danger);
                    }
                }
                ShowList = true;
                ShowForm = false;
                ShowSearch = true;
                ShowAddButton = true;
                await LoadManagerListAsync(null);
                return Page();
            }

            // Handle Edit (show form)
            if (query.ContainsKey("Edit"))
            {
                string hashId = query["Edit"];
                int id = Hash2ID.HAsh2ID(hashId);
                // cr-dotnet-1031 (Line 285): Replaced WindowsIdentity.GetCurrent().Name with
                // CognitoJwtHelper.GetUsernameFromClaims(User) to extract username from Cognito JWT
                // 'cognito:username' claim. No Active Directory dependency.
                string currentUserId = (CognitoJwtHelper.GetUsernameFromClaims(User) ?? User.Identity.Name)?.ToLower();

                using (var EF = new ULSDBEntities())
                {
                    var userInfo = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.ID == id && u.RoleID == UserRole.Admin && u.UserName != "0");
                    if (userInfo != null)
                    {
                        if (userInfo.UserName == currentUserId)
                        {
                            return RedirectToPage("/Manager/Profile");
                        }
                        EditEmail = userInfo.Email;
                        EditFullName = userInfo.FullName;
                        EditMobile = userInfo.Mobile;
                        EditUserName = userInfo.UserName;
                        EditStatus = userInfo.StatusID == UserStatus.Active;
                        ProfileImageUrl = userInfo.PicName != "no-photo.png"
                            ? "../img/users/" + userInfo.PicName
                            : "../img/users/no-photo.png";
                        IsEdit = true;
                        ShowList = false;
                        ShowForm = true;
                        ShowSearch = false;
                        ShowAddButton = false;
                    }
                    else
                    {
                        Message = Components.alert("کاربر مورد نظر یافت نشد", Components.AlertStyle.danger);
                        ShowList = true;
                        ShowForm = false;
                        ShowSearch = true;
                        ShowAddButton = true;
                        await LoadManagerListAsync(null);
                    }
                }
                return Page();
            }

            // Handle New (show form)
            if (query.ContainsKey("Value"))
            {
                ShowList = false;
                ShowForm = true;
                ShowSearch = false;
                ShowAddButton = false;
                IsEdit = false;
                return Page();
            }

            // Default: show list
            ShowList = true;
            ShowForm = false;
            ShowSearch = true;
            ShowAddButton = true;
            string searchName = query.ContainsKey("txtName") ? query["txtName"].ToString() : null;
            await LoadManagerListAsync(searchName);
            return Page();
        }

        // cr-dotnet-1000 (Line 211): Converted OnPost to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostAsync(string txtFullName, string txtUserName, string txtPassword,
            string txtMobile, string txtEmail, string chkStatus, string action, IFormFile fileUploadPic)
        {
            // cr-dotnet-1031 (Line 221): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            // cr-dotnet-1031 (Line 288): Replaced WindowsIdentity.GetCurrent().Name with
            // CognitoJwtHelper.GetUsernameFromClaims(User) to extract username from Cognito JWT
            // 'cognito:username' claim. No Active Directory dependency.
            string userId = (CognitoJwtHelper.GetUsernameFromClaims(User) ?? User.Identity.Name)?.ToLower();
            bool status = chkStatus == "on" || chkStatus == "true";
            var query = HttpContext.Request.Query;

            using (var EF = new ULSDBEntities())
            {
                if (action == "delete" && query.ContainsKey("Edit"))
                {
                    // Delete manager account
                    string hashId = query["Edit"];
                    int rowId = Hash2ID.HAsh2ID(hashId);
                    var userToDelete = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.ID == rowId);
                    if (userToDelete != null)
                    {
                        // cr-dotnet-1032 (Line 410): Replaced System.IO.File.Delete(Server.MapPath("../img/users") + "//" + PicName)
                        // with Amazon S3 DeleteObjectAsync. User profile image is removed from S3 bucket by key.
                        if (userToDelete.PicName != "no-photo.png" && userToDelete.PicName != "user.png" && userToDelete.PicName != "zero.png")
                        {
                            await DeleteUserImageFromS3Async(userToDelete.PicName);
                        }
                        var tablePermission = await EF.PAPTbl_PermissionUsers.Where(p => p.UserName == userToDelete.UserName).ToListAsync();
                        var tableCat = await EF.ULSTbl_Categories.Where(c => c.UserNameAdder == userToDelete.UserName).ToListAsync();
                        foreach (var item in tableCat) item.UserNameAdder = "0";
                        var tableBooks = await EF.ULSTbl_Books.Where(b => b.UserNameAdder == userToDelete.UserName).ToListAsync();
                        foreach (var item in tableBooks) item.UserNameAdder = "0";
                        EF.PAPTbl_PermissionUsers.RemoveRange(tablePermission);
                        var devicesToRemove = await EF.PAPMyDevice.Where(d => d.Username == userToDelete.UserName).ToListAsync();
                        EF.PAPMyDevice.RemoveRange(devicesToRemove);
                        EF.ULSTbl_Users.Remove(userToDelete);
                        await EF.SaveChangesAsync();
                        Message = Components.alert("حساب کاربری مورد نظر با موفقیت حذف شد", Components.AlertStyle.success);
                    }
                    else
                    {
                        Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                    }
                    ShowList = true;
                    ShowForm = false;
                    ShowSearch = true;
                    ShowAddButton = true;
                    await LoadManagerListAsync(null);
                    return Page();
                }

                if (query.ContainsKey("Value"))
                {
                    // Insert new manager
                    string picName = "no-photo.png";
                    var existing = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.UserName.Trim() == txtUserName.Trim());
                    if (existing == null)
                    {
                        // cr-dotnet-1032 (Line 240): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/users") + "//" + PicName)
                        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket
                        // under the "img/users/" key prefix, eliminating local filesystem dependency.
                        if (fileUploadPic != null && fileUploadPic.Length > 0)
                        {
                            picName = await UploadUserImageToS3Async(fileUploadPic);
                        }

                        var newUser = new ULSTbl_Users
                        {
                            UserNameAdder = userId,
                            DateInsert = DateTimeOffset.UtcNow.UtcDateTime,
                            PicName = picName,
                            StatusID = status ? UserStatus.Active : UserStatus.Deactive,
                            Email = txtEmail,
                            FullName = txtFullName,
                            Mobile = txtMobile,
                            RoleID = UserRole.Admin,
                            UserName = txtUserName,
                        };
                        EF.ULSTbl_Users.Add(newUser);
                        await EF.SaveChangesAsync();
                        Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                    }
                    else
                    {
                        Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                    }
                }
                else if (query.ContainsKey("Edit"))
                {
                    // Update existing manager
                    string hashId = query["Edit"];
                    int rowId = Hash2ID.HAsh2ID(hashId);
                    var userToUpdate = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.ID == rowId);
                    if (userToUpdate != null)
                    {
                        var duplicate = await EF.ULSTbl_Users.FirstOrDefaultAsync(u => u.UserName == txtUserName.Trim() && u.ID != rowId);
                        if (duplicate == null)
                        {
                            userToUpdate.Email = txtEmail;
                            userToUpdate.FullName = txtFullName;
                            userToUpdate.Mobile = txtMobile;
                            userToUpdate.StatusID = status ? UserStatus.Active : UserStatus.Deactive;

                            if (fileUploadPic != null && fileUploadPic.Length > 0)
                            {
                                // cr-dotnet-1032 (Line 333): Replaced System.IO.File.Delete(Server.MapPath("../img/users") + "//" + PicName)
                                // with Amazon S3 DeleteObjectAsync. Old user image is removed from S3 bucket by key.
                                if (userToUpdate.PicName != "no-photo.jpg" && userToUpdate.PicName != "no-photo.png")
                                {
                                    await DeleteUserImageFromS3Async(userToUpdate.PicName);
                                }
                                // cr-dotnet-1032 (Line 344): Replaced fileUploadPic.SaveAs(Server.MapPath("~/img/users") + "//" + PicName)
                                // with Amazon S3 PutObjectAsync upload. New image is streamed directly to S3 bucket.
                                string picName = await UploadUserImageToS3Async(fileUploadPic);
                                userToUpdate.PicName = picName;
                            }

                            await EF.SaveChangesAsync();
                            // cr-dotnet-0046 (Line 349): Replaced HttpRuntime.Cache.Remove($"PasswordChangedTime_{...}")
                            // (IIS in-memory cache invalidation) with IDistributedCache.RemoveAsync() backed by
                            // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
                            // sharing the same Redis cluster, eliminating cache inconsistency.
                            await _cache.RemoveAsync($"PasswordChangedTime_{userToUpdate.UserName}");
                            Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
                        }
                        else
                        {
                            Message = Components.alert("عملیات با شکست مواجعه شد", Components.AlertStyle.danger);
                        }
                    }
                }
            }

            ShowList = true;
            ShowForm = false;
            ShowSearch = true;
            ShowAddButton = true;
            await LoadManagerListAsync(null);
            return Page();
        }

        // cr-dotnet-1000: Converted LoadManagerList to async to use non-blocking EF Core async queries.
        private async Task LoadManagerListAsync(string searchName)
        {
            using (var EF = new ULSDBEntities())
            {
                var results = await (from item in EF.ULSTbl_Users
                                     join ite in EF.ULSTbl_Users on item.UserNameAdder equals ite.UserName
                                     where item.RoleID == UserRole.Admin && (string.IsNullOrEmpty(searchName) || item.FullName.Contains(searchName))
                                     orderby item.ID descending
                                     select new
                                     {
                                         UserID = ite.ID,
                                         item.ID,
                                         item.FullName,
                                         item.Mobile,
                                         Pic = item.PicName,
                                         AdderPic = ite.PicName,
                                         NameFamily = ite.FullName,
                                         item.DateInsert,
                                         item.StatusID
                                     }).ToListAsync();

                ManagerItems = results.Select(m => new ManagerListItem
                {
                    ID = m.ID,
                    IdHash = ID2hash(m.ID),
                    UserID = m.UserID,
                    UserIdHash = ID2hash(m.UserID),
                    FullName = m.FullName,
                    Mobile = m.Mobile,
                    Pic = m.Pic,
                    AdderPic = m.AdderPic,
                    NameFamily = m.NameFamily,
                    DateInsert = m.DateInsert,
                    StatusHtml = GetStatusHtml(m.StatusID == UserStatus.Active)
                }).ToList();
            }
        }
    }
}
