@* cr-dotnet-0026 REMEDIATED: Web Forms UserControl fully migrated to ASP.NET Core Razor Pages partial view *@
@* <%@ Control %> directive removed - replaced by Pages/Manager/LoginReport.cshtml (Razor Page) *@
@* This file is superseded by Pages/Manager/LoginReport.cshtml - kept for reference only *@
@* Removed: asp:ListView, asp:LinqDataSource, asp:Button Web Forms controls *@
@* Replaced: Web Forms server controls with standard HTML and Razor syntax *@
@model UniversityLibrarySystem.Manager.LoginReportModel
@Html.Raw(Model.Message)
<div class="row m-2">
    <h4>گزارش ورود</h4>
    <div>
        <a href="ChangePassword" class="btn btn-primary me-1">تغییر کلمه عبور</a>
        <form method="post" asp-page-handler="DeleteAllDevices">
            @Html.AntiForgeryToken()
            <button type="submit" class="btn btn-danger mx-1">خروج از تمام دستگاه‌ها</button>
        </form>
        <div class="table-responsive card text-nowrap my-3">
            <table class="table table-striped">
                <thead>
                    <tr>
                        <th>اطلاعات دستگاه</th>
                        <th>تاریخ اولین ورود</th>
                    </tr>
                </thead>
                <tbody class="table-border-bottom-0">
                    @foreach (var device in Model.Devices)
                    {
                        <tr>
                            <td>@Html.Raw(device.BrowserName)</td>
                            <td dir="ltr">@device.DateTime</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>
<script>window.history.replaceState(null, null, window.location.href.split('?')[0]);</script>
