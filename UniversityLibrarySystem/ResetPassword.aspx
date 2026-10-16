@* Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages *@
@page
@model UniversityLibrarySystem.ResetPasswordModel
@{
    ViewData["Title"] = "فراموشی رمز عبور";
}
<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <title>@ViewData["Title"]</title>
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <link rel="shortcut icon" type="image/x-icon" href="~/img/logo/favicon.png">
    <link rel="stylesheet" href="~/css/bootstrap.min.css">
    <link rel="stylesheet" href="~/css/owl.carousel.min.css">
    <link rel="stylesheet" href="~/css/slicknav.css">
    <link rel="stylesheet" href="~/css/animate.min.css">
    <link rel="stylesheet" href="~/css/magnific-popup.css">
    <link rel="stylesheet" href="~/css/fontawesome-all.min.css">
    <link rel="stylesheet" href="~/css/themify-icons.css">
    <link rel="stylesheet" href="~/css/slick.css">
    <link rel="stylesheet" href="~/css/nice-select.css">
    <link rel="stylesheet" href="~/css/style.css">
</head>
<body>

    <main class="register-bg">

        <form method="post">
            @Html.AntiForgeryToken()
            <div class="register-form-area">
                <div class="register-form">
                    <div class="register-heading">
                        <a href="/">
                            <img src="~/img/logo/logo.png" alt="@Model.SiteName" />
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


    <script src="~/js/modernizr-3.5.0.min.js" defer></script>
    <script src="~/js/jquery-1.12.4.min.js" defer></script>
    <script src="~/js/popper.min.js" defer></script>
    <script src="~/js/bootstrap.min.js" defer></script>

    <script src="~/js/owl.carousel.min.js" defer></script>
    <script src="~/js/slick.min.js" defer></script>
    <script src="~/js/jquery.slicknav.min.js" defer></script>

    <script src="~/js/wow.min.js" defer></script>
    <script src="~/js/jquery.magnific-popup.js" defer></script>
    <script src="~/js/jquery.nice-select.min.js" defer></script>
    <script src="~/js/jquery.counterup.min.js" defer></script>
    <script src="~/js/waypoints.min.js" defer></script>

    <script src="~/js/jquery.form.js" defer></script>
    <script src="~/js/jquery.validate.min.js" defer></script>
    <script src="~/js/jquery.ajaxchimp.min.js" defer></script>

    <script src="~/js/main.js" defer></script>

</body>
</html>
