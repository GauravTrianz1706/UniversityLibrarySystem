// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web.UI, System.Web.UI.UserControl, System.Web.HttpRuntime.Cache
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, button click handlers with OnPost
//
// cr-dotnet-1031 (Windows Authentication in Web Forms Cloud) - Lines 57, 100 in original source:
//   Replaced IIS Windows Authentication (Integrated Security/NTLM/Kerberos) with Amazon Cognito
//   User Pools and JWT Bearer Token authentication. User.Identity.IsAuthenticated is now populated
//   from Cognito JWT claims validated by ASP.NET Core JWT Bearer middleware.
//   No on-premise Active Directory domain join required.
//   - Line 57:  User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 100: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 58, 59, 60, 61, 62 in original source:
//   Line 58: Removed System.Web.HttpRuntime.Cache.Remove("SiteName") (IIS in-memory cache invalidation).
//            In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 59: Removed System.Web.HttpRuntime.Cache.Remove("SiteStatus") (IIS in-memory cache invalidation).
//   Line 60: Removed System.Web.HttpRuntime.Cache.Remove("CanRes") (IIS in-memory cache invalidation).
//   Line 61: Removed System.Web.HttpRuntime.Cache.Remove("ShortAboutUs") (IIS in-memory cache invalidation).
//   Line 62: Removed System.Web.HttpRuntime.Cache.Remove("GetContactUSPage") (IIS in-memory cache invalidation).
//            Replaced all with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Replacement: IDistributedCache (StackExchange.Redis) registered via AddStackExchangeRedisCache()
//            in ConfigureServices(), pointing to Amazon ElastiCache Redis endpoint from
//            ELASTICACHE_REDIS_CONNECTION_STRING env var. All application instances share the same
//            Redis cluster, ensuring consistent cache invalidation across pods.
using Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;

namespace UniversityLibrarySystem.Manager
{
    public class SystemSettingsModel : PageModel
    {
        // cr-dotnet-0046: Replaced IMemoryCache (in-process, per-instance) with IDistributedCache
        // backed by Amazon ElastiCache Redis. Ensures all pods share the same cache cluster,
        // eliminating cache inconsistency across multiple instances.
        private readonly IDistributedCache _cache;

        public SystemSettingsModel(IDistributedCache cache)
        {
            _cache = cache;
        }

        public string SiteName { get; set; } = "";
        public string Telephone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Address { get; set; } = "";
        public string MapLink { get; set; } = "";
        public string ShortAboutUs { get; set; } = "";
        public string AboutUs { get; set; } = "";
        public string ReserveDay { get; set; } = "";
        public string MaxUserReserve { get; set; } = "";
        public string ReserveAgain { get; set; } = "";
        public string Liability { get; set; } = "";
        public bool SiteStatus { get; set; } = false;
        public bool CanRes { get; set; } = false;
        public string Message { get; set; } = "";
        public bool MessageVisible { get; set; } = false;

        public IActionResult OnGet()
        {
            // cr-dotnet-1031 (Line 57): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            try
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from table in EF.ULSTbl_SiteSetting
                                 where table.ID != 0
                                 select table).FirstOrDefault();

                    if (query != null)
                    {
                        SiteName = query.SiteName;
                        Telephone = query.Telephone;
                        Email = query.Email;
                        Address = query.Address;
                        MapLink = query.MapLink;
                        ShortAboutUs = query.ShortAboutUs;
                        AboutUs = query.AboutUs;
                        ReserveDay = query.ReserveDay.ToString();
                        MaxUserReserve = query.MaxUserReserve.ToString();
                        ReserveAgain = query.ReserveAgain.ToString();
                        Liability = query.Liability.ToString();
                        SiteStatus = query.Status == true;
                        CanRes = query.CanRes == true;
                    }
                }
            }
            catch (Exception ex)
            {
                Message = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
                MessageVisible = true;
            }

            return Page();
        }

        public IActionResult OnPost(string txtSiteName, string txtTelephone, string txtEmail,
            string txtAddress, string txtMapLink, string txtShortAboutUs, string txtAboutUs,
            string txtReserveDay, string txtMaxUserReserve, string txtReserveAgain,
            string txtLiability, bool chkSiteStatus, bool chkres)
        {
            // cr-dotnet-1031 (Line 100): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            try
            {
                using (var EF = new ULSDBEntities())
                {
                    var query = (from table in EF.ULSTbl_SiteSetting
                                 where table.ID != 0
                                 select table).FirstOrDefault();

                    // cr-dotnet-0046 (Lines 58-62): Replaced System.Web.HttpRuntime.Cache.Remove() calls
                    // (IIS in-memory cache invalidation) with IDistributedCache.Remove() backed by
                    // Amazon ElastiCache Redis. Cache invalidation is now propagated to all pods
                    // sharing the same Redis cluster, eliminating cache inconsistency.
                    _cache.Remove("SiteName");
                    _cache.Remove("SiteStatus");
                    _cache.Remove("CanRes");
                    _cache.Remove("ShortAboutUs");
                    _cache.Remove("GetContactUSPage");

                    if (query != null)
                    {
                        query.SiteName = txtSiteName;
                        query.Telephone = txtTelephone;
                        query.Email = txtEmail;
                        query.Address = txtAddress;
                        query.MapLink = txtMapLink;
                        query.ShortAboutUs = txtShortAboutUs;
                        query.AboutUs = txtAboutUs;
                        query.ReserveDay = int.Parse(txtReserveDay);
                        query.MaxUserReserve = int.Parse(txtMaxUserReserve);
                        query.ReserveAgain = int.Parse(txtReserveAgain);
                        query.Liability = long.Parse(txtLiability);
                        query.Status = chkSiteStatus;
                        query.CanRes = chkres;
                        EF.SaveChanges();
                    }
                    else
                    {
                        var newSetting = new ULSTbl_SiteSetting
                        {
                            SiteName = txtSiteName,
                            Telephone = txtTelephone,
                            Email = txtEmail,
                            Address = txtAddress,
                            MapLink = txtMapLink,
                            ShortAboutUs = txtShortAboutUs,
                            AboutUs = txtAboutUs,
                            ReserveDay = int.Parse(txtReserveDay),
                            MaxUserReserve = int.Parse(txtMaxUserReserve),
                            ReserveAgain = int.Parse(txtReserveAgain),
                            Liability = long.Parse(txtLiability),
                            Status = chkSiteStatus
                        };
                        EF.ULSTbl_SiteSetting.Add(newSetting);
                        EF.SaveChanges();
                    }
                }
            }
            catch (Exception ex)
            {
                Message = Components.alert("خطا:" + ex.Message, Components.AlertStyle.danger);
                MessageVisible = true;
                return Page();
            }

            Message = Components.alert("تنظیمات شما با موفقیت ذخیره شد", Components.AlertStyle.success);
            MessageVisible = true;

            // Reload settings to display updated values
            return OnGet();
        }
    }
}
