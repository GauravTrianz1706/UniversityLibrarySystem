@* Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages *@
@* cr-dotnet-1038: Static assets served via Amazon CloudFront + S3 CDN instead of local origin *@
@page
@model UniversityLibrarySystem.ResetPasswordModel
@{
    ViewData["Title"] = "فراموشی رمز عبور";
    // cr-dotnet-1038: Read CloudFront CDN base URL from environment variable.
    // Set CLOUDFRONT_CDN_URL to your CloudFront distribution domain at deployment time,
    // e.g. https://d1234abcd.cloudfront.net  (no trailing slash).
    // Assets must be uploaded to the backing S3 bucket under the same relative paths
    // (css/, js/, img/) so CloudFront can serve them from the S3 origin.
    var cdnBaseUrl = Environment.GetEnvironmentVariable("CLOUDFRONT_CDN_URL") ?? string.Empty;
}
<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <title>@ViewData["Title"]</title>
    <meta name="viewport" content="width=device-width, initial-scale=1">
    @* cr-dotnet-1038: favicon served from CloudFront + S3 *@
    <link rel="shortcut icon" type="image/x-icon" href="@(cdnBaseUrl)/img/logo/favicon.png">
    @* cr-dotnet-1038: CSS assets served from CloudFront + S3 (lines 14-23) *@
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/bootstrap.min.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/owl.carousel.min.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/slicknav.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/animate.min.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/magnific-popup.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/fontawesome-all.min.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/themify-icons.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/slick.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/nice-select.css">
    <link rel="stylesheet" href="@(cdnBaseUrl)/css/style.css">
</head>
<body>

    <main class="register-bg">

        <form method="post">
            @Html.AntiForgeryToken()
            <div class="register-form-area">
                <div class="register-form">
                    <div class="register-heading">
                        <a href="/">
                            @* cr-dotnet-1038: Logo image served from CloudFront + S3 *@
                            <img src="@(cdnBaseUrl)/img/logo/logo.png" alt="@Model.SiteName" />
                        </a>
                        <span>بازیابی رمز عبور</span>
                        <p>رمز عبور جدید خود را تنظیم کنید.</p>
                        @if (!string.IsNullOrEmpty(Model.ErrorMessage))
                        {
                            <span class="text-danger" style="font-size:12px;">@Model.ErrorMessage</span>
                        }
                    </div>
                    <div class="input-box">

                        <div class="single-input-fields">
                            <label for="NewPassword">رمز عبور جدید</label>
                            <input type="password" id="NewPassword" name="NewPassword" class="form-control" placeholder="رمز عبور جدید" required
                                   pattern="^[A-Za-z\d@@$!%*?&]{8,}$" maxlength="50"
                                   title="رمز عبور باید حداقل ۸ کاراکتر باشد و می‌تواند شامل حروف لاتین یا اعداد باشد" />
                            <span class="text-danger" id="passwordError"></span>
                        </div>
                    </div>
                    <div class="forgot-password-footer">
                        <button type="submit" class="submit-btn3">تغییر رمز عبور</button>
                    </div>
                </div>
            </div>
        </form>
    </main>


    @* cr-dotnet-1038: JS assets served from CloudFront + S3 *@
    <script src="@(cdnBaseUrl)/js/modernizr-3.5.0.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery-1.12.4.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/popper.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/bootstrap.min.js" defer></script>

    <script src="@(cdnBaseUrl)/js/owl.carousel.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/slick.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery.slicknav.min.js" defer></script>

    <script src="@(cdnBaseUrl)/js/wow.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery.magnific-popup.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery.nice-select.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery.counterup.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/waypoints.min.js" defer></script>

    <script src="@(cdnBaseUrl)/js/jquery.form.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery.validate.min.js" defer></script>
    <script src="@(cdnBaseUrl)/js/jquery.ajaxchimp.min.js" defer></script>

    <script src="@(cdnBaseUrl)/js/main.js" defer></script>

</body>
</html>
