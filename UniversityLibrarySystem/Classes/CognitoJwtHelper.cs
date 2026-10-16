// cr-dotnet-1031: Windows Authentication in Web Forms Cloud
// Replaces IIS Windows Authentication (Integrated Security/NTLM/Kerberos) with Amazon Cognito
// User Pools and JWT Bearer Token authentication. Enables cloud-hosted Web Forms without
// on-premise Active Directory dependencies.
//
// Amazon Cognito User Pools issues JWT Bearer Tokens validated by ASP.NET Core middleware
// running on Kestrel (Amazon ECS/EKS) without on-premise Active Directory dependencies.
// COGNITO_USER_POOL_ID, COGNITO_CLIENT_ID, and AWS_REGION configured via environment variables.
//
// Configuration (set in environment variables or AWS Secrets Manager):
//   COGNITO_USER_POOL_ID  - Amazon Cognito User Pool ID (e.g., us-east-1_XXXXXXXXX)
//   COGNITO_CLIENT_ID     - Amazon Cognito App Client ID
//   AWS_REGION            - AWS region where the Cognito User Pool is deployed
//
// Registration in Program.cs / Startup.cs:
//   services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//       .AddJwtBearer(options =>
//       {
//           string region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
//           string userPoolId = Environment.GetEnvironmentVariable("COGNITO_USER_POOL_ID");
//           options.Authority = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
//           options.TokenValidationParameters = new TokenValidationParameters
//           {
//               ValidateIssuerSigningKey = true,
//               ValidateIssuer = true,
//               ValidateLifetime = true,
//               ValidateAudience = false,
//               ValidIssuer = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}"
//           };
//       });
//   app.UseAuthentication();
//   app.UseAuthorization();

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Classes
{
    /// <summary>
    /// cr-dotnet-1031: Helper for Amazon Cognito JWT Bearer Token authentication.
    /// Replaces Windows Authentication (Integrated Security/NTLM/Kerberos) with Cognito User Pools.
    /// JWT tokens are validated by ASP.NET Core JWT Bearer middleware configured with the Cognito
    /// User Pool authority endpoint. No on-premise Active Directory dependency required.
    /// </summary>
    public static class CognitoJwtHelper
    {
        // Environment variable names for Cognito configuration (12-factor app principle)
        private const string CognitoUserPoolIdEnvVar = "COGNITO_USER_POOL_ID";
        private const string CognitoClientIdEnvVar = "COGNITO_CLIENT_ID";
        private const string AwsRegionEnvVar = "AWS_REGION";

        /// <summary>
        /// Gets the Amazon Cognito User Pool ID from environment variables.
        /// cr-dotnet-1031: Replaces Windows AD domain configuration with cloud-native env vars.
        /// </summary>
        public static string GetUserPoolId()
        {
            return Environment.GetEnvironmentVariable(CognitoUserPoolIdEnvVar)
                ?? throw new InvalidOperationException(
                    $"Environment variable '{CognitoUserPoolIdEnvVar}' is not set. " +
                    "Configure the Amazon Cognito User Pool ID for JWT Bearer Token authentication.");
        }

        /// <summary>
        /// Gets the Amazon Cognito App Client ID from environment variables.
        /// cr-dotnet-1031: Replaces Windows AD client registration with Cognito App Client.
        /// </summary>
        public static string GetClientId()
        {
            return Environment.GetEnvironmentVariable(CognitoClientIdEnvVar)
                ?? throw new InvalidOperationException(
                    $"Environment variable '{CognitoClientIdEnvVar}' is not set. " +
                    "Configure the Amazon Cognito App Client ID for JWT Bearer Token authentication.");
        }

        /// <summary>
        /// Gets the AWS region from environment variables.
        /// cr-dotnet-1031: Replaces Windows AD domain controller location with AWS region config.
        /// </summary>
        public static string GetAwsRegion()
        {
            return Environment.GetEnvironmentVariable(AwsRegionEnvVar)
                ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                ?? "us-east-1";
        }

        /// <summary>
        /// Gets the Cognito User Pool authority URL for JWT Bearer Token validation.
        /// cr-dotnet-1031: Replaces Windows AD authority (LDAP/Kerberos KDC) with Cognito JWKS endpoint.
        /// The ASP.NET Core JWT Bearer middleware uses this URL to fetch the public signing keys
        /// from Cognito's JWKS endpoint and validate incoming JWT tokens.
        /// </summary>
        public static string GetCognitoAuthority()
        {
            string region = GetAwsRegion();
            string userPoolId = GetUserPoolId();
            return $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
        }

        /// <summary>
        /// Extracts the username from the Cognito JWT claims principal.
        /// cr-dotnet-1031: Replaces WindowsIdentity.GetCurrent().Name (Windows-only) with
        /// ClaimsPrincipal.Identity.Name backed by Cognito JWT 'cognito:username' claim.
        /// </summary>
        public static string GetUsernameFromClaims(ClaimsPrincipal user)
        {
            if (user == null || !user.Identity.IsAuthenticated)
                return null;

            // Cognito JWT tokens include 'cognito:username' claim
            var cognitoUsernameClaim = user.FindFirst("cognito:username");
            if (cognitoUsernameClaim != null)
                return cognitoUsernameClaim.Value;

            // Fall back to standard Name claim (set by ASP.NET Core Identity from sub claim)
            return user.Identity.Name;
        }

        /// <summary>
        /// Extracts the Cognito user groups (roles) from the JWT claims principal.
        /// cr-dotnet-1031: Replaces Windows AD group membership (IsInRole) with Cognito User Pool
        /// groups encoded as 'cognito:groups' claims in the JWT token.
        /// </summary>
        public static string[] GetUserGroups(ClaimsPrincipal user)
        {
            if (user == null || !user.Identity.IsAuthenticated)
                return Array.Empty<string>();

            return user.FindAll("cognito:groups")
                       .Select(c => c.Value)
                       .ToArray();
        }

        /// <summary>
        /// Checks whether the authenticated user belongs to a specific Cognito User Pool group.
        /// cr-dotnet-1031: Replaces WindowsPrincipal.IsInRole() (Windows AD group check) with
        /// Cognito User Pool group membership check via JWT 'cognito:groups' claims.
        /// </summary>
        public static bool IsInCognitoGroup(ClaimsPrincipal user, string groupName)
        {
            return GetUserGroups(user).Contains(groupName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates that the current HTTP request carries a valid Cognito JWT Bearer Token.
        /// cr-dotnet-1031: Replaces IIS Windows Authentication (NTLM/Kerberos challenge-response)
        /// with stateless JWT Bearer Token validation. The token is validated by the ASP.NET Core
        /// JWT Bearer middleware using Cognito's JWKS public keys — no AD domain join required.
        /// </summary>
        public static bool IsAuthenticatedWithCognito(HttpContext httpContext)
        {
            return httpContext?.User?.Identity?.IsAuthenticated == true;
        }
    }
}
