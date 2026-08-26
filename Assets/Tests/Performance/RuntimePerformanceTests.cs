using System.Collections;
using CityGenerator.Editor;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.Performance
{
    /// <summary>
    /// Baseline per-frame runtime cost with a generated city's traffic + pedestrians actively
    /// ticking (TrafficManager/PedestrianManager Update, CarAgent/PedestrianAgent sensors,
    /// Physics.SyncTransforms), at the three agent loads this spec's items 8-9 baseline/delta
    /// comparison uses. Measures whole-frame time rather than isolating each subsystem with its
    /// own ProfilerMarker (none of the production code carries one) -- informational only, per the
    /// spec's "no numeric threshold" decision; read from the Test Runner / exported results and
    /// copied into the spec/PR by hand.
    /// </summary>
    internal class RuntimePerformanceTests
    {
        private GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        private IEnumerator BuildCityWithAgents(int vehicleCount, int pedestrianCount)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 10;
            settings.general.gridHeight = 10;
            settings.general.useCustomSeed = true;
            settings.general.seed = 99;
            settings.general.includeTraffic = true;
            settings.general.vehicleCount = vehicleCount;
            settings.general.includePedestrians = true;
            settings.general.pedestrianCount = pedestrianCount;

            root = new GameObject("PerfRuntimeCity");
            root.transform.position = new Vector3(20000f, 0f, 20000f);
            CityGeneratorContentAssembler.Assemble(settings, root.transform);

            // A few frames so every agent's Start() (initial destination planning, node lookup) has
            // run before measurement starts -- otherwise the first measured frames would include
            // one-off spawn cost instead of steady-state ticking.
            yield return null;
            yield return null;
            yield return null;
        }

        [UnityTest, Performance]
        public IEnumerator PerFrameCost_60Agents()
        {
            yield return BuildCityWithAgents(60, 60);
            yield return Measure.Frames().WarmupCount(10).MeasurementCount(60).Run();
        }

        [UnityTest, Performance]
        public IEnumerator PerFrameCost_150Agents()
        {
            yield return BuildCityWithAgents(150, 150);
            yield return Measure.Frames().WarmupCount(10).MeasurementCount(60).Run();
        }

        [UnityTest, Performance]
        public IEnumerator PerFrameCost_300Agents()
        {
            yield return BuildCityWithAgents(300, 300);
            yield return Measure.Frames().WarmupCount(10).MeasurementCount(60).Run();
        }
    }
}
