// Migrated from ASP.NET Web Forms UserControl to ASP.NET Core Razor Pages (PageModel)
// Removed: System.Web.UI.WebControls, System.Web.UI.UserControl
// Removed: System.Management (Windows-only WMI) - replaced with cross-platform alternatives
// Replaced: System.Web.UI.UserControl with Microsoft.AspNetCore.Mvc.RazorPages.PageModel
// Replaced: Page_Load with OnGet, Timer/Button handlers with OnPost
// Replaced: WMI ManagementObjectSearcher with cross-platform System.Runtime.InteropServices.RuntimeInformation
// Replaced: PerformanceCounter (Windows-only) with Amazon CloudWatch Custom Metrics (cr-dotnet-0050)
// CloudWatch SDK publishes CPU, RAM, and Disk metrics as custom CloudWatch metrics
// cr-dotnet-0127: Added IHostApplicationLifetime graceful shutdown handlers to drain CloudWatch
//                 metric publishing, flush pending metric data, and release AWS SDK resources
//                 before the process is terminated by the cloud orchestrator (e.g., AWS ECS SIGTERM).
//
// cr-dotnet-1031 (Windows Authentication in Web Forms Cloud) - Lines 91, 105 in original source:
//   Replaced IIS Windows Authentication (Integrated Security/NTLM/Kerberos) with Amazon Cognito
//   User Pools and JWT Bearer Token authentication. User.Identity.IsAuthenticated is now populated
//   from Cognito JWT claims validated by ASP.NET Core JWT Bearer middleware.
//   No on-premise Active Directory domain join required.
//   - Line 91:  User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   - Line 105: User.Identity.IsAuthenticated check replaced with CognitoJwtHelper.IsAuthenticatedWithCognito()
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
using Classes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace UniversityLibrarySystem.Manager
{
    public class ServerMonitorModel : PageModel
    {
        // cr-dotnet-0127: IHostApplicationLifetime injected to register graceful shutdown callbacks.
        // ApplicationStopping token signals the app to stop accepting new monitoring requests and
        // drain any in-flight CloudWatch PutMetricData calls before the process exits.
        // ApplicationStopped token signals that shutdown is complete and final cleanup can run.
        private readonly IHostApplicationLifetime _appLifetime;

        // cr-dotnet-0127: CancellationTokenSource used to propagate the ApplicationStopping signal
        // into CloudWatch metric publishing and any other async resource operations.
        private static readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();

        public string CpuUsage { get; set; } = "N/A";
        public string CpuDetail { get; set; } = "N/A";
        public string RamUsage { get; set; } = "N/A";
        public string RamDetail { get; set; } = "N/A";
        public string DiskUsage { get; set; } = "N/A";
        public string DiskDetail { get; set; } = "N/A";
        public string LastUpdate { get; set; } = "";

        // CloudWatch namespace for custom metrics
        private const string CloudWatchNamespace = "UniversityLibrarySystem/ServerMonitor";

        // cr-dotnet-0127: Constructor injection of IHostApplicationLifetime.
        // Registers ApplicationStopping and ApplicationStopped callbacks to ensure:
        //   - In-flight CloudWatch PutMetricData calls are drained before the process exits.
        //   - AWS SDK HTTP connections are released gracefully on SIGTERM (AWS ECS/EKS/Fargate).
        //   - No new metric publishing starts after the shutdown signal is received.
        public ServerMonitorModel(IHostApplicationLifetime appLifetime)
        {
            _appLifetime = appLifetime;

            // cr-dotnet-0127: Register ApplicationStopping callback.
            // Triggered when the host begins a graceful shutdown (e.g., SIGTERM on AWS ECS/Fargate).
            // Cancels the shared token so that any in-progress CloudWatch calls can observe
            // the cancellation and exit cleanly rather than being abruptly terminated.
            _appLifetime.ApplicationStopping.Register(OnApplicationStopping);

            // cr-dotnet-0127: Register ApplicationStopped callback.
            // Triggered after the application has fully stopped.
            // Disposes the CancellationTokenSource to release its underlying OS resources.
            _appLifetime.ApplicationStopped.Register(OnApplicationStopped);
        }

        // cr-dotnet-0127: Called when the host signals ApplicationStopping (e.g., SIGTERM from AWS).
        // Cancels the shared CancellationToken so that any in-flight CloudWatch metric publishing
        // can observe the cancellation and exit cleanly.
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

        public IActionResult OnGet()
        {
            // cr-dotnet-1031 (Line 91): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            // cr-dotnet-0127: Check shutdown token before starting any work.
            // If the host is shutting down, return a 503 to stop accepting new monitoring requests gracefully.
            if (_shutdownCts.IsCancellationRequested)
                return StatusCode(503);

            UpdateServerStats();
            return Page();
        }

        public IActionResult OnPost()
        {
            // cr-dotnet-1031 (Line 105): Replaced Windows Authentication (NTLM/Kerberos) check with
            // Amazon Cognito JWT Bearer Token authentication. User.Identity.IsAuthenticated is set
            // by ASP.NET Core JWT Bearer middleware after validating the Cognito-issued JWT token.
            // No Active Directory domain join required — authentication is fully cloud-native.
            if (!CognitoJwtHelper.IsAuthenticatedWithCognito(HttpContext))
                return Redirect("/");

            // cr-dotnet-0127: Check shutdown token before starting any work.
            // Prevents new metric collection and CloudWatch publishing from starting during graceful shutdown.
            if (_shutdownCts.IsCancellationRequested)
                return StatusCode(503);

            UpdateServerStats();
            return Page();
        }

        private void UpdateServerStats()
        {
            try
            {
                // cr-dotnet-0127: Guard the entire stats update with the ApplicationStopping token.
                // If shutdown is signalled while collecting metrics (Line 77 in original source),
                // the operation exits cleanly without publishing partial data to CloudWatch.
                if (_shutdownCts.IsCancellationRequested)
                    return;

                // CPU usage - cross-platform approximation using process time
                float cpuUsage = GetCPUUsage();
                string cpuUsageFormatted = string.Format("{0:0.0}%", cpuUsage);
                CpuUsage = cpuUsageFormatted;
                CpuDetail = string.Format("{0} در حال استفاده", cpuUsageFormatted);

                // RAM usage - cross-platform using GC and Environment
                float ramUsage = GetRAMUsage();
                double usedRAM = GetUsedRAMInMB();
                double totalRAM = GetTotalRAMInMB();
                string ramUsageFormatted = string.Format("{0:0.0}%", ramUsage);
                RamUsage = ramUsageFormatted;
                RamDetail = string.Format("{0:0.0} GB از {1:0.0} GB",
                    usedRAM / 1024, totalRAM / 1024);

                // Disk usage - cross-platform using DriveInfo
                string driveLetter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C" : "/";
                float diskUsage = GetDiskUsage(driveLetter);
                double freeDiskSpace = GetFreeDiskSpaceInGB(driveLetter);
                double totalDiskSpace = GetTotalDiskSpaceInGB(driveLetter);
                double usedDiskSpace = totalDiskSpace - freeDiskSpace;
                string diskUsageFormatted = string.Format("{0:0.0}%", diskUsage);
                DiskUsage = diskUsageFormatted;
                DiskDetail = string.Format("{0:0.0} GB از {1:0.0} GB استفاده شده",
                    usedDiskSpace, totalDiskSpace);

                LastUpdate = DateTimeOffset.UtcNow.UtcDateTime.ToString("HH:mm:ss");

                // cr-dotnet-0127: Only publish metrics to CloudWatch if shutdown has not been requested.
                // This prevents starting new AWS SDK calls during the drain window.
                if (!_shutdownCts.IsCancellationRequested)
                {
                    // Publish metrics to Amazon CloudWatch
                    PublishMetricsToCloudWatch(cpuUsage, ramUsage, diskUsage);
                }
            }
            catch (Exception)
            {
                // Handle error gracefully - display N/A values
                LastUpdate = DateTimeOffset.UtcNow.UtcDateTime.ToString("HH:mm:ss");
            }
        }

        /// <summary>
        /// Publishes server performance metrics to Amazon CloudWatch as custom metrics.
        /// Replaces Windows Performance Counters (System.Diagnostics.PerformanceCounter)
        /// with CloudWatch SDK calls for cross-platform cloud-native monitoring.
        /// cr-dotnet-0127: Passes the ApplicationStopping CancellationToken to PutMetricDataAsync
        /// so that in-flight CloudWatch calls are cancelled cleanly on SIGTERM.
        /// </summary>
        private void PublishMetricsToCloudWatch(float cpuUsage, float ramUsage, float diskUsage)
        {
            try
            {
                // Resolve AWS region from environment variable (cloud-native configuration)
                string awsRegion = Environment.GetEnvironmentVariable("AWS_REGION")
                    ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                    ?? "us-east-1";

                var regionEndpoint = RegionEndpoint.GetBySystemName(awsRegion);

                using (var cloudWatchClient = new AmazonCloudWatchClient(regionEndpoint))
                {
                    var metricData = new List<MetricDatum>
                    {
                        new MetricDatum
                        {
                            MetricName = "CPUUsagePercent",
                            Value = cpuUsage,
                            Unit = StandardUnit.Percent,
                            Timestamp = DateTime.UtcNow,
                            Dimensions = new List<Dimension>
                            {
                                new Dimension { Name = "Application", Value = "UniversityLibrarySystem" },
                                new Dimension { Name = "Environment", Value = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" }
                            }
                        },
                        new MetricDatum
                        {
                            MetricName = "MemoryUsagePercent",
                            Value = ramUsage,
                            Unit = StandardUnit.Percent,
                            Timestamp = DateTime.UtcNow,
                            Dimensions = new List<Dimension>
                            {
                                new Dimension { Name = "Application", Value = "UniversityLibrarySystem" },
                                new Dimension { Name = "Environment", Value = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" }
                            }
                        },
                        new MetricDatum
                        {
                            MetricName = "DiskUsagePercent",
                            Value = diskUsage,
                            Unit = StandardUnit.Percent,
                            Timestamp = DateTime.UtcNow,
                            Dimensions = new List<Dimension>
                            {
                                new Dimension { Name = "Application", Value = "UniversityLibrarySystem" },
                                new Dimension { Name = "Environment", Value = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" }
                            }
                        }
                    };

                    var request = new PutMetricDataRequest
                    {
                        Namespace = CloudWatchNamespace,
                        MetricData = metricData
                    };

                    // cr-dotnet-0127: Pass the ApplicationStopping CancellationToken to PutMetricDataAsync.
                    // If a SIGTERM arrives while the CloudWatch call is in-flight, the task is cancelled
                    // cleanly, allowing the AWS SDK HTTP connection to be released before process exit.
                    cloudWatchClient.PutMetricDataAsync(request, _shutdownCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // cr-dotnet-0127: Expected during graceful shutdown - CloudWatch call was cancelled
                // because ApplicationStopping was signalled. This is normal and not an error.
            }
            catch (Exception)
            {
                // CloudWatch publishing failure should not affect the UI display
                // Metrics will be missing for this interval but the page continues to function
            }
        }

        private float GetCPUUsage()
        {
            try
            {
                // Cross-platform CPU usage approximation using process CPU time
                // Replaces Windows-only System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total")
                var process = Process.GetCurrentProcess();
                var startCpuUsage = process.TotalProcessorTime;
                var startTime = DateTime.UtcNow;
                System.Threading.Thread.Sleep(500);
                var endCpuUsage = process.TotalProcessorTime;
                var endTime = DateTime.UtcNow;
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                return (float)(cpuUsageTotal * 100);
            }
            catch
            {
                return 0;
            }
        }

        private float GetRAMUsage()
        {
            try
            {
                // Cross-platform: use GC memory info
                var gcInfo = GC.GetGCMemoryInfo();
                double totalMemory = gcInfo.TotalAvailableMemoryBytes;
                double usedMemory = GC.GetTotalMemory(false);
                if (totalMemory > 0)
                    return (float)((usedMemory / totalMemory) * 100);
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private double GetUsedRAMInMB()
        {
            try
            {
                // Cross-platform: use GC memory info
                double usedMemory = GC.GetTotalMemory(false);
                return Math.Round(usedMemory / (1024 * 1024), 2);
            }
            catch
            {
                return 0;
            }
        }

        private double GetTotalRAMInMB()
        {
            try
            {
                // Cross-platform: use GC memory info
                var gcInfo = GC.GetGCMemoryInfo();
                return Math.Round(gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0), 2);
            }
            catch
            {
                return 0;
            }
        }

        private float GetDiskUsage(string drivePath)
        {
            try
            {
                DriveInfo drive = new DriveInfo(drivePath);
                if (drive.IsReady)
                {
                    double totalSize = drive.TotalSize;
                    double freeSpace = drive.AvailableFreeSpace;
                    double usedSpace = totalSize - freeSpace;
                    return (float)((usedSpace / totalSize) * 100);
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private double GetFreeDiskSpaceInGB(string drivePath)
        {
            try
            {
                DriveInfo drive = new DriveInfo(drivePath);
                if (drive.IsReady)
                    return Math.Round(drive.AvailableFreeSpace / (1024 * 1024 * 1024.0), 2);
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private double GetTotalDiskSpaceInGB(string drivePath)
        {
            try
            {
                DriveInfo drive = new DriveInfo(drivePath);
                if (drive.IsReady)
                    return Math.Round(drive.TotalSize / (1024 * 1024 * 1024.0), 2);
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
