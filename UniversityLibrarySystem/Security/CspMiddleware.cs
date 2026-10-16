using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace UniversityLibrarySystem.Security
{
    /// <summary>
    /// cr-dotnet-0044: ASP.NET Core middleware that injects Content-Security-Policy (CSP)
    /// headers into every HTTP response. Replaces the IIS-specific CspHttpModule
    /// (IHttpModule / HttpApplication.PreSendRequestHeaders) so the application can run
    /// on Kestrel inside Amazon ECS or EKS without any IIS dependency.
    ///
    /// Register in the ASP.NET Core pipeline (Program.cs / Startup.cs) with:
    ///   app.UseCspMiddleware();
    ///
    /// The CSP policy value can be overridden at runtime via the environment variable
    /// CSP_POLICY_HEADER, allowing AWS Systems Manager Parameter Store or AWS Secrets
    /// Manager to supply environment-specific policies without a code redeployment.
    /// </summary>
    public class CspMiddleware
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

        private readonly RequestDelegate _next;

        /// <summary>
        /// Initialises the middleware with the next delegate in the ASP.NET Core pipeline.
        /// </summary>
        /// <param name="next">The next middleware component.</param>
        public CspMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        /// <summary>
        /// Invokes the middleware: adds security headers to the response, then calls the
        /// next component in the pipeline. Replaces the IIS OnPreSendRequestHeaders event
        /// handler that was previously wired via HttpApplication.PreSendRequestHeaders.
        /// </summary>
        /// <param name="context">The current HTTP context provided by Kestrel.</param>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Retrieve the CSP policy from the environment variable so that
            // AWS Systems Manager Parameter Store / Secrets Manager can supply
            // environment-specific values without a code redeployment.
            string cspPolicy = Environment.GetEnvironmentVariable("CSP_POLICY_HEADER");
            if (string.IsNullOrWhiteSpace(cspPolicy))
            {
                cspPolicy = DefaultCspPolicy;
            }

            IHeaderDictionary headers = context.Response.Headers;

            // Set Content-Security-Policy header (enforcing mode).
            // Remove any previously set value first to avoid duplicate headers.
            headers.Remove("Content-Security-Policy");
            headers.Append("Content-Security-Policy", cspPolicy);

            // Additional security headers recommended for cloud/SOC2 compliance.
            headers.Remove("X-Content-Type-Options");
            headers.Append("X-Content-Type-Options", "nosniff");

            headers.Remove("X-Frame-Options");
            headers.Append("X-Frame-Options", "DENY");

            headers.Remove("X-XSS-Protection");
            headers.Append("X-XSS-Protection", "1; mode=block");

            headers.Remove("Referrer-Policy");
            headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

            // Pass control to the next middleware component in the Kestrel pipeline.
            await _next(context);
        }
    }

    /// <summary>
    /// Extension methods for registering <see cref="CspMiddleware"/> in the
    /// ASP.NET Core application pipeline.
    /// </summary>
    public static class CspMiddlewareExtensions
    {
        /// <summary>
        /// Adds the <see cref="CspMiddleware"/> to the application's request pipeline.
        /// Call this in Program.cs / Startup.Configure before any endpoint middleware:
        ///   app.UseCspMiddleware();
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
        /// <returns>The <see cref="IApplicationBuilder"/> for chaining.</returns>
        public static IApplicationBuilder UseCspMiddleware(this IApplicationBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            return builder.UseMiddleware<CspMiddleware>();
        }
    }
}
