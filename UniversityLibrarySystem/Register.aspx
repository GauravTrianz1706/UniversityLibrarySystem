@* Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages *@
@page
@model UniversityLibrarySystem.RegisterModel
@{
    ViewData["Title"] = "ثبت نام";
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
    <main class="login-bg">

        <form method="post">
            @Html.AntiForgeryToken()
            <div class="register-form-area">
                <div class="register-form mb-1">
                    <div class="register-heading">
                        <a href="/">
                            <img src="~/img/logo/logo.png" alt="@Model.SiteName" />
                        </a>
                        <span>ثبت نام</span>
                        <p>برای دسترسی به سامانه ثبت نام کنید</p>
                        @if (Model.SuccessMessage)
                        {
                            <span class="text-success" style="font-size:12px;">ثبت نام شما با موفقیت انجام شد</span>
                        }
                        @if (!string.IsNullOrEmpty(Model.ErrorMessage))
                        {
                            <span class="text-danger" style="font-size:12px;">@Model.ErrorMessage</span>
                        }
                    </div>
                    <div class="input-box">

                        <!-- نام کامل -->
                        <div class="single-input-fields">
                            <label for="FullName">نام و نام خانوادگی</label>
                            <input type="text" id="FullName" name="FullName" class="form-control" placeholder="نام و نام خانوادگی" value="@Model.FullName" required />
                            <span asp-validation-for="FullName" class="text-danger"></span>
                        </div>

                        <!-- نام کاربری -->
                        <div class="single-input-fields">
                            <label for="Username">نام کاربری</label>
                            <input type="text" id="Username" name="Username" class="form-control" placeholder="نام کاربری" value="@Model.Username" required pattern="^\d+$" maxlength="50" />
                            <span class="text-danger" id="usernameError"></span>
                        </div>

                        <!-- رمز عبور -->
                        <div class="single-input-fields">
                            <label for="Password">رمز عبور</label>
                            <input type="password" id="Password" name="Password" class="form-control" placeholder="رمز عبور" required pattern="^[A-Za-z\d@@$!%*?&]{8,}$" maxlength="50" />
                            <span class="text-danger" id="passwordError"></span>
                        </div>

                        <!-- شماره موبایل -->
                        <div class="single-input-fields">
                            <label for="Mobile">شماره موبایل</label>
                            <input type="text" id="Mobile" name="Mobile" class="form-control" placeholder="شماره موبایل" value="@Model.Mobile" required pattern="09(0(\d)|1(\d)|2(\d)|3(\d)|(9(\d)))\d{7}$" />
                            <span class="text-danger" id="mobileError"></span>
                        </div>

                        <!-- ایمیل -->
                        <div class="single-input-fields">
                            <label for="Email">ایمیل</label>
                            <input type="email" id="Email" name="Email" class="form-control" placeholder="ایمیل" value="@Model.Email" pattern="^[a-zA-Z0-9._%+-]+@@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,255}$" />
                            <span class="text-danger" id="emailError"></span>
                        </div>

                    </div>
                    <div class="register-footer">
                        <p>حساب کاربری دارید؟ <a href="/Login">وارد شوید</a></p>
                        <button type="submit" class="submit-btn3">ثبت نام</button>
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
    <script>
        var currentUrl = window.location.href;
        var baseUrl = currentUrl.split('?')[0];
        window.history.replaceState(null, null, baseUrl);
    </script>
</body>
</html>
