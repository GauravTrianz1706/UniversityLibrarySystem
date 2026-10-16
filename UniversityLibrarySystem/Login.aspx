@page "/Login"
@model UniversityLibrarySystem.LoginModel
@{
    ViewData["Title"] = Classes.SiteConfig.mixTitle("ورود به حساب کاربری");
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
    <main class="login-bg">
        <div class="login-form-area">
            <div class="login-form mb-1">
                <div class="login-heading">
                    <a href="/">
                        <img src="img/logo/logo.png" alt="@Classes.SiteConfig.getSiteName()" />
                    </a>
                    <span>ورود</span>
                    <p>برای دسترسی به سامانه وارد شوید</p>
                    @if (Model.ShowError)
                    {
                        <span class="text-danger" style="font-size:12px">@Model.ErrorMessage</span>
                    }
                </div>
                <form method="post">
                    @Html.AntiForgeryToken()
                    <div class="input-box">
                        <div class="single-input-fields">
                            <label for="UserName">نام کاربری</label>
                            <input id="UserName" name="UserName" maxlength="50" class="form-control" placeholder="نام کاربری" pattern="^\d+$" required />
                        </div>
                        <div class="single-input-fields">
                            <label for="Password">رمز عبور</label>
                            <input id="Password" name="Password" maxlength="50" type="password" class="form-control" placeholder="رمز عبور" pattern="^[A-Za-z\d@@$!%*?&amp;]{8,50}$" required />
                        </div>
                    </div>
                    <div class="login-footer">
                        <p><a href="/ForgetPassword">رمز خود را فراموش کرده اید؟</a><br />حساب کاربری ندارید؟ <a href="Register">ثبت نام کنید</a></p>
                        <button type="submit" class="submit-btn3">ورود</button>
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
</body>
</html>
