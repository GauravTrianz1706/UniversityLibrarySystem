@* Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages *@
@* Removed: <%@ Page %> directive, runat="server", MasterPageFile, asp:Content Web Forms controls *@
@* Replaced: Web Forms page with Razor Pages layout and syntax *@
@page
@model UniversityLibrarySystem.UpdateModel
@{
    ViewData["Title"] = Classes.SiteConfig.getSiteName();
    Layout = null;
}

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <meta charset="utf-8">
    <meta http-equiv="x-ua-compatible" content="ie=edge">
    <meta name="description" content="">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <link rel="shortcut icon" type="image/x-icon" href="img/logo/favicon.png">
    <title>@ViewData["Title"]</title>
    <link rel="stylesheet" href="css/bootstrap.min.css">
    <link rel="stylesheet" href="css/style.css">
</head>
<body>
    <main class="login-bg">
        <div class="login-form-area text-center">
            <div class="login-form mb-1">
                <img src="img/logo/logo.png" class="mb-1" alt="@ViewData["Title"]" />
                <p class="lead">سایت موقتاً در دسترس نیست.</p>
                <p>در حال حاضر سایت در حال بروزرسانی یا تعمیرات است. لطفاً پس از مدتی مجدداً تلاش کنید.</p>
            </div>
        </div>
    </main>
</body>
</html>
