@page "/ForgetPassword"
@model UniversityLibrarySystem.ForgetPasswordModel
@{
    ViewData["Title"] = Classes.SiteConfig.mixTitle("فراموشی رمز عبور");
}
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <title>@ViewData["Title"]</title>
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <link rel="shortcut icon" type="image/x-icon" href="img/logo/favicon.png">
    <link rel="stylesheet" href="css/bootstrap.min.css">
    <link rel="stylesheet" href="css/owl.carousel.min.css">
    <link rel="stylesheet" href="css/slicknav.css">
    <link rel="stylesheet" href="css/animate.min.css">
    <link rel="stylesheet" href="css/magnific-popup.css">
    <link rel="stylesheet" href="css/fontawesome-all.min.css">
    <link rel="stylesheet" href="css/themify-icons.css">
    <link rel="stylesheet" href="css/slick.css">
    <link rel="stylesheet" href="css/nice-select.css">
    <link rel="stylesheet" href="css/style.css">
</head>
<body>
    <main class="register-bg">
        <div class="register-form-area">
            <div class="register-form">
                <div class="register-heading">
                    <a href="/">
                        <img src="img/logo/logo.png" alt="@Classes.SiteConfig.getSiteName()" />
                    </a>
                    <span>فراموشی رمز عبور</span>
                    <p>مقادیر زیر را جهت تغییر رمزعبور پر کنید.</p>
                    @if (Model.ShowSuccess)
                    {
                        <span class="text-success" style="font-size:12px">@Model.SuccessMessage</span>
                    }
                    @if (Model.ShowError)
                    {
                        <span class="text-danger" style="font-size:12px">@Model.ErrorMessage</span>
                    }
                </div>
                <form method="post">
                    @Html.AntiForgeryToken()
                    <div class="input-box">
                        <div class="single-input-fields">
                            <label for="Fullname">نام کامل</label>
                            <input id="Fullname" name="Fullname" class="form-control" placeholder="نام کامل" maxlength="50" value="@Model.Fullname" required />
                        </div>
                    </div>
                    <div class="input-box">
                        <div class="single-input-fields">
                            <label for="Username">نام کاربری</label>
                            <input id="Username" name="Username" class="form-control" placeholder="نام کاربری" maxlength="50" pattern="^\d+$" value="@Model.Username" required />
                        </div>
                    </div>
                    <div class="input-box">
                        <div class="single-input-fields">
                            <label for="Mobile">شماره موبایل</label>
                            <input id="Mobile" name="Mobile" class="form-control" placeholder="شماره موبایل" pattern="09(0(\d)|1(\d)|2(\d)|3(\d)|(9(\d)))\d{7}$" value="@Model.Mobile" required />
                        </div>
                    </div>
                    <div class="input-box">
                        <div class="single-input-fields">
                            <label for="Email">ایمیل</label>
                            <input id="Email" name="Email" class="form-control" placeholder="ایمیل" type="email" value="@Model.Email" />
                        </div>
                    </div>
                    <div class="forgot-password-footer">
                        <button type="submit" class="submit-btn3">بررسی</button>
                    </div>
                </form>
            </div>
        </div>
    </main>
    <script src="js/modernizr-3.5.0.min.js" defer></script>
    <script src="js/jquery-1.12.4.min.js" defer></script>
    <script src="js/popper.min.js" defer></script>
    <script src="js/bootstrap.min.js" defer></script>
    <script src="js/owl.carousel.min.js" defer></script>
    <script src="js/slick.min.js" defer></script>
    <script src="js/jquery.slicknav.min.js" defer></script>
    <script src="js/wow.min.js" defer></script>
    <script src="js/jquery.magnific-popup.js" defer></script>
    <script src="js/jquery.nice-select.min.js" defer></script>
    <script src="js/jquery.counterup.min.js" defer></script>
    <script src="js/waypoints.min.js" defer></script>
    <script src="js/jquery.form.js" defer></script>
    <script src="js/jquery.validate.min.js" defer></script>
    <script src="js/jquery.ajaxchimp.min.js" defer></script>
    <script src="js/main.js" defer></script>
    <script>
        var currentUrl = window.location.href;
        var baseUrl = currentUrl.split('?')[0];
        window.history.replaceState(null, null, baseUrl);
    </script>
</body>
</html>
