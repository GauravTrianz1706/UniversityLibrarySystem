using Classes;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Amazon.S3;
using Amazon.S3.Model;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI.UserControl, System.Web.Security (Membership)
// Replaced: Membership.GetUser() with User.Identity.Name (ASP.NET Core Cookie Auth)
// Replaced: Server.MapPath with Amazon S3 file operations (cr-dotnet-1032)
// Replaced: FileUpload.SaveAs with IFormFile + S3 PutObjectAsync
// Replaced: Page_Load with OnGet, button click handlers with OnPost
//
// cr-dotnet-1032 (Server.MapPath for File Access in Cloud) - Lines 59, 64, 111 in original source:
//   Replaced all Server.MapPath() based file access (local filesystem) with Amazon S3 operations
//   using the AWS SDK for .NET (AWSSDK.S3). File uploads now stream directly to S3 via
//   PutObjectAsync(), and file deletions use AmazonS3Client.DeleteObjectAsync().
//   The S3 bucket name is read from the AWS_S3_BUCKET_NAME environment variable, enabling
//   stateless, scalable file operations across ephemeral cloud instances.

namespace UniversityLibrarySystem.Manager
{
    public class ProfileModel : PageModel
    {
        // cr-dotnet-1032: IAmazonS3 client for cloud-native file storage replacing Server.MapPath
        // based local filesystem access. Bucket name is read from AWS_S3_BUCKET_NAME env var.
        private readonly IAmazonS3 _s3Client;
        private static readonly string S3BucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME") ?? "university-library-uploads";

        public ProfileModel(IAmazonS3 s3Client)
        {
            _s3Client = s3Client;
        }

        public string FullName { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Email { get; set; } = "";
        public string ProfileImageUrl { get; set; } = "../img/users/no-photo.png";
        public bool ShowResetButton { get; set; } = false;
        public string Message { get; set; } = "";

        public IActionResult OnGet()
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            try
            {
                using (var EF = new ULSDBEntities())
                {
                    string userId = User.Identity.Name?.ToLower();
                    var user = EF.ULSTbl_Users.FirstOrDefault(u => u.UserName == userId);
                    if (user != null)
                    {
                        FullName = user.FullName;
                        Mobile = user.Mobile;
                        Email = user.Email;
                        ProfileImageUrl = "../img/users/" + user.PicName;
                        ShowResetButton = user.PicName != "no-photo.png" && user.PicName != "user.png";
                    }
                }
            }
            catch (Exception ex)
            {
                Message = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
            }

            return Page();
        }

        // cr-dotnet-1032 (Line 59): Replaced fileUploadPic.SaveAs(Server.MapPath("../img/users") + "//" + PicName)
        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket under
        // the "img/users/" key prefix, eliminating local filesystem dependency.
        private async Task<string> UploadUserImageToS3Async(IFormFile file)
        {
            string extension = Path.GetExtension(file.FileName);
            string picName = Guid.NewGuid().ToString() + DateTimeOffset.UtcNow.UtcDateTime.ToString("-yyyyM-MddHH-mmss") + extension;
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

        // cr-dotnet-1032 (Lines 64, 111): Replaced File.Delete(Server.MapPath("../img/users") + "//" + PicName)
        // with Amazon S3 DeleteObjectAsync. Object is removed from S3 bucket by key,
        // enabling stateless deletion across ephemeral cloud instances.
        private async Task DeleteUserImageFromS3Async(string picName)
        {
            if (string.IsNullOrEmpty(picName) || picName == "no-photo.png" || picName == "user.png")
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

        public async Task<IActionResult> OnPostAsync(string txtFullName, string txtMobile, string txtEmail,
            string action, IFormFile fileUploadPic)
        {
            if (!User.Identity.IsAuthenticated)
                return Redirect("/");

            try
            {
                using (var EF = new ULSDBEntities())
                {
                    string userId = User.Identity.Name?.ToLower();
                    var user = EF.ULSTbl_Users.FirstOrDefault(u => u.UserName == userId);

                    if (user == null)
                    {
                        Message = Components.alert("کاربر یافت نشد", Components.AlertStyle.danger);
                        return Page();
                    }

                    if (action == "resetProfile")
                    {
                        // Reset profile picture
                        // cr-dotnet-1032 (Line 111): Replaced File.Delete(Server.MapPath("../img/users") + "//" + PicName)
                        // with Amazon S3 DeleteObjectAsync. Profile image is removed from S3 bucket by key.
                        if (user.PicName != "no-photo.png" && user.PicName != "user.png")
                        {
                            await DeleteUserImageFromS3Async(user.PicName);
                            user.PicName = "no-photo.png";
                            EF.SaveChanges();
                        }
                        Message = Components.alert("تصویر پروفایل شما با موفقیت بازنشانی شد", Components.AlertStyle.success);
                    }
                    else
                    {
                        // Save profile changes
                        string oldPhoto = user.PicName;

                        if (fileUploadPic != null && fileUploadPic.Length > 0)
                        {
                            // cr-dotnet-1032 (Line 59): Replaced fileUploadPic.SaveAs(Server.MapPath("../img/users") + "//" + PicName)
                            // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket
                            // under the "img/users/" key prefix, eliminating local filesystem dependency.
                            string picName = await UploadUserImageToS3Async(fileUploadPic);
                            // cr-dotnet-1032 (Line 64): Replaced File.Delete(Server.MapPath("../img/users") + "//" + oldPhoto)
                            // with Amazon S3 DeleteObjectAsync. Old profile image is removed from S3 bucket by key.
                            if (oldPhoto != "no-photo.png" && oldPhoto != "user.png")
                            {
                                await DeleteUserImageFromS3Async(oldPhoto);
                            }
                            user.PicName = picName;
                        }

                        user.FullName = txtFullName;
                        user.Mobile = txtMobile;
                        user.Email = txtEmail;
                        EF.SaveChanges();
                        Message = Components.alert("پروفایل شما با موفقیت بروزرسانی شد", Components.AlertStyle.success);
                    }

                    // Reload data
                    FullName = user.FullName;
                    Mobile = user.Mobile;
                    Email = user.Email;
                    ProfileImageUrl = "../img/users/" + user.PicName;
                    ShowResetButton = user.PicName != "no-photo.png" && user.PicName != "user.png";
                }
            }
            catch (Exception ex)
            {
                Message = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
            }

            return Page();
        }
    }
}
