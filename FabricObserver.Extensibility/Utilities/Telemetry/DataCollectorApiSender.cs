// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FabricObserver.Observers.Utilities.Telemetry
{
    /// <summary>
    /// Legacy Azure Monitor HTTP Data Collector API transport.
    /// The Data Collector API was retired on September 14, 2026. This transport remains only for
    /// sovereign/air-gapped clouds where the endpoint is still serviced. New deployments must
    /// configure the Logs Ingestion API (see <see cref="LogsIngestionSender"/>).
    /// </summary>
    // SendAsync/GetSignature are partially based on public sample: https://dejanstojanovic.net/aspnet/2018/february/send-data-to-azure-log-analytics-from-c-code/
    internal sealed class DataCollectorApiSender : ILogAnalyticsSender
    {
        private const string ApiVersion = "2016-04-01";

        // A single shared instance. Creating an HttpClient per send leaks sockets into TIME_WAIT
        // and can exhaust SNAT ports on a Service Fabric node.
        private static readonly HttpClient HttpClient = new();

        private readonly string workspaceId;
        private readonly string sharedKey;
        private readonly string logType;
        private readonly Logger logger;

        private string TargetUri => $"https://{workspaceId}.ods.opinsights.azure.com/api/logs?api-version={ApiVersion}";

        internal DataCollectorApiSender(string workspaceId, string sharedKey, string logType, Logger logger)
        {
            this.workspaceId = workspaceId;
            this.sharedKey = sharedKey;
            this.logType = logType;
            this.logger = logger;

            logger.LogWarning(
                "LogAnalytics telemetry is using the Azure Monitor HTTP Data Collector API, which was retired on September 14, 2026. " +
                "Telemetry delivery will fail. Configure LogAnalyticsDataCollectionEndpoint and LogAnalyticsDcrImmutableId to use the Logs Ingestion API.");
        }

        public async Task SendAsync(string jsonPayload, CancellationToken cancellationToken)
        {
            byte[] content = Encoding.UTF8.GetBytes(jsonPayload);
            string date = DateTime.UtcNow.ToString("r");

            // The signed content length must be the UTF-8 byte count, not the UTF-16 character count.
            // Using payload.Length here produces a 401 for any payload containing non-ASCII characters.
            string signature = GetSignature("POST", content.Length, "application/json", date, "/api/logs");

            using HttpRequestMessage request = new(HttpMethod.Post, TargetUri);
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(signature);
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.Add("Content-Type", "application/json");
            request.Content.Headers.Add("Log-Type", logType);
            request.Content.Headers.Add("x-ms-date", date);

            using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted)
            {
                return;
            }

            logger.LogWarning(
                $"Unexpected response from server in DataCollectorApiSender.SendAsync:{Environment.NewLine}{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        private string GetSignature(string method, int contentLength, string contentType, string date, string resource)
        {
            string message = $"{method}\n{contentLength}\n{contentType}\nx-ms-date:{date}\n{resource}";
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            using HMACSHA256 encryptor = new(Convert.FromBase64String(sharedKey));

            return $"SharedKey {workspaceId}:{Convert.ToBase64String(encryptor.ComputeHash(bytes))}";
        }

        public void Dispose()
        {
            // HttpClient is process-wide and intentionally not disposed here.
        }
    }
}
