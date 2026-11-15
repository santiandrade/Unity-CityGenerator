using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode.Traffic
{
    /// <summary>
    /// SPEC 20, point 0: the sensor shortcuts (b, c) must not make cars see less. A 15x15 city with
    /// 1000 kinematic cars and 1000 pedestrians runs with the shortcuts switched off and on in
    /// alternating windows, so both see the same evolving traffic (jams grow over time), and counts
    /// new incidents every half second:
    /// - a pair of cars whose colliders start overlapping (a kinematic car only stops for what its
    ///   sensors see, so a missed car ends up inside it), split into mid-block ones -- where
    ///   shortcut (b) applies -- and the rest, which the traffic already produces at crossings
    ///   (the deadlock break pushes one car through on purpose);
    /// - a pedestrian on a crosswalk arm that gets a car within 1 m of it.
    /// The shortcuts must not raise the mid-block overlaps or the pedestrian incidents.
    /// Explicit: generating the city and simulating two minutes takes several minutes.
    /// </summary>
    [Explicit("Long: generates a 15x15 city with 1000 + 1000 agents and simulates two minutes.")]
    internal class SensorShortcutEquivalenceTests
    {
        private const int WindowPairs = 6;
        private const float WindowSeconds = 10f;
        private const float SampleInterval = 0.5f;
        private const float MinPenetration = 0.3f;
        private const float PedestrianClearance = 1f;
        // Same reach as CarAgent's mid-block shortcut: sensorRange (12) + MidBlockMargin (8).
        private const float MidBlockReach = 20f;

        private GameObject root;
        private FieldInfo vehicleShortcutField;
        private FieldInfo pedestrianShortcutField;
        private FieldInfo networkField;

        [SetUp]
        public void SetUp()
        {
            vehicleShortcutField = typeof(CarAgent).GetField("VehicleSensorShortcut", BindingFlags.NonPublic | BindingFlags.Static);
            pedestrianShortcutField = typeof(CarAgent).GetField("PedestrianSensorShortcut", BindingFlags.NonPublic | BindingFlags.Static);
            networkField = typeof(CarAgent).GetField("network", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(vehicleShortcutField);
            Assert.IsNotNull(pedestrianShortcutField);
            Assert.IsNotNull(networkField);
        }

        [TearDown]
        public void TearDown()
        {
            vehicleShortcutField.SetValue(null, true);
            pedestrianShortcutField.SetValue(null, true);
            if (root != null)
                Object.DestroyImmediate(root);
        }

        private sealed class Incidents
        {
            public int Samples;
            public int MidBlockOverlaps;
            public int OtherOverlaps;
            public int PedestrianIncidents;

            public override string ToString() =>
                $"{Samples} samples, {MidBlockOverlaps} new mid-block overlaps, {OtherOverlaps} other new overlaps, {PedestrianIncidents} pedestrian incidents";
        }

        /// <summary>Shortcut (b) alone, with (c) on in both windows.</summary>
        [UnityTest]
        public IEnumerator VehicleShortcut_DoesNotRaiseMidBlockOverlaps()
        {
            Incidents off = null, on = null;
            yield return RunAlternating(new[] { vehicleShortcutField }, (a, b) => { off = a; on = b; });
            Assert.LessOrEqual(on.MidBlockOverlaps, off.MidBlockOverlaps + Mathf.Max(2, off.MidBlockOverlaps / 5),
                $"Shortcut (b) raised mid-block overlaps. Off: {off}. On: {on}.");
        }

        /// <summary>Shortcut (c) alone, with (b) on in both windows.</summary>
        [UnityTest]
        public IEnumerator PedestrianShortcut_DoesNotRaisePedestrianIncidents()
        {
            Incidents off = null, on = null;
            yield return RunAlternating(new[] { pedestrianShortcutField }, (a, b) => { off = a; on = b; });
            Assert.LessOrEqual(on.PedestrianIncidents, off.PedestrianIncidents + Mathf.Max(2, off.PedestrianIncidents / 5),
                $"Shortcut (c) raised pedestrian incidents. Off: {off}. On: {on}.");
        }

        /// <summary>Both shortcuts toggled together, as they ship.</summary>
        [UnityTest]
        public IEnumerator BothShortcuts_DoNotRaiseMidBlockOverlapsOrPedestrianIncidents()
        {
            Incidents off = null, on = null;
            yield return RunAlternating(new[] { vehicleShortcutField, pedestrianShortcutField }, (a, b) => { off = a; on = b; });
            Assert.LessOrEqual(on.MidBlockOverlaps, off.MidBlockOverlaps + Mathf.Max(2, off.MidBlockOverlaps / 5),
                $"The shortcuts raised mid-block overlaps. Off: {off}. On: {on}.");
            Assert.LessOrEqual(on.PedestrianIncidents, off.PedestrianIncidents + Mathf.Max(2, off.PedestrianIncidents / 5),
                $"The shortcuts raised pedestrian incidents. Off: {off}. On: {on}.");
        }

        /// <summary>
        /// Exact check of shortcut (b): every skipped OverlapSphere is still run and any car it
        /// would have braked for is recorded (CarAgent.VerifyVehicleShortcut). No statistics: the
        /// shortcut is equivalent only if this list stays empty.
        /// </summary>
        [UnityTest]
        public IEnumerator VehicleShortcut_NeverSkipsACarTheOverlapWouldSee()
        {
            FieldInfo verify = typeof(CarAgent).GetField("VerifyVehicleShortcut", BindingFlags.NonPublic | BindingFlags.Static);
            var misses = (List<string>)typeof(CarAgent).GetField("ShortcutMisses", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            misses.Clear();
            verify.SetValue(null, true);
            try
            {
                yield return RunAlternating(System.Array.Empty<FieldInfo>(), (a, b) => { });
            }
            finally
            {
                verify.SetValue(null, false);
            }

            for (int i = 0; i < Mathf.Min(40, misses.Count); i++)
                Debug.Log("SPEC 20 shortcut miss: " + misses[i]);
            Assert.IsEmpty(misses, $"Shortcut (b) skipped {misses.Count} OverlapSphere scans that would have braked the car.");
        }

        /// <summary>A/A control: nothing toggled, to read the run-to-run noise of the two
        /// alternating window sets. Never fails; the numbers go to the spec's notes.</summary>
        [UnityTest]
        public IEnumerator Control_NoToggle_ShowsWindowNoise()
        {
            yield return RunAlternating(System.Array.Empty<FieldInfo>(), (a, b) => { });
        }

        private IEnumerator RunAlternating(FieldInfo[] toggled, System.Action<Incidents, Incidents> done)
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 15;
            settings.general.gridHeight = 15;
            settings.general.useCustomSeed = true;
            settings.general.seed = 99;
            settings.general.includeTraffic = true;
            settings.general.vehicleCount = 1000;
            settings.general.includePedestrians = true;
            settings.general.pedestrianCount = 1000;
            settings.general.enablePhysics = false;

            root = new GameObject("EquivalenceCity");
            root.transform.position = new Vector3(40000f, 0f, 40000f);
            CityGeneratorContentAssembler.Assemble(settings, root.transform);

            // Every pedestrian's first plan is staggered by 0.01 s per spawn index.
            yield return new WaitForSeconds(1000 * 0.01f + 1f);

            CarAgent[] cars = root.GetComponentsInChildren<CarAgent>();
            PedestrianAgent[] pedestrians = root.GetComponentsInChildren<PedestrianAgent>();
            Assert.Greater(cars.Length, 0);
            int vehicleMask = 1 << cars[0].gameObject.layer;

            var off = new Incidents();
            var on = new Incidents();
            var state = new SamplerState();
            for (int window = 0; window < WindowPairs * 2; window++)
            {
                bool second = window % 2 == 1;
                foreach (FieldInfo field in toggled)
                    field.SetValue(null, second);
                yield return Sample(cars, pedestrians, vehicleMask, state, second ? on : off);
            }

            Debug.Log($"SPEC 20 equivalence [{(toggled.Length == 0 ? "control" : string.Join(" + ", System.Array.ConvertAll(toggled, f => f.Name)))}] -- first windows (off): {off}; second windows (on): {on}");
            done(off, on);
        }

        private sealed class SamplerState
        {
            public HashSet<(int, int)> PreviousPairs = new();
            public HashSet<(int, int)> CurrentPairs = new();
            public HashSet<int> PreviousPedestrians = new();
            public HashSet<int> CurrentPedestrians = new();
            public readonly Collider[] Hits = new Collider[16];
        }

        private bool IsMidBlock(CarAgent car)
        {
            var network = (TrafficNetwork)networkField.GetValue(car);
            if (network == null || car.TargetNode < 0)
                return false;

            Vector3 toTarget = network.GetNode(car.TargetNode).Position - car.transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude > MidBlockReach * MidBlockReach;
        }

        private IEnumerator Sample(CarAgent[] cars, PedestrianAgent[] pedestrians, int vehicleMask, SamplerState state, Incidents into)
        {
            float end = Time.time + WindowSeconds;
            float nextSample = Time.time;

            while (Time.time < end)
            {
                yield return null;
                if (Time.time < nextSample)
                    continue;

                nextSample = Time.time + SampleInterval;
                into.Samples++;
                state.CurrentPairs.Clear();
                state.CurrentPedestrians.Clear();

                foreach (CarAgent car in cars)
                {
                    if (car == null || !car.isActiveAndEnabled || car.OwnCollider is not BoxCollider box)
                        continue;

                    Transform t = car.transform;
                    Vector3 centre = t.TransformPoint(box.center);
                    Vector3 halfExtents = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
                    int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, state.Hits, t.rotation, vehicleMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        Collider other = state.Hits[i];
                        if (other == box)
                            continue;

                        int a = box.GetEntityId().GetHashCode();
                        int b = other.GetEntityId().GetHashCode();
                        (int, int) key = a < b ? (a, b) : (b, a);
                        if (state.CurrentPairs.Contains(key))
                            continue;

                        if (!Physics.ComputePenetration(box, t.position, t.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth)
                            || depth <= MinPenetration)
                        {
                            continue;
                        }

                        state.CurrentPairs.Add(key);
                        if (state.PreviousPairs.Contains(key))
                            continue;

                        CarAgent otherCar = other.GetComponent<CarAgent>();
                        if (IsMidBlock(car) || (otherCar != null && IsMidBlock(otherCar)))
                            into.MidBlockOverlaps++;
                        else
                            into.OtherOverlaps++;
                    }
                }

                foreach (PedestrianAgent pedestrian in pedestrians)
                {
                    if (pedestrian == null || !pedestrian.isActiveAndEnabled || !pedestrian.IsOnCrossingArm)
                        continue;

                    if (!Physics.CheckSphere(pedestrian.transform.position + Vector3.up * 0.9f, PedestrianClearance, vehicleMask, QueryTriggerInteraction.Ignore))
                        continue;

                    int id = pedestrian.GetEntityId().GetHashCode();
                    state.CurrentPedestrians.Add(id);
                    if (!state.PreviousPedestrians.Contains(id))
                        into.PedestrianIncidents++;
                }

                (state.PreviousPairs, state.CurrentPairs) = (state.CurrentPairs, state.PreviousPairs);
                (state.PreviousPedestrians, state.CurrentPedestrians) = (state.CurrentPedestrians, state.PreviousPedestrians);
            }
        }
    }
}
