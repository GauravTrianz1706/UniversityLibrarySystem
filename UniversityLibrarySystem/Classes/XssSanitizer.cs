// cr-dotnet-1018: Razor Views Without Anti-XSS Encoding
// This helper class provides centralized XSS sanitization using the Ganss.Xss HtmlSanitizer
// library. Allowed HTML tags and attributes configuration is loaded from AWS Secrets Manager
// for centralized management across multi-tenant cloud environments.
//
// The sanitizer configuration is stored in AWS Secrets Manager under the secret name
// specified by the XSS_SANITIZER_CONFIG_SECRET_NAME environment variable
// (default: "UniversityLibrarySystem/Security/XssSanitizerConfig").
//
// Secret value format (JSON):
// {
//   "allowedTags": ["a", "b", "br", "div", "em", "i", "li", "ol", "p", "span", "strong", "ul"],
//   "allowedAttributes": {
//     "a": ["href", "title", "class"],
//     "div": ["class", "role"],
//     "span": ["class"],
//     "*": ["class"]
//   }
// }
//
// If the secret is unavailable, a safe default configuration is used.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Ganss.Xss;

namespace UniversityLibrarySystem.Classes
{
    /// <summary>
    /// Provides centralized XSS sanitization using Ganss.Xss HtmlSanitizer.
    /// cr-dotnet-1018: Replaces unsafe @Html.Raw() / HtmlString usage with sanitized output.
    /// Sanitizer configuration (allowed tags/attributes) is loaded from AWS Secrets Manager
    /// for centralized management across multi-tenant cloud environments.
    /// </summary>
    public static class XssSanitizer
    {
        private static HtmlSanitizer _sanitizer;
        private static DateTime _configExpiry = DateTime.MinValue;
        private static readonly TimeSpan ConfigCacheTtl = TimeSpan.FromMinutes(15);
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        // Default secret name for XSS sanitizer configuration in AWS Secrets Manager
        private static readonly string SecretName =
            Environment.GetEnvironmentVariable("XSS_SANITIZER_CONFIG_SECRET_NAME")
            ?? "UniversityLibrarySystem/Security/XssSanitizerConfig";

        // Default allowed tags for safe HTML rendering (conservative whitelist)
        private static readonly string[] DefaultAllowedTags = new[]
        {
            "a", "b", "br", "div", "em", "i", "li", "ol", "p",
            "span", "strong", "ul", "td", "th", "tr", "table", "thead", "tbody"
        };

        // Default allowed attributes
        private static readonly Dictionary<string, string[]> DefaultAllowedAttributes =
            new Dictionary<string, string[]>
            {
                { "a",    new[] { "href", "title", "class" } },
                { "div",  new[] { "class", "role" } },
                { "span", new[] { "class" } },
                { "td",   new[] { "class" } },
                { "th",   new[] { "class" } },
                { "tr",   new[] { "class" } },
                { "*",    new[] { "class" } }
            };

        /// <summary>
        /// Sanitizes a plain-text string to prevent XSS injection.
        /// Encodes all HTML special characters — use for user-supplied plain text values.
        /// cr-dotnet-1018: Replaces direct use of Request.Query values in HTML output.
        /// </summary>
        public static string SanitizeText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // For plain text, encode HTML entities to prevent XSS
            return System.Net.WebUtility.HtmlEncode(input);
        }

        /// <summary>
        /// Sanitizes an HTML string using the configured HtmlSanitizer.
        /// Allowed tags and attributes are loaded from AWS Secrets Manager.
        /// cr-dotnet-1018: Replaces @Html.Raw() / HtmlString with sanitized HTML output.
        /// </summary>
        public static string SanitizeHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            var sanitizer = GetSanitizer();
            return sanitizer.Sanitize(html);
        }

        /// <summary>
        /// Asynchronously sanitizes an HTML string, loading config from AWS Secrets Manager.
        /// cr-dotnet-1018: Async variant for use in async action handlers.
        /// </summary>
        public static async Task<string> SanitizeHtmlAsync(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            var sanitizer = await GetSanitizerAsync().ConfigureAwait(false);
            return sanitizer.Sanitize(html);
        }

        /// <summary>
        /// Gets or creates the HtmlSanitizer instance, refreshing config from AWS Secrets Manager
        /// when the cache TTL expires.
        /// </summary>
        private static HtmlSanitizer GetSanitizer()
        {
            if (_sanitizer != null && DateTime.UtcNow < _configExpiry)
                return _sanitizer;

            _semaphore.Wait();
            try
            {
                if (_sanitizer != null && DateTime.UtcNow < _configExpiry)
                    return _sanitizer;

                // Try to load config from AWS Secrets Manager synchronously
                string configJson = null;
                try
                {
                    configJson = AwsSecretsManagerHelper.GetSecret(SecretName);
                }
                catch
                {
                    // Fall through to defaults
                }

                _sanitizer = BuildSanitizer(configJson);
                _configExpiry = DateTime.UtcNow.Add(ConfigCacheTtl);
                return _sanitizer;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Async version of GetSanitizer — loads config from AWS Secrets Manager without blocking.
        /// </summary>
        private static async Task<HtmlSanitizer> GetSanitizerAsync()
        {
            if (_sanitizer != null && DateTime.UtcNow < _configExpiry)
                return _sanitizer;

            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_sanitizer != null && DateTime.UtcNow < _configExpiry)
                    return _sanitizer;

                string configJson = null;
                try
                {
                    // cr-dotnet-1018: Load allowed tags/attributes config from AWS Secrets Manager
                    // for centralized management across multi-tenant cloud environments.
                    configJson = await AwsSecretsManagerHelper.GetSecretAsync(SecretName)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Fall through to defaults if Secrets Manager is unavailable
                }

                _sanitizer = BuildSanitizer(configJson);
                _configExpiry = DateTime.UtcNow.Add(ConfigCacheTtl);
                return _sanitizer;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Builds an HtmlSanitizer from the provided JSON configuration, or uses defaults.
        /// </summary>
        private static HtmlSanitizer BuildSanitizer(string configJson)
        {
            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Clear();
            sanitizer.AllowedAttributes.Clear();

            bool configLoaded = false;

            if (!string.IsNullOrEmpty(configJson))
            {
                try
                {
                    using (var doc = JsonDocument.Parse(configJson))
                    {
                        // Load allowed tags from secret
                        if (doc.RootElement.TryGetProperty("allowedTags", out var tagsElement)
                            && tagsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tag in tagsElement.EnumerateArray())
                            {
                                string tagName = tag.GetString();
                                if (!string.IsNullOrEmpty(tagName))
                                    sanitizer.AllowedTags.Add(tagName);
                            }
                            configLoaded = true;
                        }

                        // Load allowed attributes from secret
                        if (doc.RootElement.TryGetProperty("allowedAttributes", out var attrsElement)
                            && attrsElement.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var attrEntry in attrsElement.EnumerateObject())
                            {
                                if (attrEntry.Value.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var attr in attrEntry.Value.EnumerateArray())
                                    {
                                        string attrName = attr.GetString();
                                        if (!string.IsNullOrEmpty(attrName))
                                            sanitizer.AllowedAttributes.Add(attrName);
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // JSON parse error — fall through to defaults
                    configLoaded = false;
                }
            }

            if (!configLoaded)
            {
                // Apply default safe whitelist
                foreach (var tag in DefaultAllowedTags)
                    sanitizer.AllowedTags.Add(tag);

                foreach (var attrEntry in DefaultAllowedAttributes)
                {
                    foreach (var attr in attrEntry.Value)
                        sanitizer.AllowedAttributes.Add(attr);
                }
            }

            return sanitizer;
        }
    }
}
