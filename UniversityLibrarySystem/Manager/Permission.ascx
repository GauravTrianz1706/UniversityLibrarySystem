@* cr-dotnet-0026 REMEDIATED: Web Forms UserControl fully migrated to ASP.NET Core Razor Pages partial view *@
@* <%@ Control %> directive removed - replaced by Pages/Manager/Permission.cshtml (Razor Page) *@
@* This file is superseded by Pages/Manager/Permission.cshtml - kept for reference only *@
@* Removed: asp:ScriptManager, asp:UpdatePanel, asp:CheckBoxList, asp:LinqDataSource Web Forms controls *@
@* Replaced: Web Forms server controls with standard HTML and Razor syntax *@
@model UniversityLibrarySystem.Manager.PermissionModel
<style type="text/css">
    .overlay {
        position: fixed;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
        background-color: var(--custom-color-bg-menu-item);
        opacity: 0.8;
        z-index: 9999;
        display: flex;
        justify-content: center;
        align-items: center;
    }

    svg {
        width: 3.25em;
        transform-origin: center;
        animation: rotate4 2s linear infinite;
    }

    circle {
        fill: none;
        stroke: hsl(214, 97%, 59%);
        stroke-width: 2;
        stroke-dasharray: 1, 200;
        stroke-dashoffset: 0;
        stroke-linecap: round;
        animation: dash4 1.5s ease-in-out infinite;
        background: none;
    }

    @@keyframes rotate4 {
        100% {
            transform: rotate(360deg);
        }
    }

    @@keyframes dash4 {
        0% {
            stroke-dasharray: 1, 200;
            stroke-dashoffset: 0;
        }

        50% {
            stroke-dasharray: 90, 200;
            stroke-dashoffset: -35px;
        }

        100% {
            stroke-dashoffset: -125px;
        }
    }

    #permission-checkbox-list input[type="checkbox"] {
        -webkit-appearance: none;
        -moz-appearance: none;
        appearance: none;
        width: 50px;
        height: 25px;
        background-color: #ccc;
        border-radius: 25px;
        position: relative;
        cursor: pointer;
        outline: none;
        border: 2px solid transparent;
        transition: background-color 0.3s, border-color 0.3s;
    }

        #permission-checkbox-list input[type="checkbox"]:before {
            content: "";
            width: 20px;
            height: 90%;
            background-color: white;
            border-radius: 50%;
            position: absolute;
            top: 1.2px;
            left: 2px;
            transition: transform 0.3s;
        }

        #permission-checkbox-list input[type="checkbox"]:checked {
            background-color: #275EFE;
        }

            #permission-checkbox-list input[type="checkbox"]:checked:before {
                transform: translateX(25px);
            }

        #permission-checkbox-list input[type="checkbox"]:disabled {
            background-color: #e6e6e6;
            cursor: not-allowed;
        }

            #permission-checkbox-list input[type="checkbox"]:disabled:before {
                background-color: #ccc;
            }

    #permission-checkbox-list label {
        display: inline-block;
        vertical-align: middle;
        margin-right: 5px;
        margin-bottom: 3.5%;
    }
</style>

<script type="text/javascript">
    $(document).ready(function () {
        $("#btn_CheckAll").click(function () {
            $("#permission-checkbox-list").find("input[type=checkbox]").each(function () {
                if (!this.checked) {
                    this.checked = true;
                    return;
                }
                this.checked = false;
            });
        });
    });
</script>

<div class="content-wrapper">
    <!-- Content -->

    <div class="container-xxl flex-grow-1 container-p-y">
        <h4 class="py-3 breadcrumb-wrapper mb-4">سطح دسترسی مدیران</h4>
        <div class="mb-0">
            @Html.Raw(Model.Message)
        </div>
        <form method="post">
            @Html.AntiForgeryToken()
            <!-- Striped Rows -->
            <div class="col-12">
                <div class="card mb-4">
                    <div class="card-body">
                        <div class="row">
                            <div class="col-12 mb-2">
                                <a href="#" id="btn_CheckAll">انتخاب همه</a>
                                <br />
                                <label class="form-label" for="drpPersonal">نام مدیر:</label>
                                <div>
                                    <select class="form-select" id="drpPersonal" name="drpPersonal"
                                            onchange="this.form.submit()">
                                        <option value="0">لطفا یک حساب کاربری را انتخاب کنید</option>
                                        @foreach (var admin in Model.AdminUsers)
                                        {
                                            <option value="@admin.UserName" @(Model.SelectedUserName == admin.UserName ? "selected" : "")>@admin.PersonalName</option>
                                        }
                                    </select>
                                    @if (!string.IsNullOrEmpty(Model.SelectedUserName) && Model.SelectedUserName != "0")
                                    {
                                        <span>نام کاربری مدیر: @Model.SelectedUserName</span>
                                    }
                                </div>
                            </div>
                            <div class="col-12" id="permission-checkbox-list">
                                @foreach (var page in Model.PermissionPages)
                                {
                                    <div>
                                        <input type="checkbox" id="perm_@page.ID" name="selectedPermissions" value="@page.ID"
                                               @(page.IsSelected ? "checked" : "")>
                                        <label for="perm_@page.ID">@page.PageNameForDisplay</label>
                                    </div>
                                }
                            </div>

                            <hr class="my-4 mx-n4">
                            <div class="pt-4">
                                <button type="submit" class="btn btn-primary me-sm-3 me-1"
                                        @(string.IsNullOrEmpty(Model.SelectedUserName) || Model.SelectedUserName == "0" ? "disabled" : "")>ثبت</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <!--/ Striped Rows -->
        </form>
    </div>
    <!-- / Content -->
    <div class="content-backdrop fade"></div>
</div>
