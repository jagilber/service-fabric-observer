// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace FabricObserver.Observers.Utilities.Telemetry
{
    /// <summary>
    /// LogAnalytics telemetry configuration.
    /// Supply DataCollectionEndpoint and DcrImmutableId to use the Logs Ingestion API.
    /// WorkspaceId and SharedKey select the retired HTTP Data Collector API and are only honored
    /// when the Logs Ingestion settings are absent.
    /// </summary>
    public class LogAnalyticsConfig
    {
        /// <summary>
        /// Data Collection Endpoint logs ingestion URI, e.g. https://my-dce-abcd.eastus-1.ingest.monitor.azure.com.
        /// </summary>
        public string DataCollectionEndpoint
        {
            get; set;
        }

        /// <summary>
        /// Immutable id of the Data Collection Rule, e.g. dcr-0123456789abcdef0123456789abcdef.
        /// </summary>
        public string DcrImmutableId
        {
            get; set;
        }

        /// <summary>
        /// DCR stream name. Defaults to Custom-{LogType}_CL when not supplied.
        /// </summary>
        public string StreamName
        {
            get; set;
        }

        /// <summary>
        /// Log scope. Also used to derive the default stream name.
        /// </summary>
        public string LogType
        {
            get; set;
        }

        /// <summary>
        /// Entra ID tenant id. Only required for service principal authentication.
        /// </summary>
        public string TenantId
        {
            get; set;
        }

        /// <summary>
        /// Client id of the service principal, or of the user-assigned managed identity when no secret is supplied.
        /// Leave empty to use the system-assigned managed identity.
        /// </summary>
        public string ClientId
        {
            get; set;
        }

        /// <summary>
        /// Service principal client secret. Prefer managed identity over storing a secret in configuration.
        /// </summary>
        public string ClientSecret
        {
            get; set;
        }

        /// <summary>
        /// Log Analytics workspace id. Retired HTTP Data Collector API only.
        /// </summary>
        public string WorkspaceId
        {
            get; set;
        }

        /// <summary>
        /// Log Analytics workspace shared key. Retired HTTP Data Collector API only.
        /// </summary>
        public string SharedKey
        {
            get; set;
        }
    }
}
