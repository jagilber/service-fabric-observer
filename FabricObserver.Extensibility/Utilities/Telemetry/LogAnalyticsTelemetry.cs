// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Health;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using FabricObserver.Observers.Interfaces;
using FabricObserver.TelemetryLib;
using Newtonsoft.Json;

namespace FabricObserver.Observers.Utilities.Telemetry
{
    public class LogAnalyticsTelemetry : ITelemetryProvider
    {
        private readonly Logger logger = new("TelemetryLogger");
        private readonly ILogAnalyticsSender sender;

        public string Key
        {
            get; set;
        }

        /// <summary>
        /// Creates a provider that sends to the retired HTTP Data Collector API.
        /// Retained for source compatibility and for clouds where the endpoint is still serviced.
        /// Prefer <see cref="Create"/>, which selects the Logs Ingestion API when it is configured.
        /// </summary>
        public LogAnalyticsTelemetry(string workspaceId, string sharedKey, string logType)
        {
            Key = sharedKey;
            sender = new DataCollectorApiSender(workspaceId, sharedKey, logType, logger);
        }

        private LogAnalyticsTelemetry(ILogAnalyticsSender sender)
        {
            this.sender = sender;
        }

        /// <summary>
        /// Builds a LogAnalytics telemetry provider from configuration, preferring the Logs Ingestion API.
        /// </summary>
        /// <param name="config">LogAnalytics configuration values.</param>
        /// <returns>A configured provider, or null when the supplied configuration is insufficient.</returns>
        public static LogAnalyticsTelemetry Create(LogAnalyticsConfig config)
        {
            Logger logger = new("TelemetryLogger");

            if (config == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(config.DataCollectionEndpoint) && !string.IsNullOrWhiteSpace(config.DcrImmutableId))
            {
                if (!Uri.TryCreate(config.DataCollectionEndpoint, UriKind.Absolute, out Uri endpoint))
                {
                    logger.LogWarning($"LogAnalyticsDataCollectionEndpoint is not a valid absolute URI: {config.DataCollectionEndpoint}. LogAnalytics telemetry is disabled.");
                    return null;
                }

                TokenCredential credential = CreateCredential(config);
                string stream = !string.IsNullOrWhiteSpace(config.StreamName) ? config.StreamName : $"Custom-{config.LogType}_CL";

                return new LogAnalyticsTelemetry(new LogsIngestionSender(endpoint, config.DcrImmutableId, stream, credential, logger));
            }

            if (!string.IsNullOrWhiteSpace(config.WorkspaceId) && !string.IsNullOrWhiteSpace(config.SharedKey))
            {
                return new LogAnalyticsTelemetry(config.WorkspaceId, config.SharedKey, config.LogType);
            }

            return null;
        }

        private static TokenCredential CreateCredential(LogAnalyticsConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.TenantId)
                && !string.IsNullOrWhiteSpace(config.ClientId)
                && !string.IsNullOrWhiteSpace(config.ClientSecret))
            {
                return new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
            }

            // A client id without a secret identifies a user-assigned managed identity.
            return !string.IsNullOrWhiteSpace(config.ClientId)
                ? new ManagedIdentityCredential(config.ClientId)
                : new ManagedIdentityCredential();
        }

        /// <summary>
        /// Sends telemetry data to Azure Monitor.
        /// </summary>
        /// <param name="payload">Json string containing telemetry data.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        /// <returns>A completed task or task containing exception info.</returns>
        private async Task SendTelemetryAsync(string payload, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(payload) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await sender.SendAsync(payload, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                logger.LogInfo($"Exception sending telemetry to LogAnalytics service:{Environment.NewLine}{e.Message}");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Do not take down FO with a telemetry fault. Log it. Warning level will always log.
                // This means there is either a bug in this code or something else that needs your attention.
#if DEBUG
                logger.LogWarning($"Exception sending telemetry to LogAnalytics service:{Environment.NewLine}{e}");
#else
                logger.LogWarning($"Exception sending telemetry to LogAnalytics service: {e.Message}");
#endif
            }
        }

        public async Task ReportHealthAsync(
                            string propertyName,
                            HealthState state,
                            string unhealthyEvaluations,
                            string source,
                            CancellationToken cancellationToken,
                            string serviceName = null,
                            string instanceName = null)
        {
            string jsonPayload = JsonConvert.SerializeObject(
                    new
                    {
                        ClusterInformation.ClusterInfoTuple.ClusterId,
                        source,
                        property = propertyName,
                        healthState = state.ToString(),
                        healthEvaluation = unhealthyEvaluations,
                        serviceName = serviceName ?? string.Empty,
                        instanceName = instanceName ?? string.Empty,
                        osPlatform = OperatingSystem.IsWindows() ? "Windows" : "Linux"
                    });

            await SendTelemetryAsync(jsonPayload, cancellationToken);
        }

        public async Task ReportHealthAsync(TelemetryDataBase telemetryData, CancellationToken cancellationToken)
        {
            if (telemetryData == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            switch (telemetryData)
            {
                case ServiceTelemetryData serviceTelemData:
                {
                    if (JsonHelper.TrySerializeObject(serviceTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case NodeTelemetryData nodeTelemData:
                {
                    if (JsonHelper.TrySerializeObject(nodeTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case DiskTelemetryData diskTelemData:
                {
                    if (JsonHelper.TrySerializeObject(diskTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case ClusterTelemetryData clusterTelemData:
                {
                    if (JsonHelper.TrySerializeObject(clusterTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case SystemServiceTelemetryData systemServiceTelemData:
                {
                    if (JsonHelper.TrySerializeObject(systemServiceTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case NetworkTelemetryData networkTelemData:
                {
                    if (JsonHelper.TrySerializeObject(networkTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case ContainerTelemetryData containerTelemData:
                {
                    if (JsonHelper.TrySerializeObject(containerTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                default:
                {
                    if (JsonHelper.TrySerializeObject(telemetryData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }
            }
        }

        public async Task ReportMetricAsync(TelemetryDataBase telemetryData, CancellationToken cancellationToken)
        {
            if (telemetryData == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            switch (telemetryData)
            {
                case ServiceTelemetryData serviceTelemData:
                {
                    if (JsonHelper.TrySerializeObject(serviceTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case NodeTelemetryData nodeTelemData:
                {
                    if (JsonHelper.TrySerializeObject(nodeTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case DiskTelemetryData diskTelemData:
                {
                    if (JsonHelper.TrySerializeObject(diskTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case ClusterTelemetryData clusterTelemData:
                {
                    if (JsonHelper.TrySerializeObject(clusterTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case SystemServiceTelemetryData systemServiceTelemData:
                {
                    if (JsonHelper.TrySerializeObject(systemServiceTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case NetworkTelemetryData networkTelemData:
                {
                    if (JsonHelper.TrySerializeObject(networkTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                case ContainerTelemetryData containerTelemData:
                {
                    if (JsonHelper.TrySerializeObject(containerTelemData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }

                default:
                {
                    if (JsonHelper.TrySerializeObject(telemetryData, out string jsonPayload))
                    {
                        await SendTelemetryAsync(jsonPayload, cancellationToken);
                    }

                    break;
                }
            }
        }

        public async Task ReportMetricAsync(List<ChildProcessTelemetryData> telemetryData, CancellationToken cancellationToken)
        {
            if (telemetryData == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (JsonHelper.TrySerializeObject(telemetryData, out string jsonPayload))
            {
                await SendTelemetryAsync(jsonPayload, cancellationToken);
            }
        }

        public async Task ReportMetricAsync(MachineTelemetryData machineTelemetryData, CancellationToken cancellationToken)
        {
            if (machineTelemetryData == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (JsonHelper.TrySerializeObject(machineTelemetryData, out string jsonPayload))
            {
                await SendTelemetryAsync(jsonPayload, cancellationToken);
            }
        }

        public async Task<bool> ReportMetricAsync<T>(
                                    string name,
                                    T value,
                                    string source,
                                    CancellationToken cancellationToken)
        {
            string jsonPayload = JsonConvert.SerializeObject(
                    new
                    {
                        id = $"FO_{Guid.NewGuid()}",
                        datetime = DateTime.UtcNow,
                        ClusterInformation.ClusterInfoTuple.ClusterId,
                        source,
                        property = name,
                        value
                    });

            await SendTelemetryAsync(jsonPayload, cancellationToken);

            return await Task.FromResult(true);
        }

        public async Task ReportClusterUpgradeStatusAsync(ServiceFabricUpgradeEventData eventData, CancellationToken token)
        {
            if (eventData?.FabricUpgradeProgress == null || token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                string jsonPayload = JsonConvert.SerializeObject(
                        new
                        {
                            ClusterId = ServiceFabricUpgradeEventData.ClusterId ?? ClusterInformation.ClusterInfoTuple.ClusterId,
                            Timestamp = DateTime.UtcNow,
                            eventData.OS,
                            CurrentUpgradeDomain = eventData.FabricUpgradeProgress.CurrentUpgradeDomainProgress?.UpgradeDomainName,
                            eventData.FabricUpgradeProgress?.NextUpgradeDomain,
                            UpgradeTargetCodeVersion = eventData.FabricUpgradeProgress.UpgradeDescription?.TargetCodeVersion,
                            UpgradeTargetConfigVersion = eventData.FabricUpgradeProgress.UpgradeDescription?.TargetConfigVersion,
                            UpgradeState = Enum.GetName(typeof(FabricUpgradeState), eventData.FabricUpgradeProgress.UpgradeState),
                            UpgradeDuration = eventData.FabricUpgradeProgress.CurrentUpgradeDomainDuration,
                            FailureReason = eventData.FabricUpgradeProgress.FailureReason.HasValue ? Enum.GetName(typeof(UpgradeFailureReason), eventData.FabricUpgradeProgress.FailureReason.Value) : null,
                        });

                await SendTelemetryAsync(jsonPayload, token).ConfigureAwait(true);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Telemetry is non-critical and should not take down FH.
                logger.LogWarning($"Failure in ReportClusterUpgradeStatus:{Environment.NewLine}{e.Message}");
            }
        }

        public async Task ReportApplicationUpgradeStatusAsync(ServiceFabricUpgradeEventData eventData, CancellationToken token)
        {
            if (eventData?.ApplicationUpgradeProgress == null || token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                string jsonPayload = JsonConvert.SerializeObject(
                        new
                        {
                            ClusterId = ServiceFabricUpgradeEventData.ClusterId ?? ClusterInformation.ClusterInfoTuple.ClusterId,
                            Timestamp = DateTime.UtcNow,
                            eventData.OS,
                            ApplicationName = eventData.ApplicationUpgradeProgress.ApplicationName?.OriginalString,
                            CurrentUpgradeDomain = eventData.ApplicationUpgradeProgress.CurrentUpgradeDomainProgress?.UpgradeDomainName,
                            eventData.ApplicationUpgradeProgress?.NextUpgradeDomain,
                            UpgradeTargetAppTypeVersion = eventData.ApplicationUpgradeProgress.UpgradeDescription?.TargetApplicationTypeVersion,
                            UpgradeState = Enum.GetName(typeof(FabricUpgradeState), eventData.ApplicationUpgradeProgress.UpgradeState),
                            UpgradeDuration = eventData.ApplicationUpgradeProgress.CurrentUpgradeDomainDuration,
                            FailureReason = eventData.ApplicationUpgradeProgress.FailureReason.HasValue ? Enum.GetName(typeof(UpgradeFailureReason), eventData.ApplicationUpgradeProgress.FailureReason.Value) : null,
                        });

                await SendTelemetryAsync(jsonPayload, token).ConfigureAwait(true);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Telemetry is non-critical and should not take down FH.
                logger.LogWarning($"Failure in ReportClusterUpgradeStatus:{Environment.NewLine}{e.Message}");
            }
        }

        public async Task ReportNodeSnapshotAsync(NodeSnapshotTelemetryData nodeSnapshotTelem, CancellationToken cancellationToken)
        {
            if (JsonHelper.TrySerializeObject(nodeSnapshotTelem, out string jsonPayload))
            {
                await SendTelemetryAsync(jsonPayload, cancellationToken);
            }
        }

        // Implement functions below as you need.
        public Task ReportAvailabilityAsync(
                        Uri serviceUri,
                        string instance,
                        string testName,
                        DateTimeOffset captured,
                        TimeSpan duration,
                        string location,
                        bool success,
                        CancellationToken cancellationToken,
                        string message = null)
        {
            return Task.CompletedTask;
        }

        public Task ReportMetricAsync(
                        string name,
                        long value,
                        IDictionary<string, string> properties,
                        CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReportMetricAsync(
                        string service,
                        Guid partition,
                        string name,
                        long value,
                        CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReportMetricAsync(
                        string role,
                        long id,
                        string name,
                        long value,
                        CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReportMetricAsync(
                        string roleName,
                        string instance,
                        string name,
                        long value,
                        int count,
                        long min,
                        long max,
                        long sum,
                        double deviation,
                        IDictionary<string, string> properties,
                        CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
