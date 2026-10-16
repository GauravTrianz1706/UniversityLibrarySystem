@* Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages partial view *@
@* Removed: <%@ Control %> directive, runat="server", asp:ListView, asp:LinqDataSource, asp:Button Web Forms controls *@
@* Replaced: Web Forms server controls with standard HTML and Razor syntax *@
@using Classes
@model UniversityLibrarySystem.Student.LoginReportModel
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
<form method="post">
    @Html.AntiForgeryToken()
    @if (!string.IsNullOrEmpty(Model.LiteralMsg))
    {
        @Html.Raw(Model.LiteralMsg)
    }
    <div class="row m-2">
        <h4>گزارش ورود</h4>
        <div>
            <a href="ChangePassword" class="btn btn-primary me-1 my-2">تغییر کلمه عبور</a>
            <button type="submit" asp-page-handler="DeleteAllDevice" class="btn btn-danger mx-1 my-2">خروج از تمام دستگاه‌ها</button>
            <div class="table-responsive card text-nowrap">
                <table class="table table-striped">
                    <thead>
                        <tr>
                            <th>اطلاعات دستگاه</th>
                            <th>تاریخ اولین ورود</th>
                        </tr>
                    </thead>
                    <tbody class="table-border-bottom-0">
                        @if (Model.DeviceList != null)
                        {
                            @foreach (var item in Model.DeviceList)
                            {
                                <tr>
                                    <td>@Html.Raw(item.BrowserName)</td>
                                    <td dir="ltr">@item.DateTime</td>
                                </tr>
                            }
                        }
                    </tbody>
                </table>
            </div>
        </div>
    </div>
    <script>window.history.replaceState(null, null, window.location.href.split('?')[0]);</script>
</form>
