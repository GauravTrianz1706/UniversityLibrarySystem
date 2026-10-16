@* Migrated from ASP.NET Web Forms Page to ASP.NET Core Razor Pages *@
@page
@model UniversityLibrarySystem.ServerSideCheckerModel
@{
    ViewData["Title"] = "Console";
}
<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <title>@ViewData["Title"]</title>
    <style>
        :root{
            font-family:'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            font-weight:bold;
            background-color:#000;
            color:#0f0;
        }
    </style>
</head>
<body>
    <div>
        @if (!string.IsNullOrEmpty(Model.Output))
        {
            @Html.Raw(Model.Output)
        }
        <script>
            // این تابع پارامترهای URL را پاک می‌کند
            function clearUrlParams() {
                var currentUrl = window.location.href;
                var baseUrl = currentUrl.split('?')[0];
                window.history.replaceState(null, null, baseUrl);
            }
            clearUrlParams();
        </script>
    </div>
</body>
</html>
