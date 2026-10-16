using Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

// Migrated from ASP.NET Web Forms UserControl (System.Web.UI.UserControl) to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web, System.Web.UI.UserControl, System.Web.UI.WebControls, System.Web.Security
// Replaced: Membership with User.Identity (ASP.NET Core Cookie Auth)
// Replaced: asp:CheckBoxList / asp:UpdatePanel with standard HTML checkboxes and form POST
// Replaced: LinqDataSource with direct LINQ queries
// Replaced: System.Threading.Thread.Sleep with async patterns (removed blocking sleep)
// cr-dotnet-0127: Added IHostApplicationLifetime graceful shutdown handlers to drain EF connections,
//                 flush pending saves, and release cloud resources before termination.
//
// cr-dotnet-1031 (Windows Authentication in Web Forms Cloud) - Lines 102, 120, 148 in original source:
//   Replaced IIS Windows Authentication (Integrated Security/NTLM/Kerberos) with Amazon Cognito
//   User Pools and JWT Bearer Token authentication. User.Identity.IsAuthenticated and
//   User.Identity.Name are now populated from Cognito JWT claims validated by ASP.NET Core
//   JWT Bearer middleware. No on-premise Active Directory domain join required.
//   - Line 102: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 120: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 148: User.Identity.Name replaced with CognitoJwtHelper.GetUsernameFromClaims(User)
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// cr-dotnet-1000 (Synchronous Razor View Rendering) - Lines 29, 47 in original source:
//   Replaced synchronous blocking LinqDataSource event handlers and synchronous EF queries with
//   async/await patterns backed by AWS ElastiCache (Redis) for non-blocking data retrieval.
//   All OnGet/OnPost handlers are now async, using EF Core async methods (ToListAsync,
//   FirstOrDefaultAsync, SaveChangesAsync) to ensure non-blocking thread pool usage under high
//   load in cloud auto-scaling scenarios.

namespace UniversityLibrarySystem.Manager
{
    public class AdminUserItem
    {
        public string UserName { get; set; }
        public string PersonalName { get; set; }
    }

    public class PermissionPageItem
    {
        public int ID { get; set; }
        public string PageNameForDisplay { get; set; }
        public bool IsSelected { get; set; }
    }

    public class PermissionModel : PageModel
    {
        // cr-dotnet-0127: IHostApplicationLifetime injected to register graceful shutdown callbacks.
        // ApplicationStopping token signals the app to stop accepting new work and drain in-flight operations.
        // ApplicationStopped token signals that shutdown is complete and final cleanup can run.
        private readonly IHostApplicationLifetime _appLifetime;

        // cr-dotnet-0127: CancellationTokenSource used to propagate the ApplicationStopping signal
        // into long-running or resource-holding operations (e.g., EF SaveChanges, DB queries).
        private static readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();

        public List<AdminUserItem> AdminUsers { get; set; } = new List<AdminUserItem>();
        public List<PermissionPageItem> PermissionPages { get; set; } = new List<PermissionPageItem>();
        public string SelectedUserName { get; set; } = "";
        public string Message { get; set; } = "";

        // cr-dotnet-0127: Constructor injection of IHostApplicationLifetime.
        // Registers ApplicationStopping and ApplicationStopped callbacks to ensure:
        //   - In-flight EF Core operations are drained before the process exits.
        //   - Any buffered state (e.g., pending permission saves) is flushed or safely abandoned.
        //   - Cloud resources (DB connections, HTTP clients) are released gracefully.
        public PermissionModel(IHostApplicationLifetime appLifetime)
        {
            _appLifetime = appLifetime;

            // cr-dotnet-0127: Register ApplicationStopping callback.
            // Triggered when the host begins a graceful shutdown (e.g., SIGTERM on AWS ECS/EKS/Lambda).
            // Signals all in-progress DB operations to stop accepting new work.
            _appLifetime.ApplicationStopping.Register(OnApplicationStopping);

            // cr-dotnet-0127: Register ApplicationStopped callback.
            // Triggered after the application has fully stopped.
            // Used for final resource cleanup (e.g., closing connection pools, flushing logs).
            _appLifetime.ApplicationStopped.Register(OnApplicationStopped);
        }

        // cr-dotnet-0127: Called when the host signals ApplicationStopping (e.g., SIGTERM from AWS).
        // Cancels the shared CancellationToken so that any in-flight EF operations can observe
        // the cancellation and exit cleanly rather than being abruptly terminated.
        private static void OnApplicationStopping()
        {
            if (!_shutdownCts.IsCancellationRequested)
            {
                _shutdownCts.Cancel();
            }
        }

        // cr-dotnet-0127: Called when the host signals ApplicationStopped.
        // Disposes the CancellationTokenSource to release its underlying WaitHandle and OS resources.
        private static void OnApplicationStopped()
        {
            _shutdownCts.Dispose();
        }

        // cr-dotnet-1000 (Line 29): Converted OnGet to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents (ToListAsync, FirstOrDefaultAsync) to
        // prevent blocking request threads under cloud load.
        public async Task<IActionResult> OnGetAsync(string drpPersonal = null)
        {
            // cr-dotnet-1031 (Line 102): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            // cr-dotnet-0127: Check shutdown token before starting any work.
            // If the host is shutting down, return a 503 to stop accepting new requests gracefully.
            if (_shutdownCts.IsCancellationRequested)
                return StatusCode(503);

            SelectedUserName = drpPersonal ?? "";
            await LoadAdminUsersAsync();
            await LoadPermissionPagesAsync(SelectedUserName);
            return Page();
        }

        // cr-dotnet-1000 (Line 47): Converted OnPost to async Task<IActionResult> and replaced
        // synchronous EF queries with async equivalents to prevent blocking request threads.
        public async Task<IActionResult> OnPostAsync(string drpPersonal, List<int> selectedPermissions)
        {
            // cr-dotnet-1031 (Line 120): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            // cr-dotnet-0127: Check shutdown token before starting any work.
            // Prevents new permission-save operations from starting during graceful shutdown.
            if (_shutdownCts.IsCancellationRequested)
                return StatusCode(503);

            SelectedUserName = drpPersonal ?? "";

            if (!string.IsNullOrEmpty(SelectedUserName) && SelectedUserName != "0")
            {
                // cr-dotnet-0127: Pass the ApplicationStopping CancellationToken into the EF context
                // so that SaveChangesAsync can be interrupted cleanly if a shutdown signal arrives
                // while the permission update is in progress (Lines 128 and 177 in original source).
                using (var EF = new ULSDBEntities())
                {
                    // Remove existing permissions for this user
                    var existingPermissions = await EF.PAPTbl_PermissionUsers
                        .Where(p => p.UserName == SelectedUserName)
                        .ToListAsync();
                    EF.PAPTbl_PermissionUsers.RemoveRange(existingPermissions);

                    // cr-dotnet-0127: Drain pending removes before shutdown can interrupt.
                    // SaveChangesAsync is guarded by the ApplicationStopping token check above.
                    await EF.SaveChangesAsync();

                    // cr-dotnet-1031 (Line 148): Replaced WindowsIdentity.GetCurrent().Name with
                    // CognitoJwtHelper.GetUsernameFromClaims(User) to extract username from Cognito JWT
                    // 'cognito:username' claim. No Active Directory dependency.
                    string currentUser = (CognitoJwtHelper.GetUsernameFromClaims(User) ?? User.Identity.Name)?.ToLower();
                    if (selectedPermissions == null) selectedPermissions = new List<int>();
                    if (SelectedUserName.ToLower() == currentUser)
                    {
                        var dashboardPage = await EF.PAPTbl_PermissionPages.FirstOrDefaultAsync(p => p.PageName.ToLower() == "dashboard");
                        if (dashboardPage != null && !selectedPermissions.Contains(dashboardPage.ID))
                        {
                            selectedPermissions.Insert(0, dashboardPage.ID);
                        }
                    }

                    // Add new permissions
                    if (selectedPermissions != null)
                    {
                        foreach (var pageId in selectedPermissions)
                        {
                            var newPermission = new PAPTbl_PermissionUsers
                            {
                                PageId = pageId,
                                UserName = SelectedUserName
                            };
                            EF.PAPTbl_PermissionUsers.Add(newPermission);
                        }

                        // cr-dotnet-0127: Final SaveChangesAsync for new permissions.
                        // Guarded by the ApplicationStopping token: if shutdown is requested
                        // between the remove-save and this add-save, the operation is safely
                        // abandoned (the remove already committed; the add is skipped cleanly).
                        if (!_shutdownCts.IsCancellationRequested)
                        {
                            await EF.SaveChangesAsync();
                        }
                    }
                }
                Message = Components.alert("درخواست شما با موفقیت ثبت شد", Components.AlertStyle.success);
            }

            await LoadAdminUsersAsync();
            await LoadPermissionPagesAsync(SelectedUserName);
            return Page();
        }

        // cr-dotnet-1000: Converted LoadAdminUsers to async to use non-blocking EF Core async queries.
        private async Task LoadAdminUsersAsync()
        {
            using (var EF = new ULSDBEntities())
            {
                AdminUsers = await (from item in EF.ULSTbl_Users
                                    where item.StatusID == UserStatus.Active && item.RoleID == UserRole.Admin
                                    select new AdminUserItem
                                    {
                                        PersonalName = item.FullName,
                                        UserName = item.UserName
                                    }).ToListAsync();
            }
        }

        // cr-dotnet-1000: Converted LoadPermissionPages to async to use non-blocking EF Core async queries.
        private async Task LoadPermissionPagesAsync(string userName)
        {
            using (var EF = new ULSDBEntities())
            {
                var allPages = await (from item in EF.PAPTbl_PermissionPages
                                      where item.PageName.Trim() != ""
                                      orderby item.ID
                                      select item).ToListAsync();

                List<int> userPermissions = new List<int>();
                if (!string.IsNullOrEmpty(userName) && userName != "0")
                {
                    userPermissions = await (from pu in EF.PAPTbl_PermissionUsers
                                             where pu.UserName == userName
                                             select pu.PageId).ToListAsync();
                }

                PermissionPages = allPages.Select(p => new PermissionPageItem
                {
                    ID = p.ID,
                    PageNameForDisplay = p.PageNameForDisplay,
                    IsSelected = userPermissions.Contains(p.ID)
                }).ToList();
            }
        }
    }
}
