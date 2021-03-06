﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;

namespace FabricObserver.Observers.Utilities
{
    public class ConfigSettings
    {
        private ConfigurationSection section
        {
            get; set;
        }

        public TimeSpan RunInterval
        {
            get; set;
        }

        public TimeSpan MonitorDuration
        {
            get; set;
        }

        // Default enablement for any observer is enabled (true).
        public bool IsEnabled
        {
            get; set;
        } = true;

        public bool EnableVerboseLogging
        {
            get; set;
        }

        public bool IsObserverTelemetryEnabled
        {
            get; set;
        }

        public TimeSpan AsyncTimeout
        {
            get; set;
        } = TimeSpan.FromSeconds(60);

        public int DataCapacity
        {
            get;
            private set;
        }

        public ConfigurationSettings Settings
        {
            get; private set;
        }

        public bool UseCircularBuffer
        {
            get;
            private set;
        }

        public ConfigSettings(ConfigurationSettings settings, string observerConfiguration)
        {
            Settings = settings;
            section = settings.Sections[observerConfiguration];

            UpdateConfigSettings();
        }

        public void UpdateConfigSettings(
            ConfigurationSettings settings = null)
        {
            if (settings != null)
            {
                Settings = settings;
            }

            // Observer enabled?
            if (bool.TryParse(
                GetConfigSettingValue(
                ObserverConstants.ObserverEnabledParameter),
                out bool enabled))
            {
                IsEnabled = enabled;
            }

            // Observer telemetry enabled?
            if (bool.TryParse(
                GetConfigSettingValue(
                ObserverConstants.ObserverTelemetryEnabledParameter),
                out bool telemetryEnabled))
            {
                IsObserverTelemetryEnabled = telemetryEnabled;
            }

            // Verbose logging?
            if (bool.TryParse(
                GetConfigSettingValue(
                ObserverConstants.EnableVerboseLoggingParameter),
                out bool enableVerboseLogging))
            {
                EnableVerboseLogging = enableVerboseLogging;
            }

            // RunInterval?
            if (TimeSpan.TryParse(
                GetConfigSettingValue(
                ObserverConstants.ObserverRunIntervalParameter),
                out TimeSpan runInterval))
            {
                RunInterval = runInterval;
            }

            // Monitor duration.
            if (TimeSpan.TryParse(
                GetConfigSettingValue(
                ObserverConstants.MonitorDurationParameter),
                out TimeSpan monitorDuration))
            {
                MonitorDuration = monitorDuration;
            }

            // Async cluster operation timeout setting..
            if (int.TryParse(
                GetConfigSettingValue(
                ObserverConstants.AsyncClusterOperationTimeoutSeconds),
                out int asyncOpTimeoutSeconds))
            {
                AsyncTimeout = TimeSpan.FromSeconds(asyncOpTimeoutSeconds);
            }

            // Resource usage data collection item capacity.
            if (int.TryParse(
               GetConfigSettingValue(
               ObserverConstants.DataCapacityParameter),
               out int dataCapacity))
            {
                DataCapacity = dataCapacity;
            }

            // Resource usage data collection type.
            if (bool.TryParse(
                GetConfigSettingValue(
                ObserverConstants.UseCircularBufferParameter),
                out bool useCircularBuffer))
            {
                UseCircularBuffer = useCircularBuffer;
            }
        }

        private string GetConfigSettingValue(string parameterName)
        {
            try
            {
                var configSettings = Settings;

                if (configSettings == null || string.IsNullOrEmpty(section.Name))
                {
                    return null;
                }

                if (section == null)
                {
                    return null;
                }

                ConfigurationProperty parameter = null;

                if (section.Parameters.Any(p => p.Name == parameterName))
                {
                    parameter = section.Parameters[parameterName];
                }

                if (parameter == null)
                {
                    return null;
                }

                return parameter.Value;
            }
            catch (Exception e) when (e is KeyNotFoundException || e is FabricElementNotFoundException)
            {

            }

            return null;
        }
    }
}