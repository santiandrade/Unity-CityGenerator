using System.Collections;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// SPEC 20, grounding shortcut: pedestrians beyond PedestrianManager.GroundingShortcutDistance of
    /// the focus skip the per-frame ground raycast on flat stretches (and between their logic frames
    /// while staggered), yet keep their feet on the ground.
    /// </summary>
    internal class PedestrianGroundingShortcutTests
    {
        private const string GroundingMarker = "CityGenerator.Pedestrians.Grounding";
        private const int MeasuredFrames = 40;

        private GameObject generatedCity;
        private GameObject focus;

        [TearDown]
        public void TearDown()
        {
            if (generatedCity != null)
                Object.DestroyImmediate(generatedCity);
            if (focus != null)
                Object.Destroy(focus);
        }

        private PedestrianAgent[] GenerateSmallPedestrianCity(Vector3 position)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 3;
            settings.general.gridHeight = 3;
            settings.general.useCustomSeed = true;
            settings.general.seed = 7;
            settings.general.includeTraffic = false;
            settings.general.vehicleCount = 0;
            settings.general.includePedestrians = true;
            settings.general.pedestrianCount = 100;

            generatedCity = new GameObject("GroundingCity");
            generatedCity.transform.position = position;
            CityGeneratorContentAssembler.Assemble(settings, generatedCity.transform);
            return generatedCity.GetComponentsInChildren<PedestrianAgent>();
        }

        private static IEnumerator CountGroundingCalls(ProfilerRecorder recorder, long[] total)
        {
            total[0] = 0;
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                yield return null;
                if (recorder.Count > 0)
                    total[0] += recorder.GetSample(recorder.Count - 1).Count;
            }
        }

        [UnityTest]
        public IEnumerator FarPedestrians_CastFarFewerRays_AndStayOnTheGround()
        {
            Vector3 origin = new(-90000f, 0f, 90000f);
            PedestrianAgent[] pedestrians = GenerateSmallPedestrianCity(origin);
            PedestrianManager manager = generatedCity.GetComponentInChildren<PedestrianManager>();
            Assert.IsNotNull(manager);
            Assert.Greater(pedestrians.Length, 50);

            focus = new GameObject("Focus");
            focus.transform.position = origin + Vector3.right * 2000f;
            manager.SimulationFocus = focus.transform;

            // Agents pause up to 0.5 s before their first walk.
            yield return new WaitForSeconds(1.5f);

            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GroundingMarker, 8);
            var calls = new long[1];

            manager.GroundingShortcutDistance = 0f;
            yield return CountGroundingCalls(recorder, calls);
            long exactCalls = calls[0];
            Assert.Greater(exactCalls, MeasuredFrames * 20, "Without the shortcut every walking pedestrian raycasts every frame.");

            manager.GroundingShortcutDistance = 1f;
            // The first frames evaluate every agent's current stretch.
            for (int frame = 0; frame < 10; frame++)
                yield return null;

            calls[0] = 0;
            int samples = 0;
            int offGround = 0;
            float worst = 0f;
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                yield return null;
                if (recorder.Count > 0)
                    calls[0] += recorder.GetSample(recorder.Count - 1).Count;

                foreach (PedestrianAgent pedestrian in pedestrians)
                {
                    Vector3 position = pedestrian.transform.position;
                    if (!Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 4f, 1, QueryTriggerInteraction.Ignore))
                        continue;

                    float error = Mathf.Abs(position.y - hit.point.y);
                    samples++;
                    worst = Mathf.Max(worst, error);
                    if (error > 0.02f)
                        offGround++;
                }
            }

            Assert.Less(calls[0], exactCalls * 0.35f, $"With the shortcut far pedestrians must cast far fewer rays ({calls[0]} vs {exactCalls}).");
            Assert.Greater(samples, 0);
            Assert.Less((float)offGround / samples, 0.05f, $"{offGround} of {samples} samples were more than 2 cm off the ground.");
            Assert.Less(worst, 0.35f, "A staggered pedestrian may lag a kerb by a few frames, never more.");
        }
    }
}
