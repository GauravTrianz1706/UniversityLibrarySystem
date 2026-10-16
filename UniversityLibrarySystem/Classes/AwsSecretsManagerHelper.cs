// cr-dotnet-0123: Lack of Externalized Secrets
// This helper class retrieves secrets from AWS Secrets Manager at runtime, replacing hardcoded
// connection strings and credentials previously embedded in Web.config / appsettings.json.
//
// Secrets are encrypted at rest in AWS Secrets Manager, support automatic rotation, and can be
// updated without redeployment — following 12-factor app principles.
//
// Secret naming convention:
//   DB_SECRET_NAME env var → AWS Secrets Manager secret name for the database connection string
//   Default secret name: "UniversityLibrarySystem/Database/ConnectionString"
//
// The secret value is expected to be a JSON object with the following structure:
//   {
//     "connectionString": "metadata=res://...;provider connection string=\"data source=<host>;..."
//   }
// OR a plain string containing the Entity Framework connection string directly.
//
// Fallback: If AWS Secrets Manager is unavailable (e.g. local development), the helper falls back
// to the DB_CONNECTION_STRING environment variable, then to the Web.config value.

using System;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace UniversityLibrarySystem
{
    /// <summary>
    /// Retrieves application secrets from AWS Secrets Manager.
    /// cr-dotnet-0123: Replaces hardcoded connection strings and credentials in Web.config /
    /// appsettings.json with runtime retrieval from AWS Secrets Manager.
    /// Secrets are encrypted at rest, support automatic rotation, and can be updated without
    /// redeployment.
    /// </summary>
    public static class AwsSecretsManagerHelper
    {
        // Cached connection string to avoid repeated Secrets Manager calls on every request.
        // The cache is invalidated after the TTL expires to support secret rotation.
        private static string _cachedConnectionString;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
        private static readonly object _lock = new object();

        /// <summary>
        /// Retrieves the Entity Framework database connection string from AWS Secrets Manager.
        /// cr-dotnet-0123: Replaces hardcoded "ULSDBEntities" connection string in Web.config.
        ///
        /// Resolution order:
        ///   1. In-memory cache (refreshed every 5 minutes to support secret rotation)
        ///   2. AWS Secrets Manager secret named by DB_SECRET_NAME environment variable
        ///      (default: "UniversityLibrarySystem/Database/ConnectionString")
        ///   3. DB_CONNECTION_STRING environment variable (fallback for local development)
        ///   4. Web.config "ULSDBEntities" connection string (last-resort fallback)
        /// </summary>
        /// <returns>Entity Framework connection string for ULSDBEntities.</returns>
        public static string GetDbConnectionString()
        {
            // Return cached value if still valid (supports secret rotation with bounded staleness)
            if (_cachedConnectionString != null && DateTime.UtcNow < _cacheExpiry)
                return _cachedConnectionString;

            lock (_lock)
            {
                // Double-checked locking
                if (_cachedConnectionString != null && DateTime.UtcNow < _cacheExpiry)
                    return _cachedConnectionString;

                string connectionString = null;

                // 1. Try AWS Secrets Manager
                try
                {
                    string secretName = Environment.GetEnvironmentVariable("DB_SECRET_NAME")
                        ?? "UniversityLibrarySystem/Database/ConnectionString";

                    using (var client = new AmazonSecretsManagerClient())
                    {
                        var request = new GetSecretValueRequest
                        {
                            SecretId = secretName,
                            VersionStage = "AWSCURRENT"
                        };

                        var response = client.GetSecretValueAsync(request).GetAwaiter().GetResult();
                        string secretValue = response.SecretString;

                        if (!string.IsNullOrEmpty(secretValue))
                        {
                            // Try to parse as JSON object with "connectionString" key
                            try
                            {
                                using (var doc = JsonDocument.Parse(secretValue))
                                {
                                    if (doc.RootElement.TryGetProperty("connectionString", out var connStrElement))
                                    {
                                        connectionString = connStrElement.GetString();
                                    }
                                    else if (doc.RootElement.TryGetProperty("connection_string", out var connStrElement2))
                                    {
                                        connectionString = connStrElement2.GetString();
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                                // Secret value is a plain connection string (not JSON)
                                connectionString = secretValue;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // AWS Secrets Manager unavailable — fall through to environment variable fallback
                }

                // 2. Fall back to DB_CONNECTION_STRING environment variable
                if (string.IsNullOrEmpty(connectionString))
                {
                    connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
                }

                // 3. Fall back to Web.config connection string (last resort for local development)
                if (string.IsNullOrEmpty(connectionString))
                {
                    connectionString = System.Configuration.ConfigurationManager
                        .ConnectionStrings["ULSDBEntities"]?.ConnectionString;
                }

                if (!string.IsNullOrEmpty(connectionString))
                {
                    _cachedConnectionString = connectionString;
                    _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);
                }

                return _cachedConnectionString ?? string.Empty;
            }
        }

        /// <summary>
        /// Retrieves a named secret value from AWS Secrets Manager.
        /// cr-dotnet-0123: Generic helper for retrieving any application secret.
        /// </summary>
        /// <param name="secretName">The name or ARN of the secret in AWS Secrets Manager.</param>
        /// <param name="fallbackValue">Value to return if the secret cannot be retrieved.</param>
        /// <returns>The secret value, or <paramref name="fallbackValue"/> if unavailable.</returns>
        public static string GetSecret(string secretName, string fallbackValue = "")
        {
            try
            {
                using (var client = new AmazonSecretsManagerClient())
                {
                    var request = new GetSecretValueRequest
                    {
                        SecretId = secretName,
                        VersionStage = "AWSCURRENT"
                    };

                    var response = client.GetSecretValueAsync(request).GetAwaiter().GetResult();
                    return response.SecretString ?? fallbackValue;
                }
            }
            catch (Exception)
            {
                return fallbackValue;
            }
        }
    }
}
