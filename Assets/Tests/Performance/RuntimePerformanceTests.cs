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
    /// Physics.SyncTransforms), at the three agent loads SPEC 05's items 8-9 baseline/delta
    /// comparison uses (whole-frame time only), plus SPEC 20's 15x15 physics-mode loads, which also
    /// record the package's own ProfilerMarkers per subsystem. Informational only, per SPEC 05's
    /// "no numeric threshold" decision; read from the Test Runner / exported results and copied
    /// into the spec/PR by hand.
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

        // SPEC 20: the package's own markers, recorded per frame next to the frame time so a dense
        // city's cost can be split by subsystem. Physics.Simulate is Unity's own physics step.
        private static readonly string[] AgentMarkers =
        {
            "CityGenerator.Traffic.Tick",
            "CityGenerator.Traffic.Sensors",
            "CityGenerator.Traffic.Sensors.Vehicles",
            "CityGenerator.Traffic.Sensors.VehicleOverlap",
            "CityGenerator.Traffic.Sensors.Pedestrians",
            "CityGenerator.Traffic.Sensors.Obstacles",
            "CityGenerator.Traffic.CollisionCallbacks",
            "CityGenerator.Traffic.SyncTransforms",
            "CityGenerator.Pedestrians.Tick",
            "CityGenerator.Pedestrians.Grounding",
            "CityGenerator.Pedestrians.Separation",
            "CityGenerator.Pedestrians.ProximityGrid",
            "Physics.Simulate"
        };

        private IEnumerator BuildCityWithAgents(int vehicleCount, int pedestrianCount, int gridSize = 10, bool enablePhysics = false)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = gridSize;
            settings.general.gridHeight = gridSize;
            settings.general.useCustomSeed = true;
            settings.general.seed = 99;
            settings.general.includeTraffic = true;
            settings.general.vehicleCount = vehicleCount;
            settings.general.includePedestrians = true;
            settings.general.pedestrianCount = pedestrianCount;
            settings.general.enablePhysics = enablePhysics;

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

        // SPEC 20: the scale a consuming project needs for a 15x15 city to look populated, with
        // Enable Physics on as such a project runs it, plus the same grid at today's typical load
        // for comparison. Every pedestrian's first destination is planned InitialPlanStaggerSeconds
        // apart (PedestrianAgent), so the city only reaches steady state once the last one walks.
        [UnityTest, Performance]
        public IEnumerator PerFrameCost_400Agents_15x15_Physics()
        {
            yield return BuildCityWithAgents(400, 400, 15, enablePhysics: true);
            yield return new WaitForSeconds(400 * 0.01f + 1f);
            yield return Measure.Frames().WarmupCount(30).MeasurementCount(120).ProfilerMarkers(AgentMarkers).Run();
        }

        [UnityTest, Performance]
        public IEnumerator PerFrameCost_1000Agents_15x15_Physics()
        {
            yield return BuildCityWithAgents(1000, 1000, 15, enablePhysics: true);
            yield return new WaitForSeconds(1000 * 0.01f + 1f);
            yield return Measure.Frames().WarmupCount(30).MeasurementCount(120).ProfilerMarkers(AgentMarkers).Run();
        }
    }
}
