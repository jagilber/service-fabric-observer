// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FabricObserver.Observers.Utilities.Telemetry
{
    /// <summary>
    /// Azure Monitor Logs Ingestion API transport. This is the supported replacement for the
    /// HTTP Data Collector API, which was retired on September 14, 2026.
    /// Data is routed through a Data Collection Endpoint (DCE) and a Data Collection Rule (DCR),
    /// authenticated with Entra ID rather than a workspace shared key.
    /// </summary>
    internal sealed class LogsIngestionSender : ILogAnalyticsSender
    {
        private readonly LogsIngestionClient client;
        private readonly string dcrImmutableId;
        private readonly string streamName;
        private readonly Logger logger;

        internal LogsIngestionSender(
                    Uri dataCollectionEndpoint,
                    string dcrImmutableId,
                    string streamName,
                    TokenCredential credential,
                    Logger logger)
        {
            this.dcrImmutableId = dcrImmutableId;
            this.streamName = streamName;
            this.logger = logger;

            // LogsIngestionClient is thread-safe and pools connections. Create once, reuse for the process lifetime.
            client = new LogsIngestionClient(dataCollectionEndpoint, credential);
        }

        public async Task SendAsync(string jsonPayload, CancellationToken cancellationToken)
        {
            if (!TryBuildIngestionBody(jsonPayload, out string body))
            {
                logger.LogWarning($"Unable to build Logs Ingestion payload from: {jsonPayload}");
                return;
            }

            try
            {
                using RequestContent content = RequestContent.Create(body);
                RequestContext context = new() { CancellationToken = cancellationToken };

                _ = await client.UploadAsync(dcrImmutableId, streamName, content, context: context);
            }
            catch (RequestFailedException rfe)
            {
                logger.LogWarning(
                    $"Unexpected response from server in LogsIngestionSender.SendAsync:{Environment.NewLine}{rfe.Status} {rfe.ErrorCode}: {rfe.Message}");
            }
            catch (AuthenticationFailedException afe)
            {
                logger.LogWarning(
                    "Unable to acquire an Entra ID token for Logs Ingestion. Verify the configured identity exists and holds the " +
                    $"Monitoring Metrics Publisher role on DCR {dcrImmutableId}.{Environment.NewLine}{afe.Message}");
            }
        }

        /// <summary>
        /// The Logs Ingestion API requires a JSON array and does not stamp TimeGenerated for the caller,
        /// unlike the Data Collector API. Normalize single objects into an array and add TimeGenerated when absent.
        /// </summary>
        internal static bool TryBuildIngestionBody(string jsonPayload, out string body)
        {
            body = null;

            try
            {
                // DateParseHandling.None keeps ISO-8601 values as strings. The default converts them to DateTime
                // and re-serializes them in a different format, corrupting every timestamp field in the payload.
                using JsonTextReader reader = new(new StringReader(jsonPayload)) { DateParseHandling = DateParseHandling.None };
                JToken token = JToken.ReadFrom(reader);
                JArray records = token as JArray ?? [token];
                string timeGenerated = DateTime.UtcNow.ToString("o");

                foreach (JObject record in records.OfType<JObject>())
                {
                    if (record.Property("TimeGenerated", StringComparison.OrdinalIgnoreCase) == null)
                    {
                        record.Add("TimeGenerated", timeGenerated);
                    }
                }

                body = records.ToString(Formatting.None);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            // LogsIngestionClient does not hold disposable state.
        }
    }
}
