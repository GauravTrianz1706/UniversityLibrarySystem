@* Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages partial view *@
@* Removed: <%@ Control %> directive, runat="server", asp:* Web Forms controls *@
@* Replaced: Web Forms server controls with standard HTML and Razor syntax *@
@* Replaced: asp:Image, asp:FileUpload, asp:Button, asp:Literal with HTML equivalents *@
@using Classes
@model UniversityLibrarySystem.Student.ProfileModel
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
<style>
    #profileImage {
        border-radius: 0.25rem;
    }
</style>
<script type="text/javascript">
    function previewFile() {
        var preview = document.querySelector('#profileImage');
        var file = document.querySelector('#fileUploadPic').files[0];
        var reader = new FileReader();

        reader.onloadend = function () {
            preview.src = reader.result;
        }

        if (file) {
            reader.readAsDataURL(file);
        } else {
            preview.src = "../img/no-photo.png";
        }
    }
</script>

<!-- Content -->

<div class="container-xxl flex-grow-1 container-p-y">
    <h4 id="head-title" class="py-3 breadcrumb-wrapper mb-4">پروفایل من</h4>
    @if (!string.IsNullOrEmpty(Model.LiteralMessage))
    {
        @Html.Raw(Model.LiteralMessage)
    }

    <div class="row">
        <div class="col-md-12">
            <div class="card mb-4">
                <div class="card-body">
                    <form method="post" enctype="multipart/form-data">
                        @Html.AntiForgeryToken()
                        <div class="row">
                            <div class="mb-3 col-md-12">
                                <label for="fileUploadPic" class="form-label">تصویر پروفایل</label>
                                <div class="col-6">
                                    <img id="profileImage" src="@Model.ProfileImageUrl" height="225" width="225" alt="Profile" />
                                    <br />
                                    @if (Model.ShowResetButton)
                                    {
                                        <button type="submit" asp-page-handler="ResetProfile" class="btn btn-danger my-2 mx-3">بازنشانی تصویر پروفایل</button>
                                    }
                                </div>
                                <br />
                                <hr />
                                <input accept="image/*" id="fileUploadPic" name="fileUploadPic" onchange="previewFile()" type="file" class="form-control" />
                            </div>
                            <div class="mb-3 col-md-6">
                                <label for="txtFullName" class="form-label">نام و نام خانوادگی</label>
                                <input class="form-control" type="text" id="txtFullName" name="txtFullName" value="@Model.FullName" placeholder="نام و نام خانوادگی" required maxlength="50">
                            </div>
                            <div class="mb-3 col-md-6">
                                <label for="txtMobile" class="form-label">موبایل</label>
                                <input class="form-control text-start" dir="ltr" type="text" id="txtMobile" name="txtMobile" value="@Model.Mobile" placeholder="موبایل" required>
                            </div>
                            <div class="mb-3 col-md-6">
                                <label for="txtEmail" class="form-label">ایمیل</label>
                                <input class="form-control text-start" dir="ltr" type="text" id="txtEmail" name="txtEmail" value="@Model.Email" placeholder="ایمیل">
                            </div>
                        </div>
                        <div class="mt-2">
                            <button type="submit" asp-page-handler="Submit" class="btn btn-primary me-2">ذخیره تغییرات</button>
                            <a href="./" class="btn btn-label-secondary">بازگشت</a>
                        </div>
                    </form>
                </div>
                <!-- /Account -->
            </div>
        </div>
    </div>
</div>
