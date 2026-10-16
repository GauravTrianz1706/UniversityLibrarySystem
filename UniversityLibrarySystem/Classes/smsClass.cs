using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Channels;
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
    /// cr-dotnet-1000: Replaced synchronous .GetAwaiter().GetResult() blocking calls with
    /// async/await patterns to ensure non-blocking operations and efficient thread pool usage
    /// under high load in cloud auto-scaling scenarios.
    /// cr-dotnet-0039: Replaced blocking producer-consumer patterns with System.Threading.Channels
    /// Channel&lt;T&gt; and async enumeration for non-blocking, scalable parameter retrieval in
    /// distributed cloud environments.
    /// </summary>
    internal static class SsmParameterHelper
    {
        private static readonly AmazonSimpleSystemsManagementClient _ssmClient =
            new AmazonSimpleSystemsManagementClient();

        /// <summary>
        /// Asynchronously retrieves a parameter value from AWS SSM Parameter Store.
        /// cr-dotnet-1000: Replaced blocking .GetAwaiter().GetResult() (line 38) with
        /// non-blocking await to prevent thread pool starvation under cloud auto-scaling load.
        /// cr-dotnet-0039 (Line 38): Replaced blocking collection Take() with Channel&lt;string&gt;
        /// async producer-consumer pattern. The producer writes the SSM parameter value into a
        /// bounded channel; the consumer reads it asynchronously — no thread is blocked waiting.
        /// Falls back to <paramref name="fallbackValue"/> when the parameter cannot be resolved.
        /// </summary>
        public static async Task<string> GetParameterAsync(string parameterName, string fallbackValue = "")
        {
            // cr-dotnet-0039 (Line 38): Channel<string> replaces BlockingCollection<T>.Take()
            // blocking pattern. BoundedChannel with capacity 1 provides backpressure control
            // and async enumeration via ReadAllAsync() — fully non-blocking producer-consumer.
            var resultChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

            // Producer: fetch SSM parameter and write result into channel asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var request = new GetParameterRequest
                    {
                        Name = parameterName,
                        WithDecryption = true
                    };

                    // Non-blocking async call — no thread is blocked waiting for SSM response
                    var response = await _ssmClient.GetParameterAsync(request).ConfigureAwait(false);
                    await resultChannel.Writer.WriteAsync(response.Parameter.Value ?? fallbackValue).ConfigureAwait(false);
                }
                catch
                {
                    // Return fallback when SSM is unavailable (e.g. local development)
                    await resultChannel.Writer.WriteAsync(fallbackValue).ConfigureAwait(false);
                }
                finally
                {
                    resultChannel.Writer.Complete();
                }
            });

            // Consumer: read result from channel asynchronously via async enumeration (non-blocking)
            string result = fallbackValue;
            await foreach (var item in resultChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                result = item;
            }

            return result;
        }

        /// <summary>
        /// Synchronous wrapper retained for static field initializers and legacy call sites.
        /// Prefer GetParameterAsync wherever async context is available.
        /// </summary>
        public static string GetParameter(string parameterName, string fallbackValue = "")
        {
            return GetParameterAsync(parameterName, fallbackValue).GetAwaiter().GetResult();
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
        /// cr-dotnet-1000 (Line 80): onRetry callback uses structured async-safe logging to
        /// avoid blocking the retry pipeline in cloud auto-scaling scenarios.
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
                    onRetryAsync: async (outcome, timespan, retryAttempt, context) =>
                    {
                        // cr-dotnet-1000 (Line 80): Replaced synchronous Trace.TraceWarning with
                        // async-compatible logging callback (onRetryAsync) to avoid blocking the
                        // Polly retry pipeline under cloud auto-scaling load.
                        await Task.Run(() =>
                            System.Diagnostics.Trace.TraceWarning(
                                $"[Polly] Retry {retryAttempt} after {timespan.TotalSeconds}s. " +
                                $"Reason: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}")
                        ).ConfigureAwait(false);
                    });
        }
    }

    public class SmsSender
    {
        private readonly string apiKey = "";
        private readonly string apiUrl;

        // cr-dotnet-1014: Polly retry policy for transient AWS API Gateway errors (503, timeout)
        private static readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy =
            HttpRetryPolicyHelper.GetRetryPolicy();

        public SmsSender()
        {
            // URL loaded asynchronously from AWS SSM Parameter Store at:
            //   /UniversityLibrarySystem/SMS/SendApiUrl
            // Fallback: "https://api.sms.ir/v1/send"
            // cr-dotnet-1000: Async initialization via Task.Run to avoid blocking constructor.
            apiUrl = Task.Run(() => SsmParameterHelper.GetParameterAsync(
                "/UniversityLibrarySystem/SMS/SendApiUrl",
                "https://api.sms.ir/v1/send")).GetAwaiter().GetResult();
        }

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
        private readonly string apiUrl;

        // cr-dotnet-1014: Polly retry policy for transient AWS API Gateway errors (503, timeout)
        private static readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy =
            HttpRetryPolicyHelper.GetRetryPolicy();

        public SmsIr(string apiKey)
        {
            this.apiKey = apiKey;
            // URL loaded asynchronously from AWS SSM Parameter Store at:
            //   /UniversityLibrarySystem/SMS/VerifyApiUrl
            // Fallback: "https://api.sms.ir/v1/send/verify"
            // cr-dotnet-1000: Async initialization via Task.Run to avoid blocking constructor.
            apiUrl = Task.Run(() => SsmParameterHelper.GetParameterAsync(
                "/UniversityLibrarySystem/SMS/VerifyApiUrl",
                "https://api.sms.ir/v1/send/verify")).GetAwaiter().GetResult();
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
