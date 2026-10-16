using System;
using System.Web;

namespace UniversityLibrarySystem.Security
{
    /// <summary>
    /// cr-dotnet-1041: ASP.NET Web Forms HttpModule that injects Content-Security-Policy (CSP)
    /// and companion security headers into every HTTP response to satisfy SOC2 and
    /// penetration-test compliance requirements in cloud multi-tenant environments.
    ///
    /// Registered in Web.config for both pipeline modes:
    ///   - Classic pipeline (IIS 6 / Cassini):  system.web/httpModules
    ///   - Integrated pipeline (IIS 7+ / AWS Elastic Beanstalk): system.webServer/modules
    ///
    /// Combined with AWS WAF AWSManagedRulesCommonRuleSet managed rules to enforce
    /// XSS protection at the edge for cloud deployments.
    ///
    /// The CSP policy value can be overridden at runtime via the environment variable
    /// CSP_POLICY_HEADER, allowing AWS Systems Manager Parameter Store or AWS Secrets
    /// Manager to supply environment-specific policies without a code redeployment.
    ///
    /// Example Web.config registration (already present):
    ///   &lt;system.web&gt;
    ///     &lt;httpModules&gt;
    ///       &lt;add name="CspHttpModule" type="UniversityLibrarySystem.Security.CspHttpModule, UniversityLibrarySystem" /&gt;
    ///     &lt;/httpModules&gt;
    ///   &lt;/system.web&gt;
    ///   &lt;system.webServer&gt;
    ///     &lt;modules&gt;
    ///       &lt;add name="CspHttpModule" type="UniversityLibrarySystem.Security.CspHttpModule, UniversityLibrarySystem" /&gt;
    ///     &lt;/modules&gt;
    ///   &lt;/system.webServer&gt;
    /// </summary>
    public class CspHttpModule : IHttpModule
    {
        // Default CSP policy applied when the environment variable is not set.
        // Allows same-origin resources, trusted CDNs used by the application, and
        // inline styles/scripts that ASP.NET Web Forms requires for __doPostBack etc.
        // Adjust the default to match your actual asset origins before going to production.
        private const string DefaultCspPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://ajax.aspnetcdn.com; " +
            "style-src 'self' 'unsafe-inline' https://ajax.aspnetcdn.com; " +
            "img-src 'self' data:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "form-action 'self'; " +
            "base-uri 'self';";

        /// <summary>
        /// Initialises the module and registers the PreSendRequestHeaders event handler
        /// so that CSP headers are injected on every outgoing HTTP response.
        /// </summary>
        /// <param name="context">The <see cref="HttpApplication"/> instance for this request.</param>
        public void Init(HttpApplication context)
        {
            if (context == null)
                throw new ArgumentNullException("context");

            // Wire up to PreSendRequestHeaders so headers are added just before IIS
            // flushes them to the client — this is the standard pattern for IHttpModule
            // header injection in ASP.NET Web Forms.
            context.PreSendRequestHeaders += OnPreSendRequestHeaders;
        }

        /// <summary>
        /// Handles the PreSendRequestHeaders event: reads the CSP policy from the
        /// CSP_POLICY_HEADER environment variable (set via AWS Systems Manager Parameter
        /// Store at deployment time) and writes the security headers to the response.
        /// </summary>
        private void OnPreSendRequestHeaders(object sender, EventArgs e)
        {
            HttpApplication application = sender as HttpApplication;
            if (application == null || application.Response == null)
                return;

            HttpResponse response = application.Response;

            // Retrieve the CSP policy from the environment variable so that
            // AWS Systems Manager Parameter Store / Secrets Manager can supply
            // environment-specific values without a code redeployment.
            string cspPolicy = Environment.GetEnvironmentVariable("CSP_POLICY_HEADER");
            if (string.IsNullOrEmpty(cspPolicy))
            {
                cspPolicy = DefaultCspPolicy;
            }

            // Set Content-Security-Policy header (enforcing mode).
            // Remove any previously set value first to avoid duplicate headers.
            response.Headers.Remove("Content-Security-Policy");
            response.Headers.Set("Content-Security-Policy", cspPolicy);

            // Additional security headers recommended for cloud/SOC2 compliance.
            response.Headers.Remove("X-Content-Type-Options");
            response.Headers.Set("X-Content-Type-Options", "nosniff");

            response.Headers.Remove("X-Frame-Options");
            response.Headers.Set("X-Frame-Options", "DENY");

            response.Headers.Remove("X-XSS-Protection");
            response.Headers.Set("X-XSS-Protection", "1; mode=block");

            response.Headers.Remove("Referrer-Policy");
            response.Headers.Set("Referrer-Policy", "strict-origin-when-cross-origin");
        }

        /// <summary>
        /// Disposes any resources held by the module.
        /// No unmanaged resources are used, so this is a no-op.
        /// </summary>
        public void Dispose()
        {
            // No unmanaged resources to release.
        }
    }
}
