using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Polly;
using Polly.Extensions.Http;

namespace Classes
{
    /// <summary>
    /// Provides a helper to resolve service URLs from AWS Systems Manager Parameter Store.
    /// Parameter names follow the convention: /UniversityLibrarySystem/SMS/{paramName}
    /// If the parameter is not found in SSM (e.g. local dev), the supplied fallback value is used.
    /// </summary>
    internal static class SsmParameterHelper
    {
        private static readonly AmazonSimpleSystemsManagementClient _ssmClient =
            new AmazonSimpleSystemsManagementClient();

        /// <summary>
        /// Retrieves a parameter value from AWS SSM Parameter Store.
        /// Falls back to <paramref name="fallbackValue"/> when the parameter cannot be resolved.
        /// </summary>
        public static string GetParameter(string parameterName, string fallbackValue = "")
        {
            try
            {
                var request = new GetParameterRequest
                {
                    Name = parameterName,
                    WithDecryption = true
                };

                var response = _ssmClient.GetParameterAsync(request).GetAwaiter().GetResult();
                return response.Parameter.Value;
            }
            catch
            {
                // Return fallback when SSM is unavailable (e.g. local development)
                return fallbackValue;
            }
        }
    }

    /// <summary>
    /// Provides a Polly retry policy for HttpClient calls targeting AWS API Gateway endpoints.
    /// Handles transient HTTP errors (503 Service Unavailable, 408 Request Timeout) and
    /// network-level timeouts that are common in cloud infrastructure.
    /// cr-dotnet-1014: Polly Retry Policy with AWS API Gateway and HttpClient
    /// </summary>
    internal static class HttpRetryPolicyHelper
    {
        /// <summary>
        /// Returns an async Polly retry policy that retries up to 3 times with exponential
        /// back-off (2 s, 4 s, 8 s) on transient HTTP 5xx / 408 responses and on
        /// <see cref="HttpRequestException"/> / <see cref="TaskCanceledException"/> (timeout).
        /// </summary>
        public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                // Handle HttpRequestException and HTTP 5xx / 408 status codes
                .HandleTransientHttpError()
                // Also handle 503 Service Unavailable explicitly (common on AWS API Gateway)
                .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
                // Also handle TaskCanceledException (request timeout)
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2s, 4s, 8s
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        // Log retry attempt details for cloud monitoring / CloudWatch
                        System.Diagnostics.Trace.TraceWarning(
                            $"[Polly] Retry {retryAttempt} after {timespan.TotalSeconds}s. " +
                            $"Reason: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                    });
        }
    }

    public class SmsSender
    {
        private readonly string apiKey = "";

        // URL loaded from AWS SSM Parameter Store at:
        //   /UniversityLibrarySystem/SMS/SendApiUrl
        // Fallback: "https://api.sms.ir/v1/send"
        private readonly string apiUrl = SsmParameterHelper.GetParameter(
            "/UniversityLibrarySystem/SMS/SendApiUrl",
            "https://api.sms.ir/v1/send");

        // cr-dotnet-1014: Polly retry policy for transient AWS API Gateway errors (503, timeout)
        private static readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy =
            HttpRetryPolicyHelper.GetRetryPolicy();

        public async Task<string> SendSmsAsync(string phoneNumber, string message)
        {
            // cr-dotnet-1014: HttpClient calls wrapped with Polly retry policy to handle
            // transient 503/timeout errors common in AWS cloud infrastructure.
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);

                var smsData = new
                {
                    Mobile = phoneNumber,
                    Message = message
                };

                var json = JsonSerializer.Serialize(smsData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Execute the POST through the Polly retry policy
                HttpResponseMessage response = await _retryPolicy.ExecuteAsync(
                    () => client.PostAsync(apiUrl, content));
                response.EnsureSuccessStatusCode();

                string result = await response.Content.ReadAsStringAsync();
                return result;
            }
        }

        /*
        string apiKey = "YourApiKey";
        SmsSender smsSender = new SmsSender(apiKey);

        string phoneNumber = "09123456789";
        string message = "Hello, this is a test message.";

        var result = smsSender.SendSmsAsync(phoneNumber, message).Result;         
         */
    }

    public class SmsIr
    {
        private readonly string apiKey;

        // URL loaded from AWS SSM Parameter Store at:
        //   /UniversityLibrarySystem/SMS/VerifyApiUrl
        // Fallback: "https://api.sms.ir/v1/send/verify"
        private readonly string apiUrl = SsmParameterHelper.GetParameter(
            "/UniversityLibrarySystem/SMS/VerifyApiUrl",
            "https://api.sms.ir/v1/send/verify");

        // cr-dotnet-1014: Polly retry policy for transient AWS API Gateway errors (503, timeout)
        private static readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy =
            HttpRetryPolicyHelper.GetRetryPolicy();

        public SmsIr(string apiKey)
        {
            this.apiKey = apiKey;
        }

        public async Task<string> SendVerifyAsync(string mobile, int templateId, VerifySendParameterModel[] parameters)
        {
            // cr-dotnet-1014: HttpClient calls wrapped with Polly retry policy to handle
            // transient 503/timeout errors common in AWS cloud infrastructure.
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);

                var model = new VerifySendModel
                {
                    Mobile = mobile,
                    TemplateId = templateId,
                    Parameters = parameters
                };

                var json = JsonSerializer.Serialize(model);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Execute the POST through the Polly retry policy
                HttpResponseMessage response = await _retryPolicy.ExecuteAsync(
                    () => client.PostAsync(apiUrl, content));
                response.EnsureSuccessStatusCode();

                string result = await response.Content.ReadAsStringAsync();
                return result;
            }
        }
    }
    /*
            string apiKey = "YourApiKey";
            SmsIr smsIr = new SmsIr(apiKey);

            string mobile = "9120000000";
            int templateId = 123456;
            VerifySendParameterModel[] parameters = new VerifySendParameterModel[]
            {
                new VerifySendParameterModel { Name = "CODE", Value = "1234" }
            };

            var result = smsIr.SendVerifyAsync(mobile, templateId, parameters).Result;
            Response.Write(result);*/
    #region Models
    public class VerifySendParameterModel
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class VerifySendModel
    {
        public string Mobile { get; set; }
        public int TemplateId { get; set; }
        public VerifySendParameterModel[] Parameters { get; set; }
    }

    #endregion
}
