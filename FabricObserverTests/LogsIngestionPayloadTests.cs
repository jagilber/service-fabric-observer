// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using FabricObserver.Observers.Utilities.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace FabricObserverTests
{
    [TestClass]
    public class LogsIngestionPayloadTests
    {
        [TestMethod]
        public void TryBuildIngestionBody_SingleObject_IsWrappedInArray()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody("{\"NodeName\":\"_Node_0\"}", out string body);

            Assert.IsTrue(built);

            JArray records = JArray.Parse(body);
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("_Node_0", (string)records[0]["NodeName"]);
        }

        [TestMethod]
        public void TryBuildIngestionBody_Array_StaysAnArrayAndStampsEveryRecord()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody("[{\"Value\":1},{\"Value\":2}]", out string body);

            Assert.IsTrue(built);

            JArray records = JArray.Parse(body);
            Assert.AreEqual(2, records.Count);
            Assert.IsNotNull(records[0]["TimeGenerated"]);
            Assert.IsNotNull(records[1]["TimeGenerated"]);
        }

        [TestMethod]
        public void TryBuildIngestionBody_MissingTimeGenerated_IsAdded()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody("{\"Metric\":\"CpuTime\"}", out string body);

            Assert.IsTrue(built);
            Assert.IsNotNull(JArray.Parse(body)[0]["TimeGenerated"]);
        }

        [TestMethod]
        public void TryBuildIngestionBody_ExistingTimeGenerated_IsPreserved()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody(
                            "{\"TimeGenerated\":\"2020-01-01T00:00:00.0000000Z\"}", out string body);

            Assert.IsTrue(built);
            Assert.AreEqual("[{\"TimeGenerated\":\"2020-01-01T00:00:00.0000000Z\"}]", body);
        }

        [TestMethod]
        public void TryBuildIngestionBody_TimestampFields_AreNotReformatted()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody(
                            "{\"ObservationTimestamp\":\"2020-06-15T13:45:30.1234567Z\"}", out string body);

            Assert.IsTrue(built);
            StringAssert.Contains(body, "\"ObservationTimestamp\":\"2020-06-15T13:45:30.1234567Z\"");
        }

        [TestMethod]
        public void TryBuildIngestionBody_NonAsciiContent_RoundTrips()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody("{\"ApplicationName\":\"fabric:/приложение\"}", out string body);

            Assert.IsTrue(built);
            Assert.AreEqual("fabric:/приложение", (string)JArray.Parse(body)[0]["ApplicationName"]);
        }

        [TestMethod]
        public void TryBuildIngestionBody_MalformedJson_ReturnsFalse()
        {
            bool built = LogsIngestionSender.TryBuildIngestionBody("{not json", out string body);

            Assert.IsFalse(built);
            Assert.IsNull(body);
        }
    }
}
