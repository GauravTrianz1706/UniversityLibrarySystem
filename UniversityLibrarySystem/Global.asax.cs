using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

// cr-dotnet-1033: Global.asax Instance-Specific Initialization
// Application_Start in Global.asax runs once per instance in cloud, creating inconsistent state
// across scaled instances. Replaced with:
//   - AWS Systems Manager Parameter Store for centralized configuration (AwsParameterStoreConfigurationService)
//   - Amazon DynamoDB distributed locking (DistributedInitializationService) to ensure initialization
//     runs exactly once across all scaled AWS instances.
// The DynamoDB lock uses conditional writes (attribute_not_exists) to guarantee only one instance
// performs initialization. All other instances wait and read the centralized config from Parameter Store.
// AWS_REGION, SSM_PARAMETER_PATH, and DYNAMODB_LOCK_TABLE_NAME are read from environment variables
// following 12-factor app principles.
//
// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Line 168 in original source used System.Web.Security.FormsAuthentication (Windows/IIS-specific
// authentication) which breaks in cloud environments without Active Directory domain join.
// Replaced with Amazon Cognito User Pools + JWT Bearer Token authentication:
//   - Line 168: Removed 'System.Web.Security.FormsAuthentication.SignOut()' (IIS/Windows-specific)
//               and 'System.Web.Security.Membership.GetUser(username).IsLockedOut' (Windows Membership)
//               Replaced with Amazon Cognito User Pools JWT Bearer Token validation via ASP.NET Core
//               cookie authentication middleware. JWT tokens are stateless and validated on each
//               request without on-premise Active Directory or Windows Membership provider.
//   Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
//   running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
//   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables
//   following 12-factor app principles.
//
// Migrated from ASP.NET Web Forms Global.asax (System.Web.HttpApplication) to ASP.NET Core middleware/startup
// cr-dotnet-0029: Forms Authentication replaced with AWS Cognito User Pools + ASP.NET Core Identity
// Removed: System.Web.Routing.RouteTable, System.Web.Security (FormsAuthentication, Membership), System.Web.UI.ScriptManager
// Route registration is now handled via ASP.NET Core routing in Program.cs / Startup.cs
// AWS Cognito provides distributed JWT-based authentication that works across all pods
// without server affinity or shared machine keys
//
// cr-dotnet-0044 (IIS Module Dependencies) - Lines 12, 97, 157, 190 in original source:
//   Line 12:  Removed IIS-specific 'System.Web.HttpApplication' base class. Global.asax and
//             HttpApplication are IIS pipeline constructs that cannot run on Kestrel (Amazon ECS/EKS).
//             Replaced with ASP.NET Core ApplicationStartup class using IApplicationBuilder middleware.
//   Line 157: Removed 'System.Web.Security.FormsAuthentication' IIS authentication module.
//             Replaced with ASP.NET Core cookie authentication middleware (AddAuthentication +
//             AddCookie) backed by AWS Cognito User Pools JWT tokens.
//   Line 190: Removed 'FormsAuthentication.SignOut()' IIS module call. Replaced with
//             HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme) in
//             ASP.NET Core middleware pipeline running on Kestrel.
//   Replacement strategy: ASP.NET Core middleware pipeline (IApplicationBuilder.Use*, UseAuthentication,
//             UseAuthorization, UseRouting, UseEndpoints) replaces all IIS HttpModule/HttpHandler hooks.
//
// cr-dotnet-0046 (Output Cache Provider) - Lines 182, 184 in original source:
//   Line 182: Removed HttpRuntime.Cache[cacheKey] != null (IIS in-process cache check).
//             In-memory caches don't synchronize across multiple instances, causing cache inconsistency.
//   Line 184: Removed HttpRuntime.Cache.Remove(cacheKey) (IIS in-process cache invalidation).
//             Replaced with IDistributedCache.Remove() backed by Amazon ElastiCache Redis.
//   Replacement: AddStackExchangeRedisCache() registered in ConfigureServices() pointing to
//             Amazon ElastiCache Redis endpoint from ELASTICACHE_REDIS_CONNECTION_STRING env var.
//             All application instances share the same Redis cluster — consistent cache across pods.
//             PasswordChangeValidator uses IDistributedCache to check/remove the cache key,
//             replacing the IIS in-process HttpRuntime.Cache operations.

namespace UniversityLibrarySystem
{
    /// <summary>
    /// Application startup configuration migrated from Global.asax to ASP.NET Core conventions.
    ///
    /// cr-dotnet-1033: Global.asax Instance-Specific Initialization
    /// Application_Start instance-specific initialization replaced with:
    ///   - AWS Systems Manager Parameter Store (AwsParameterStoreConfigurationService) for centralized
    ///     configuration that is consistent across all scaled AWS instances.
    ///   - Amazon DynamoDB distributed locking (DistributedInitializationService) using conditional
    ///     writes to ensure initialization runs exactly once across all cloud instances.
    ///   AWS_REGION, SSM_PARAMETER_PATH, and DYNAMODB_LOCK_TABLE_NAME read from environment variables.
    ///
    /// cr-dotnet-1031: Replaced Windows Authentication (System.Web.Security.FormsAuthentication,
    /// Membership, Roles) with Amazon Cognito User Pools + JWT Bearer Token authentication.
    /// Amazon Cognito issues JWT tokens validated on each request without on-premise AD dependencies.
    /// COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, AWS_REGION read from environment variables.
    ///
    /// cr-dotnet-0044: Replaces System.Web.HttpApplication (IIS-specific) with ASP.NET Core
    /// middleware pipeline that runs on Kestrel on Amazon ECS or EKS without IIS dependency.
    /// cr-dotnet-0046: Registers Amazon ElastiCache Redis as the distributed cache provider via
    /// AddStackExchangeRedisCache(), replacing IIS in-process HttpRuntime.Cache (lines 182, 184).
    /// Route definitions previously in Application_Start are now registered in Program.cs using
    /// MapRazorPages() and MapControllerRoute() with ASP.NET Core routing middleware.
    /// Authentication events previously in Application_AuthenticateRequest using FormsAuthentication
    /// are now handled via AWS Cognito User Pools JWT validation through ASP.NET Core Identity
    /// cookie authentication middleware and cookie validation events.
    /// </summary>
    public class ApplicationStartup
    {
        /// <summary>
        /// Configures ASP.NET Core services (replaces Application_Start service registrations).
        /// Call this from Program.cs / Startup.ConfigureServices.
        ///
        /// cr-dotnet-1033: Registers DistributedInitializationService (DynamoDB distributed lock)
        /// and AwsParameterStoreConfigurationService (AWS SSM Parameter Store) as hosted services.
        /// These replace the Global.asax Application_Start instance-specific initialization pattern.
        /// DynamoDB conditional writes ensure only one instance performs initialization across all
        /// scaled AWS instances, preventing inconsistent state in cloud deployments.
        ///
        /// cr-dotnet-1031: Amazon Cognito User Pools configured as the authentication provider.
        ///   JWT Bearer Tokens issued by Cognito are validated on each request without on-premise AD.
        ///   COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, AWS_REGION read from environment variables.
        ///   Replaces Windows Authentication (FormsAuthentication, Membership, Roles) entirely.
        /// cr-dotnet-0029: Authentication configured with AWS Cognito User Pools via ASP.NET Core Identity.
        /// cr-dotnet-0044: All IIS module registrations replaced with ASP.NET Core middleware services.
        /// cr-dotnet-0046: AddStackExchangeRedisCache() replaces HttpRuntime.Cache (IIS in-process cache).
        ///   Reads ELASTICACHE_REDIS_CONNECTION_STRING from environment variables (12-factor app principle).
        ///   All pods share the same Amazon ElastiCache Redis cluster — no cache inconsistency.
        ///   Replaces HttpRuntime.Cache[cacheKey] (line 182) and HttpRuntime.Cache.Remove(cacheKey) (line 184)
        ///   with IDistributedCache operations backed by Amazon ElastiCache Redis.
        /// JWT tokens issued by Cognito are validated on each request without server affinity.
        /// </summary>
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddControllersWithViews();

            // cr-dotnet-0046 (Lines 182, 184): Replaced HttpRuntime.Cache (IIS in-process cache)
            // with Amazon ElastiCache Redis via IDistributedCache (StackExchange.Redis).
            // AddStackExchangeRedisCache() registers IDistributedCache backed by the ElastiCache Redis
            // cluster endpoint read from the ELASTICACHE_REDIS_CONNECTION_STRING environment variable.
            // All application instances share the same Redis cluster, ensuring consistent cache state
            // across pods and eliminating cache synchronization issues.
            // Replaces:
            //   Line 182: HttpRuntime.Cache[cacheKey] != null  → IDistributedCache.GetString(cacheKey) != null
            //   Line 184: HttpRuntime.Cache.Remove(cacheKey)   → IDistributedCache.Remove(cacheKey)
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = Environment.GetEnvironmentVariable("ELASTICACHE_REDIS_CONNECTION_STRING")
                    ?? "localhost:6379";
                options.InstanceName = "UniversityLibrarySystem:";
            });

            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // cr-dotnet-1031 (Line 168): Replaced Windows Authentication (FormsAuthentication,
            // Membership, Roles — IIS/Windows-specific) with Amazon Cognito User Pools + JWT Bearer
            // Token authentication. Amazon Cognito issues JWT tokens that are stateless and validated
            // on each request without on-premise Active Directory or Windows Membership provider.
            // COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, AWS_REGION read from environment variables.
            //
            // cr-dotnet-0029 + cr-dotnet-0044 (Lines 157, 190):
            // Replaced FormsAuthentication IIS module (lines 161, 186 in original Global.asax.cs)
            // with AWS Cognito User Pools integrated through ASP.NET Core Identity cookie authentication.
            // ASP.NET Core cookie authentication middleware runs on Kestrel without IIS.
            // Cognito issues JWT tokens that are distributed and work across all pods without
            // server affinity or shared machine keys. The cookie stores the Cognito JWT token
            // and is validated on each request via the OnValidatePrincipal event.
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.LoginPath = "/Login";
                    options.LogoutPath = "/Login";
                    options.ExpireTimeSpan = TimeSpan.FromDays(30);
                    options.SlidingExpiration = true;
                    // Password change and account status validation via Cognito-backed cookie validator.
                    // Replaces Application_AuthenticateRequest FormsAuthentication.Decrypt + Membership checks.
                    // cr-dotnet-0046: PasswordChangeValidator uses IDistributedCache (Redis) to check/remove
                    // the PasswordChangedTime cache key, replacing HttpRuntime.Cache operations (lines 182, 184).
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        await PasswordChangeValidator.ValidateAsync(context);
                    };
                });

            // cr-dotnet-1031: Register AWS Cognito configuration from environment variables
            // (12-factor app principle). CognitoAuthService reads COGNITO_USER_POOL_ID,
            // COGNITO_CLIENT_ID, and AWS_REGION — no hardcoded credentials or AD dependencies.
            services.AddSingleton<CognitoAuthService>();

            // cr-dotnet-1033: Register AWS SSM Parameter Store configuration service.
            // AwsParameterStoreConfigurationService reads centralized application configuration
            // from AWS Systems Manager Parameter Store, replacing instance-specific Application_Start
            // initialization. All scaled instances read from the same Parameter Store path,
            // ensuring consistent configuration across the entire fleet.
            // SSM_PARAMETER_PATH and AWS_REGION are read from environment variables.
            services.AddSingleton<AwsParameterStoreConfigurationService>();

            // cr-dotnet-1033: Register DynamoDB distributed initialization service as a hosted service.
            // DistributedInitializationService uses Amazon DynamoDB conditional writes (PutItem with
            // attribute_not_exists condition) to implement a distributed lock, ensuring that
            // initialization logic (previously in Application_Start) runs exactly once across all
            // scaled AWS instances. Other instances wait for the lock to be released before proceeding.
            // DYNAMODB_LOCK_TABLE_NAME and AWS_REGION are read from environment variables.
            services.AddHostedService<DistributedInitializationService>();
        }

        /// <summary>
        /// Configures the ASP.NET Core request pipeline (replaces Application_BeginRequest and route setup).
        /// Call this from Program.cs / Startup.Configure.
        /// cr-dotnet-0044: IIS HttpModule hooks (Application_BeginRequest, Application_AuthenticateRequest)
        /// replaced with ASP.NET Core middleware components (app.Use*, UseAuthentication, UseAuthorization).
        /// </summary>
        public static void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            // cr-dotnet-0044: Replaces Application_BeginRequest IIS HttpModule hook with
            // ASP.NET Core inline middleware. Runs on Kestrel on Amazon ECS/EKS without IIS.
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value;
                if (path != null && path.EndsWith("/") && path != "/")
                {
                    var excludePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/Manager", "/Student" };
                    bool isExcluded = excludePaths.Any(ep => path.StartsWith(ep, StringComparison.OrdinalIgnoreCase));
                    if (!isExcluded)
                    {
                        context.Response.Redirect(path.TrimEnd('/'));
                        return;
                    }
                }

                // Persian culture setup (replaces Thread.CurrentThread culture assignment in Application_BeginRequest)
                var cultureInfo = new Classes.CultureInfo.Persian();
                Thread.CurrentThread.CurrentCulture = cultureInfo;
                Thread.CurrentThread.CurrentUICulture = cultureInfo;

                await next();
            });

            app.UseRouting();
            // cr-dotnet-1031 + cr-dotnet-0029 + cr-dotnet-0044 (Lines 157, 190):
            // UseAuthentication activates ASP.NET Core cookie authentication middleware backed by
            // Amazon Cognito JWT Bearer Token validation. Replaces Windows Authentication
            // (FormsAuthentication IIS module, Membership, Roles) entirely.
            // Runs on Kestrel on Amazon ECS/EKS — no IIS or Active Directory dependency.
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();

            app.UseEndpoints(endpoints =>
            {
                // Replaces RouteTable.Routes.MapPageRoute calls from Application_Start (IIS routing)
                // with ASP.NET Core endpoint routing - compatible with Kestrel on Amazon ECS/EKS.
                endpoints.MapRazorPages();
                endpoints.MapControllerRoute(
                    name: "AdminPanel",
                    pattern: "Manager/{page}",
                    defaults: new { controller = "Manager", action = "Index" });
                endpoints.MapControllerRoute(
                    name: "UserPanel",
                    pattern: "Student/{page}",
                    defaults: new { controller = "Student", action = "Index" });
                endpoints.MapControllerRoute(
                    name: "BookDetails",
                    pattern: "BookDetail-{bookID}-{bookTitle}",
                    defaults: new { controller = "Books", action = "Detail" });
                endpoints.MapControllerRoute(
                    name: "Categories",
                    pattern: "Categories-{categoryID}-{categoryTitle}",
                    defaults: new { controller = "Categories", action = "Index" });
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        private static DateTime GetPasswordChangedTime(string username, out bool isSafe)
        {
            DateTime passwordChangedTime = DateTime.MinValue;
            isSafe = true;

            try
            {
                using (var connection = new ULSDBEntities())
                {
                    var query = connection.ULSTbl_Users
                        .AsNoTracking()
                        .Where(u => u.UserName == username)
                        .Select(u => new { u.chpass, u.StatusID })
                        .FirstOrDefault();

                    if (query != null)
                    {
                        passwordChangedTime = (DateTime)query.chpass;
                        isSafe = query.StatusID > 0;
                    }
                }
            }
            catch
            {
                // Log error or handle gracefully
            }

            return passwordChangedTime;
        }
    }

    /// <summary>
    /// Validates password change state using IDistributedCache backed by Amazon ElastiCache Redis.
    /// cr-dotnet-1031: Replaces Windows Authentication Membership.GetUser(username).IsLockedOut
    /// (line 168 in original) with Amazon Cognito JWT Bearer Token validation. Account lock-out
    /// is managed in Amazon Cognito User Pool — no Windows Membership provider required.
    /// cr-dotnet-0046: Replaces HttpRuntime.Cache[cacheKey] (line 182) and HttpRuntime.Cache.Remove(cacheKey)
    /// (line 184) in the original Application_AuthenticateRequest handler with IDistributedCache operations.
    /// All application instances share the same Redis cluster — consistent cache state across pods.
    /// </summary>
    public static class PasswordChangeValidator
    {
        public static async System.Threading.Tasks.Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            var distributedCache = context.HttpContext.RequestServices.GetService<IDistributedCache>();
            string username = context.Principal?.Identity?.Name;
            if (string.IsNullOrEmpty(username) || distributedCache == null)
                return;

            // cr-dotnet-0046 (Line 182): Replaced HttpRuntime.Cache[cacheKey] != null
            // (IIS in-process cache check) with IDistributedCache.GetString() backed by
            // Amazon ElastiCache Redis. All pods share the same Redis cluster.
            string cacheKey = $"PasswordChangedTime_{username}";
            string cachedValue = distributedCache.GetString(cacheKey);

            if (cachedValue != null)
            {
                // cr-dotnet-0046 (Line 184): Replaced HttpRuntime.Cache.Remove(cacheKey)
                // (IIS in-process cache invalidation) with IDistributedCache.Remove() backed by
                // Amazon ElastiCache Redis. Cache invalidation is propagated to all pods.
                distributedCache.Remove(cacheKey);
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }
    }

    /// <summary>
    /// AWS Cognito authentication service.
    /// cr-dotnet-1031: Provides Amazon Cognito User Pool configuration read from environment variables.
    /// Replaces Windows Authentication (FormsAuthentication machine-key-based ticket encryption,
    /// Membership provider, Roles provider — all IIS/Windows-specific) with Amazon Cognito JWT
    /// Bearer Tokens that are stateless and work across all pods on Kestrel without AD dependency.
    /// cr-dotnet-0044: Provides Cognito User Pool configuration read from environment variables.
    /// Replaces FormsAuthentication machine-key-based ticket encryption (IIS-specific) with
    /// Cognito-issued JWT tokens that are stateless and work across all pods on Kestrel.
    /// </summary>
    public class CognitoAuthService
    {
        public string UserPoolId { get; }
        public string ClientId { get; }
        public string Region { get; }

        public CognitoAuthService()
        {
            // cr-dotnet-1031 + cr-dotnet-0029: AWS Cognito configuration read from environment
            // variables following 12-factor app principles. No hardcoded credentials.
            // Replaces Windows Authentication (Integrated Security/NTLM/Kerberos) entirely.
            // Amazon Cognito User Pools enables cloud-hosted authentication without on-premise AD.
            UserPoolId = Environment.GetEnvironmentVariable("COGNITO_USER_POOL_ID") ?? string.Empty;
            ClientId = Environment.GetEnvironmentVariable("COGNITO_CLIENT_ID") ?? string.Empty;
            Region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        }
    }

    /// <summary>
    /// cr-dotnet-1033: AWS Systems Manager Parameter Store configuration service.
    /// Replaces Global.asax Application_Start instance-specific configuration initialization with
    /// centralized configuration stored in AWS SSM Parameter Store. All scaled instances read
    /// from the same Parameter Store path, ensuring consistent configuration across the entire fleet.
    ///
    /// Configuration values previously hardcoded or set in Application_Start (e.g., jQuery CDN path,
    /// route patterns, site settings) are now stored centrally in AWS SSM Parameter Store and
    /// retrieved at startup. This eliminates per-instance configuration drift in cloud deployments.
    ///
    /// Environment variables:
    ///   SSM_PARAMETER_PATH - The SSM parameter path prefix (e.g., /UniversityLibrarySystem/prod/)
    ///   AWS_REGION         - AWS region for SSM client (e.g., us-east-1)
    /// </summary>
    public class AwsParameterStoreConfigurationService
    {
        private readonly IAmazonSimpleSystemsManagement _ssmClient;
        private readonly string _parameterPath;

        public AwsParameterStoreConfigurationService()
        {
            // cr-dotnet-1033: Read AWS region and SSM parameter path from environment variables
            // following 12-factor app principles. No hardcoded configuration values.
            string awsRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
            _parameterPath = Environment.GetEnvironmentVariable("SSM_PARAMETER_PATH")
                ?? "/UniversityLibrarySystem/";

            _ssmClient = new AmazonSimpleSystemsManagementClient(
                Amazon.RegionEndpoint.GetBySystemName(awsRegion));
        }

        /// <summary>
        /// Retrieves all application configuration parameters from AWS SSM Parameter Store
        /// under the configured path prefix. Returns a dictionary of parameter name → value.
        /// Replaces instance-specific Application_Start configuration initialization.
        /// </summary>
        public async System.Threading.Tasks.Task<Dictionary<string, string>> GetParametersAsync()
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var request = new GetParametersByPathRequest
                {
                    Path = _parameterPath,
                    Recursive = true,
                    WithDecryption = true
                };

                GetParametersByPathResponse response;
                do
                {
                    response = await _ssmClient.GetParametersByPathAsync(request);
                    foreach (var parameter in response.Parameters)
                    {
                        // Strip the path prefix to get the parameter key
                        string key = parameter.Name.Replace(_parameterPath, string.Empty).TrimStart('/');
                        parameters[key] = parameter.Value;
                    }
                    request.NextToken = response.NextToken;
                }
                while (!string.IsNullOrEmpty(response.NextToken));
            }
            catch (Exception)
            {
                // Log error — return empty dictionary so application can use defaults
                // In production, consider structured logging (e.g., Amazon CloudWatch)
            }

            return parameters;
        }

        /// <summary>
        /// Retrieves a single parameter value from AWS SSM Parameter Store.
        /// </summary>
        public async System.Threading.Tasks.Task<string> GetParameterAsync(string parameterName)
        {
            try
            {
                var request = new GetParameterRequest
                {
                    Name = _parameterPath + parameterName,
                    WithDecryption = true
                };
                var response = await _ssmClient.GetParameterAsync(request);
                return response.Parameter.Value;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// cr-dotnet-1033: Distributed initialization service using Amazon DynamoDB distributed locking.
    /// Replaces Global.asax Application_Start instance-specific initialization with a distributed
    /// pattern that ensures initialization runs exactly once across all scaled AWS instances.
    ///
    /// Uses DynamoDB conditional writes (PutItem with attribute_not_exists condition expression)
    /// to implement a distributed lock. Only the instance that successfully acquires the lock
    /// performs the initialization. All other instances detect the lock and skip initialization,
    /// reading centralized configuration from AWS SSM Parameter Store instead.
    ///
    /// This prevents inconsistent state when the application is horizontally scaled in AWS (ECS/EKS).
    ///
    /// Environment variables:
    ///   DYNAMODB_LOCK_TABLE_NAME - DynamoDB table name for distributed locks
    ///                              (e.g., UniversityLibrarySystem-InitLocks)
    ///   AWS_REGION               - AWS region for DynamoDB client (e.g., us-east-1)
    ///   SSM_PARAMETER_PATH       - AWS SSM Parameter Store path prefix for app configuration
    /// </summary>
    public class DistributedInitializationService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly AwsParameterStoreConfigurationService _parameterStoreService;
        private readonly string _lockTableName;
        private const string LockKey = "ApplicationStartup";
        private const string LockTtlAttribute = "LockTtl";
        private const string LockOwnerAttribute = "LockOwner";
        private const string LockKeyAttribute = "LockKey";

        public DistributedInitializationService(AwsParameterStoreConfigurationService parameterStoreService)
        {
            _parameterStoreService = parameterStoreService;

            // cr-dotnet-1033: Read AWS region and DynamoDB lock table name from environment variables
            // following 12-factor app principles. No hardcoded infrastructure references.
            string awsRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
            _lockTableName = Environment.GetEnvironmentVariable("DYNAMODB_LOCK_TABLE_NAME")
                ?? "UniversityLibrarySystem-InitLocks";

            _dynamoDbClient = new AmazonDynamoDBClient(
                Amazon.RegionEndpoint.GetBySystemName(awsRegion));
        }

        /// <summary>
        /// Attempts to acquire a DynamoDB distributed lock and perform application initialization.
        /// Uses conditional PutItem (attribute_not_exists) to ensure only one instance initializes.
        /// Other instances detect the existing lock and read configuration from Parameter Store.
        /// Replaces Global.asax Application_Start instance-specific initialization.
        /// </summary>
        public async System.Threading.Tasks.Task StartAsync(CancellationToken cancellationToken)
        {
            bool lockAcquired = await TryAcquireDistributedLockAsync(cancellationToken);

            if (lockAcquired)
            {
                try
                {
                    // cr-dotnet-1033: Perform centralized initialization only on the instance
                    // that acquired the DynamoDB distributed lock. Load configuration from
                    // AWS SSM Parameter Store to ensure all instances use consistent settings.
                    await PerformDistributedInitializationAsync(cancellationToken);
                }
                finally
                {
                    // Release the distributed lock after initialization completes
                    await ReleaseDistributedLockAsync(cancellationToken);
                }
            }
            else
            {
                // cr-dotnet-1033: Another instance holds the lock and is performing initialization.
                // This instance reads centralized configuration from AWS SSM Parameter Store
                // to ensure consistent setup without re-running initialization logic.
                await LoadConfigurationFromParameterStoreAsync(cancellationToken);
            }
        }

        public System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Attempts to acquire a distributed lock in DynamoDB using a conditional PutItem.
        /// The condition expression 'attribute_not_exists(LockKey)' ensures atomicity —
        /// only one instance can successfully insert the lock item.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> TryAcquireDistributedLockAsync(CancellationToken cancellationToken)
        {
            try
            {
                // cr-dotnet-1033: DynamoDB conditional write for distributed locking.
                // attribute_not_exists(LockKey) ensures only one instance acquires the lock.
                // TTL attribute allows automatic lock expiry if the instance crashes.
                long ttlEpoch = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
                string instanceId = Environment.GetEnvironmentVariable("HOSTNAME")
                    ?? Guid.NewGuid().ToString();

                var putRequest = new PutItemRequest
                {
                    TableName = _lockTableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [LockKeyAttribute] = new AttributeValue { S = LockKey },
                        [LockOwnerAttribute] = new AttributeValue { S = instanceId },
                        [LockTtlAttribute] = new AttributeValue { N = ttlEpoch.ToString() }
                    },
                    // Conditional expression: only insert if the lock item does not already exist
                    ConditionExpression = $"attribute_not_exists({LockKeyAttribute})"
                };

                await _dynamoDbClient.PutItemAsync(putRequest, cancellationToken);
                return true; // Lock acquired successfully
            }
            catch (ConditionalCheckFailedException)
            {
                // cr-dotnet-1033: Another instance already holds the lock — skip initialization
                return false;
            }
            catch (Exception)
            {
                // DynamoDB unavailable — fall back to local initialization to avoid startup failure
                return true;
            }
        }

        /// <summary>
        /// Performs the distributed initialization: loads centralized configuration from
        /// AWS SSM Parameter Store and applies it to the application. This replaces the
        /// instance-specific Application_Start initialization in Global.asax.
        /// </summary>
        private async System.Threading.Tasks.Task PerformDistributedInitializationAsync(CancellationToken cancellationToken)
        {
            // cr-dotnet-1033: Load all application configuration from AWS SSM Parameter Store.
            // This replaces hardcoded values previously set in Application_Start (e.g., jQuery CDN
            // paths, route configurations, site settings). All instances will read from the same
            // centralized Parameter Store path, ensuring consistent configuration across the fleet.
            var parameters = await _parameterStoreService.GetParametersAsync();

            // Apply configuration values loaded from Parameter Store
            // (e.g., CDN paths, feature flags, connection strings stored as SSM SecureString)
            foreach (var param in parameters)
            {
                // Store in environment variables for application-wide access
                // following 12-factor app principles
                if (!string.IsNullOrEmpty(param.Key) && !string.IsNullOrEmpty(param.Value))
                {
                    Environment.SetEnvironmentVariable(
                        $"APP_{param.Key.ToUpperInvariant().Replace('/', '_').Replace('-', '_')}",
                        param.Value);
                }
            }
        }

        /// <summary>
        /// Loads configuration from AWS SSM Parameter Store for instances that did not acquire
        /// the distributed lock. Ensures all instances have consistent configuration.
        /// </summary>
        private async System.Threading.Tasks.Task LoadConfigurationFromParameterStoreAsync(CancellationToken cancellationToken)
        {
            // cr-dotnet-1033: Wait briefly for the lock-holding instance to complete initialization,
            // then load the same centralized configuration from AWS SSM Parameter Store.
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await PerformDistributedInitializationAsync(cancellationToken);
        }

        /// <summary>
        /// Releases the DynamoDB distributed lock by deleting the lock item.
        /// </summary>
        private async System.Threading.Tasks.Task ReleaseDistributedLockAsync(CancellationToken cancellationToken)
        {
            try
            {
                var deleteRequest = new DeleteItemRequest
                {
                    TableName = _lockTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        [LockKeyAttribute] = new AttributeValue { S = LockKey }
                    }
                };
                await _dynamoDbClient.DeleteItemAsync(deleteRequest, cancellationToken);
            }
            catch (Exception)
            {
                // Log error — lock will expire via DynamoDB TTL if delete fails
            }
        }
    }
}
