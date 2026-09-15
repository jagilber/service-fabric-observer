// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace FabricObserver.Observers.Utilities.Telemetry
{
    /// <summary>
    /// Transport abstraction for Log Analytics telemetry. Decouples payload construction in
    /// <see cref="LogAnalyticsTelemetry"/> from the wire protocol used to deliver it.
    /// </summary>
    public interface ILogAnalyticsSender : IDisposable
    {
        /// <summary>
        /// Sends a serialized JSON telemetry payload to Azure Monitor.
        /// </summary>
        /// <param name="jsonPayload">JSON object or JSON array of records.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        Task SendAsync(string jsonPayload, CancellationToken cancellationToken);
    }
}
