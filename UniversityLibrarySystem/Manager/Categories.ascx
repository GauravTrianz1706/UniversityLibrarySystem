@* Migrated from ASP.NET Web Forms UserControl (<%@ Control %>) to ASP.NET Core Razor partial view *@
@* This file replaces Manager/Categories.ascx as a Razor partial view *@
@model UniversityLibrarySystem.Manager.CategoriesViewModel
<style>
    .activepage {
        line-height: 2;
        text-align: center;
        min-width: calc(2.0075rem + 0px);
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
    <div class="container-xxl flex-grow-1 container-p-y">
        <h4 class="py-3 breadcrumb-wrapper mb-4">مدیریت دسته بندی ها</h4>
        @if (!string.IsNullOrEmpty(Model.AlertMessage))
        {
            <div class="alert alert-@Model.AlertType mb-2" role="alert">@Html.Raw(Model.AlertMessage)</div>
        }
        @if (Model.ShowList)
        {
            <div class="mb-0 py-1">
                <form action="Categories" method="get">
                    <input name="Value" value="New" hidden="hidden" type="text" />
                    <button type="submit" class="btn btn-primary my-2"><i class="bx bx-plus"></i>&nbsp;درج جدید</button>
                </form>
            </div>
            <div class="card">
                <div class="table-responsive text-nowrap">
                    <table class="table table-striped text-nowrap">
                        <thead>
                            <tr>
                                <th>درج کننده</th>
                                <th>تصویر</th>
                                <th>عنوان</th>
                                <th>وضعیت</th>
                                <th>تعداد کتاب ها</th>
                                <th>تاریخ درج</th>
                                <th>عمل‌ها</th>
                            </tr>
                        </thead>
                        <tbody class="table-border-bottom-0">
                            @foreach (var cat in Model.Categories)
                            {
                                <tr>
                                    <td>
                                        <ul class="list-unstyled users-list m-0 avatar-group d-flex align-items-center">
                                            <li class="avatar avatar-xs pull-up">
                                                <img src="../img/users/@cat.AdderPic" style="min-width: 26px;" alt="Avatar" class="rounded-circle">
                                            </li>
                                            <a style="color:#000;" href="./Manager?Edit=@cat.UserIdHash">@cat.NameFamily</a>
                                        </ul>
                                    </td>
                                    <td>
                                        <div class="avatar avatar-xl">
                                            <img class="example-image" onerror="this.src='../img/Error/no-photo.png'"
                                                src='../img/Categories/@cat.PicName' alt="@cat.Title" />
                                        </div>
                                    </td>
                                    <td>@cat.Title</td>
                                    <td>@Html.Raw(cat.StatusHtml)</td>
                                    <td>@cat.BooksCount</td>
                                    <td>@cat.DateInsert.ToShortDateString()</td>
                                    <td>
                                        <div class="dropdown">
                                            <button type="button" class="btn p-0 dropdown-toggle hide-arrow" data-bs-toggle="dropdown">
                                                <i class="bx bx-dots-vertical-rounded"></i>
                                            </button>
                                            <div class="dropdown-menu">
                                                <a class="dropdown-item" href="./Categories?Edit=@cat.IdHash"><i class="bx bx-edit-alt me-1"></i>مشاهده / ویرایش</a>
                                                <a class="dropdown-item" href="./Categories?Delete=@cat.IdHash"><i class="bx bx-trash me-1"></i>حذف</a>
                                            </div>
                                        </div>
                                    </td>
                                </tr>
                            }
                        </tbody>
                    </table>
                </div>
                <hr />
                <div class="col-lg-4" style="padding: 1rem;">
                    <nav aria-label="Page navigation">
                        <ul class="pagination pagination-info" style="display: flex;">
                            @for (int p = 1; p <= Model.TotalPages; p++)
                            {
                                <li class="page-item @(p == Model.CurrentPage ? "active" : "")">
                                    <a class="page-link @(p == Model.CurrentPage ? "activepage" : "noactivepage")" href="?page=@p">@p</a>
                                </li>
                            }
                        </ul>
                    </nav>
                </div>
            </div>
        }
        @if (Model.ShowForm)
        {
            <form method="post" enctype="multipart/form-data">
                @Html.AntiForgeryToken()
                <input type="hidden" name="EditHash" value="@Model.EditHash" />
                <div class="col-12">
                    <div class="card mb-4">
                        <div class="card-body">
                            <div class="row">
                                <div class="col-xl-6 col-md-6 col-sm-12 mb-4">
                                    <label class="form-label">عنوان</label>
                                    <div class="input-group input-group-merge">
                                        <input type="text" name="Title" class="form-control text-start" dir="ltr" maxlength="50" placeholder="عنوان" value="@Model.Title" required minlength="2" maxlength="100">
                                    </div>
                                </div>
                                <div class="mb-3 col-md-12">
                                    <label class="form-label">تصویر</label>
                                    <div class="col-6">
                                        <input type="file" name="fileUploadPic" accept="image/*" onchange="previewFile()" class="form-control" />
                                    </div>
                                    <br />
                                    <hr />
                                    <img id="fileUploadPicPreview" src="@(string.IsNullOrEmpty(Model.CurrentPicUrl) ? "../img/Error/no-photo.png" : Model.CurrentPicUrl)" height="225px" width="225px" />
                                </div>
                                <div class="col-xl-4 col-md-6 col-sm-12 mb-4">
                                    <label class="form-label">وضعیت</label>
                                    <div class="form-check form-switch mb-2">
                                        <input class="form-check-input" type="checkbox" name="Status" id="chkStatus" @(Model.Status ? "checked" : "")>
                                        <label class="form-check-label" for="chkStatus">وضعیت</label>
                                    </div>
                                </div>
                                <hr class="my-4 mx-n4">
                                <div class="pt-4">
                                    <button type="submit" class="btn btn-primary me-sm-3 me-1">ثبت</button>
                                    <a href="./Categories" class="btn btn-label-secondary">بازگشت</a>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </form>
        }
    </div>
    <div class="content-backdrop fade"></div>
</div>
<script>
    function previewFile() {
        var preview = document.querySelector('#fileUploadPicPreview');
        var file = document.querySelector('input[name="fileUploadPic"]').files[0];
        var reader = new FileReader();
        reader.onloadend = function () { preview.src = reader.result; }
        if (file) { reader.readAsDataURL(file); } else { preview.src = "../img/Error/no-photo.png"; }
    }
    function clearUrlParams() {
        var currentUrl = window.location.href;
        var baseUrl = currentUrl.split('?')[0];
        window.history.replaceState(null, null, baseUrl);
    }
    clearUrlParams();
</script>
