@page "/BookDetail/{bookID}"
@model UniversityLibrarySystem.BookDetailModel

@if (Model.BookNotFound)
{
    <style>
        .lead { font-size: 18pt; }
        main { text-align: center; }
    </style>
    <main class="m-5 text-center">
        <h1 class="display-1">404</h1>
        <p class="lead">کتاب مورد نظر یافت نشد!</p>
        <p>متاسفانه کتابی که به دنبال آن هستید یافت نشد یا غیرفعال شده است.</p>
        <a onclick="history.go(-1)" class="btn header-btn">بازگشت</a>
    </main>
}
else
{
    <div class="breadcrumb-items">
        <div class="row">
            <ul class="breadcrumb">
                <li>
                    <a href="/" class="breadcrumb-link">صفحه اصلی</a>
                </li>
                <li class="chevron"><span class="fa fa-chevron-left"></span></li>
                <li>
                    @Html.Raw(Model.BottomNavHtml)
                </li>
            </ul>
        </div>
    </div>

    <div class="book-details">
        <div class="container">
            <div class="row">
                <div class="col-xl-12">
                    <div class="row">
                        <div class="col-xl-12">
                            @if (!string.IsNullOrEmpty(Model.AlertMessage))
                            {
                                @Html.Raw(Model.AlertMessage)
                            }
                            @Html.Raw(Model.BookShortDetailHtml)

                            @if (Model.ShowNeedLogin)
                            {
                                <a href="/Login" class="btn btn-success">ورود به سیستم</a>
                            }
                            else if (Model.ShowDeleteReserve)
                            {
                                <form method="post">
                                    <button type="submit" asp-page-handler="DelReserve" class="btn btn-danger">حذف رزرو</button>
                                </form>
                            }
                            else if (Model.ShowAdminView)
                            {
                                <a href="/Manager/Books?Edit=@Model.BookIdHash" class="btn btn-success">ویرایش کتاب</a>
                            }
                            else if (Model.ShowUnavailable)
                            {
                                <p class="text-danger">کتاب مورد نظر موجود نمی باشد</p>
                            }
                            else if (Model.ShowReserve)
                            {
                                <form method="post">
                                    <button type="submit" asp-page-handler="Reserve" class="btn btn-success">رزرو کتاب</button>
                                </form>
                            }
                            else if (Model.ShowReserved)
                            {
                                <a href="/Dash" class="btn btn-success">مشاهده رزروها</a>
                            }

                            @if (!string.IsNullOrEmpty(Model.LabelMessage))
                            {
                                <span style="color:red">@Html.Raw(Model.LabelMessage)</span>
                            }
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    @Html.Raw(Model.BookLongDetailHtml)

    <section class="related-books section-bg">
        <div class="container">
            <div class="row justify-content-center">
                <div class="col-xl-7 col-lg-8">
                    <div class="section-tittle text-center mb-55">
                        <h2>کتاب های مرتبط</h2>
                    </div>
                </div>
                <div class="row">
                    <div class="col-xl-12">
                        <div class="related-books-box">
                            @Html.Raw(Model.RelatedBooksHtml)
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </section>
    <script>
        function clearUrlParams() {
            window.history.replaceState(null, null, window.location.href.split('?')[0]);
        }
        clearUrlParams();
    </script>
}
