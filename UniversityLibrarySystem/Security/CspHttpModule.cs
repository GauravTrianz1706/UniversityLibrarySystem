using System;
using System.Web;

namespace UniversityLibrarySystem.Security
{
    /// <summary>
    /// cr-dotnet-1041: Custom HttpModule that injects Content-Security-Policy (CSP) headers
    /// into every HTTP response served by the ASP.NET Web Forms application.
    ///
    /// Registered in Web.config under both:
    ///   - system.web/httpModules  (Classic pipeline / IIS 6 compatibility)
    ///   - system.webServer/modules (Integrated pipeline / IIS 7+)
    ///
    /// Combined with AWS WAF managed rules (AWSManagedRulesCommonRuleSet) this satisfies
    /// SOC2 and penetration-test compliance requirements for cloud-hosted multi-tenant
    /// environments by preventing Cross-Site Scripting (XSS) attacks.
    ///
    /// The CSP policy value can be overridden at runtime via the environment variable
    /// CSP_POLICY_HEADER, allowing AWS Systems Manager Parameter Store or AWS Secrets
    /// Manager to supply environment-specific policies without a code deployment.
    /// </summary>
    public class CspHttpModule : IHttpModule
    {
        // Default CSP policy applied when the environment variable is not set.
        // Allows same-origin resources, trusted CDNs used by the application, and
        // inline styles/scripts that ASP.NET Web Forms requires for __doPostBack etc.
        // Adjust the default to match your actual asset origins before going to production.
        private const string DefaultCspPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "form-action 'self'; " +
            "base-uri 'self';";

        public void Init(HttpApplication context)
        {
            if (context == null)
                throw new ArgumentNullException("context");

            context.PreSendRequestHeaders += OnPreSendRequestHeaders;
        }

        private void OnPreSendRequestHeaders(object sender, EventArgs e)
        {
            HttpApplication application = sender as HttpApplication;
            if (application == null)
                return;

            HttpResponse response = application.Response;
            if (response == null)
                return;

            // Retrieve the CSP policy from the environment variable so that
            // AWS Systems Manager Parameter Store / Secrets Manager can supply
            // environment-specific values without a code redeployment.
            string cspPolicy = Environment.GetEnvironmentVariable("CSP_POLICY_HEADER");
            if (string.IsNullOrWhiteSpace(cspPolicy))
            {
                cspPolicy = DefaultCspPolicy;
            }

            // Set Content-Security-Policy header (enforcing mode).
            // Remove any previously set value first to avoid duplicate headers.
            response.Headers.Remove("Content-Security-Policy");
            response.Headers.Add("Content-Security-Policy", cspPolicy);

            // Additional security headers recommended for cloud/SOC2 compliance.
            response.Headers.Remove("X-Content-Type-Options");
            response.Headers.Add("X-Content-Type-Options", "nosniff");

            response.Headers.Remove("X-Frame-Options");
            response.Headers.Add("X-Frame-Options", "DENY");

            response.Headers.Remove("X-XSS-Protection");
            response.Headers.Add("X-XSS-Protection", "1; mode=block");

            response.Headers.Remove("Referrer-Policy");
            response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
        }

        public void Dispose()
        {
            // No unmanaged resources to release.
        }
    }
}
