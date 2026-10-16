@* cr-dotnet-0026 REMEDIATED: Web Forms UserControl fully migrated to ASP.NET Core Razor Pages partial view *@
@* <%@ Control %> directive removed - replaced by Pages/Manager/Faqs.cshtml (Razor Page) *@
@* This file is superseded by Pages/Manager/Faqs.cshtml - kept for reference only *@
@* Removed: asp:ScriptManager, asp:MultiView, asp:ListView, asp:LinqDataSource, asp:DataPager *@
@* Replaced: Web Forms server controls with standard HTML and Razor syntax *@
@model UniversityLibrarySystem.Manager.FaqsModel
<style>
    .activepage {
        line-height: 2;
        text-align: center;
        min-width: calc( 2.0075rem + 0px );
        border-color: #00cfdd;
        background-color: #00cfdd;
        color: #fff;
        box-shadow: 0 0.125rem 0.25rem rgba(147, 158, 170, 0.4);
        border-top-right-radius: 0.25rem;
        border-bottom-right-radius: 0.25rem;
        border-top-left-radius: 0.25rem;
        border-bottom-left-radius: 0.25rem;
    }

    .noactivepage {
        border-top-right-radius: 0.25rem;
        border-bottom-right-radius: 0.25rem;
        border-top-left-radius: 0.25rem;
        border-bottom-left-radius: 0.25rem;
    }
</style>
<div class="content-wrapper">
    <!-- Content -->

    <div class="container-xxl flex-grow-1 container-p-y">
        <h4 class="py-3 breadcrumb-wrapper mb-4">سوالات متداول</h4>
        <div class="mb-0">
            @if (Model.ShowSearch)
            {
                <form action="Faqs" method="get">
                    <div class="row">
                        <div class="col-12" style="display: block ruby;">
                            <div class="col-6">
                                <input id="text2" name="txtText" type="text" class="form-control" placeholder="عنوان">
                            </div>
                            <div class="col-5">
                                <button type="submit" class="btn btn-success"><i class="bx bx-search-alt-2"></i>&nbsp;جستجو</button>
                            </div>
                        </div>
                    </div>
                </form>
            }
            <form action="Faqs" method="get">
                <input id="Text1" name="Value" value="New" hidden="hidden" type="text" />
                @if (Model.ShowAddButton)
                {
                    <button type="submit" class="btn btn-primary my-2"><i class="bx bx-plus"></i>&nbsp;درج جدید</button>
                }
                @Html.Raw(Model.Message)
            </form>
        </div>

        @if (Model.ShowList)
        {
            <script>
                function clearUrlParams() {
                    var currentUrl = window.location.href;
                    var baseUrl = currentUrl.split('?')[0];
                    window.history.replaceState(null, null, baseUrl);
                }
                clearUrlParams();
            </script>
            <!-- Striped Rows -->
            <div class="card">
                <div class="table-responsive text-nowrap">
                    <table class="table table-striped">
                        <thead>
                            <tr>
                                <th>درج کننده</th>
                                <th>سوال</th>
                                <th>وضعیت</th>
                                <th>تاریخ درج</th>
                                <th>عمل‌ها</th>
                            </tr>
                        </thead>
                        <tbody class="table-border-bottom-0">
                            @foreach (var item in Model.FaqItems)
                            {
                                <tr>
                                    <td>
                                        <ul class="list-unstyled users-list m-0 avatar-group d-flex align-items-center">
                                            <li data-bs-toggle="tooltip" data-popup="tooltip-custom" data-bs-placement="top" class="avatar avatar-xs pull-up" title="" data-bs-original-title="@item.NameFamily">
                                                <img src="../img/users/@item.AdderPic" alt="Avatar" class="rounded-circle">
                                            </li>
                                            <a style="color:#000;" href="./Manager?Edit=@item.UserIdHash">@item.NameFamily</a>
                                        </ul>
                                    </td>
                                    <td>@item.Question</td>
                                    @Html.Raw(item.StatusHtml)
                                    <td>@item.DateInsert.ToShortDateString()</td>
                                    <td>
                                        <div class="dropdown">
                                            <button type="button" class="btn p-0 dropdown-toggle hide-arrow" data-bs-toggle="dropdown">
                                                <i class="bx bx-dots-vertical-rounded"></i>
                                            </button>
                                            <div class="dropdown-menu">
                                                <a class="dropdown-item" href="./Faqs?Edit=@item.IdHash"><i class="bx bx-edit-alt me-1"></i>مشاهده / ویرایش</a>
                                                <a class="dropdown-item" href="./Faqs?Delete=@item.IdHash"><i class="bx bx-trash me-1"></i>حذف</a>
                                            </div>
                                        </div>
                                    </td>
                                </tr>
                            }
                        </tbody>
                    </table>
                </div>
            </div>
            <!--/ Striped Rows -->
        }

        @if (Model.ShowForm)
        {
            <form method="post">
                @Html.AntiForgeryToken()
                <div class="col-12">
                    <div class="card mb-4">
                        <div class="card-body">
                            <div class="row">
                                <div class="col-xl-12 col-md-12 col-sm-12 mb-4 mb">
                                    <label class="form-label" for="txtQuestion">عنوان سوال</label>
                                    <div class="mb-3">
                                        <input type="text" class="form-control" id="txtQuestion" name="txtQuestion" value="@Model.EditQuestion" placeholder="سوال" required />
                                    </div>
                                </div>
                                <div class="col-xl-12 col-md-12 col-sm-12 mb-4">
                                    <label class="form-label" for="txtAnswer">پاسخ</label>
                                    <div class="mb-3">
                                        <textarea class="form-control" id="txtAnswer" name="txtAnswer" rows="5" placeholder="پاسخ" required>@Model.EditAnswer</textarea>
                                    </div>
                                </div>
                                <div class="col-xl-4 col-md-6 col-sm-12 mb-4 mb">
                                    <label class="form-label" for="chkStatus">وضعیت</label>
                                    <div class="form-check form-switch mb-2">
                                        <input class="form-check-input" type="checkbox" id="chkStatus" name="chkStatus" @(Model.EditStatus ? "checked" : "")>
                                        <label class="form-check-label" for="chkStatus">وضعیت</label>
                                    </div>
                                </div>
                                <hr class="my-4 mx-n4">
                                <div class="pt-4">
                                    <button type="submit" class="btn btn-primary me-sm-3 me-1">ثبت</button>
                                    <a href="./Faqs" class="btn btn-label-secondary">بازگشت</a>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </form>
        }
    </div>
    <!-- / Content -->
    <div class="content-backdrop fade"></div>
</div>
