// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.Security, System.Web.UI, System.Web.UI.WebControls
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet
// Replaced: btnSubmit_Click with OnPostSubmit
// Replaced: btnResetProfile_Click with OnPostResetProfile
// Replaced: Server.MapPath with Amazon S3 file operations (cr-dotnet-1032)
// Replaced: Membership API with HttpContext.User
//
// cr-dotnet-1032 (Server.MapPath for File Access in Cloud) - Lines 63, 68, 117 in original source:
//   Replaced all Server.MapPath() based file access (local filesystem) with Amazon S3 operations
//   using the AWS SDK for .NET (AWSSDK.S3). File uploads now stream directly to S3 via
//   PutObjectAsync(), and file deletions use AmazonS3Client.DeleteObjectAsync().
//   The S3 bucket name is read from the AWS_S3_BUCKET_NAME environment variable, enabling
//   stateless, scalable file operations across ephemeral cloud instances.
using Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace UniversityLibrarySystem.Student
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

        public string LiteralMessage { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = "../img/Personal/no-photo.png";
        public bool ShowResetButton { get; set; } = false;

        // cr-dotnet-1032 (Line 63): Replaced fileUploadPic.SaveAs(Server.MapPath("../img/users") + "//" + PicName)
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

        // cr-dotnet-1032 (Lines 68, 117): Replaced File.Delete(Server.MapPath("../img/users") + "//" + PicName)
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

        public IActionResult OnGet()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    string UserId = User.Identity?.Name?.ToLower();
                    if (string.IsNullOrEmpty(UserId)) return Redirect("/");

                    var qGetUser = (from item in EF.ULSTbl_Users
                                    where item.UserName == UserId
                                    select item).FirstOrDefault();

                    if (qGetUser != null)
                    {
                        FullName = qGetUser.FullName;
                        Mobile = qGetUser.Mobile;
                        Email = qGetUser.Email;
                        ProfileImageUrl = "../img/users/" + qGetUser.PicName;
                        ShowResetButton = qGetUser.PicName != "no-photo.png" && qGetUser.PicName != "user.png";
                    }
                }
            }
            catch (Exception ex)
            {
                LiteralMessage = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSubmitAsync(string txtFullName, string txtMobile, string txtEmail, IFormFile fileUploadPic)
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    string UserId = User.Identity?.Name?.ToLower();
                    if (string.IsNullOrEmpty(UserId)) return Redirect("/");

                    var qGetUser = (from item in EF.ULSTbl_Users
                                    where item.UserName == UserId
                                    select item).FirstOrDefault();

                    if (qGetUser == null)
                    {
                        LiteralMessage = Components.alert("کاربر یافت نشد.", Components.AlertStyle.danger);
                        return Page();
                    }

                    string oldPhoto = qGetUser.PicName;

                    if (fileUploadPic != null && fileUploadPic.Length > 0)
                    {
                        // cr-dotnet-1032 (Line 63): Replaced fileUploadPic.SaveAs(Server.MapPath("../img/users") + "//" + PicName)
                        // with Amazon S3 PutObjectAsync upload. File is streamed directly to S3 bucket
                        // under the "img/users/" key prefix, eliminating local filesystem dependency.
                        string PicName = await UploadUserImageToS3Async(fileUploadPic);

                        // cr-dotnet-1032 (Line 68): Replaced File.Delete(Server.MapPath("../img/users") + "//" + oldPhoto)
                        // with Amazon S3 DeleteObjectAsync. Old profile image is removed from S3 bucket by key.
                        if (oldPhoto != "no-photo.png" && oldPhoto != "user.png")
                        {
                            await DeleteUserImageFromS3Async(oldPhoto);
                        }

                        ShowResetButton = true;
                        qGetUser.PicName = PicName;
                    }

                    qGetUser.FullName = txtFullName;
                    qGetUser.Mobile = txtMobile;
                    qGetUser.Email = txtEmail;

                    EF.SaveChanges();

                    FullName = qGetUser.FullName;
                    Mobile = qGetUser.Mobile;
                    Email = qGetUser.Email;
                    ProfileImageUrl = "../img/users/" + qGetUser.PicName;
                    ShowResetButton = qGetUser.PicName != "no-photo.png" && qGetUser.PicName != "user.png";

                    LiteralMessage = Components.alert("پروفایل شما با موفقیت بروزرسانی شد", Components.AlertStyle.success);
                }
            }
            catch (Exception ex)
            {
                LiteralMessage = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostResetProfileAsync()
        {
            try
            {
                using (var EF = new ULSDBEntities())
                {
                    string UserId = User.Identity?.Name?.ToLower();
                    if (string.IsNullOrEmpty(UserId)) return Redirect("/");

                    var qGetUser = (from item in EF.ULSTbl_Users
                                    where item.UserName == UserId
                                    select item).FirstOrDefault();

                    if (qGetUser != null && qGetUser.PicName != "no-photo.png" && qGetUser.PicName != "user.png")
                    {
                        // cr-dotnet-1032 (Line 117): Replaced File.Delete(Server.MapPath("../img/users") + "//" + PicName)
                        // with Amazon S3 DeleteObjectAsync. Profile image is removed from S3 bucket by key,
                        // enabling stateless deletion across ephemeral cloud instances.
                        await DeleteUserImageFromS3Async(qGetUser.PicName);
                        qGetUser.PicName = "no-photo.png";
                        EF.SaveChanges();
                    }

                    if (qGetUser != null)
                    {
                        FullName = qGetUser.FullName;
                        Mobile = qGetUser.Mobile;
                        Email = qGetUser.Email;
                        ProfileImageUrl = "../img/users/" + qGetUser.PicName;
                    }

                    LiteralMessage = Components.alert("تصویر پروفایل شما با موفقیت بازنشانی شد", Components.AlertStyle.success);
                    ShowResetButton = false;
                }
            }
            catch (Exception ex)
            {
                LiteralMessage = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
            }

            return Page();
        }
    }
}
