using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode.Traffic
{
    /// <summary>
    /// SPEC 20, point 0: the sensor shortcuts must skip work without changing what a car sees.
    /// (a) a physics-mode car runs its sensors at most once per rendered frame, and a staggered
    /// one still gets its turn when every frame has two physics steps; (b) mid-block, the
    /// OverlapSphere fallback is skipped, yet a car still brakes for a car in Recovering or for one
    /// registered as (-1, target) ahead of it; (c)/(d) are covered by the equivalence test.
    /// </summary>
    internal class SensorCostTests
    {
        private const string SensorsMarker = "CityGenerator.Traffic.Sensors";
        private const string VehicleOverlapMarker = "CityGenerator.Traffic.Sensors.VehicleOverlap";

        private readonly List<GameObject> spawned = new();
        private GameObject networkGo;
        private TrafficNetwork network;
        private TrafficLaneOccupancy laneOccupancy;
        private float[] axes;
        private float laneX;
        private int previousCaptureFramerate;

        [SetUp]
        public void SetUp()
        {
            previousCaptureFramerate = Time.captureFramerate;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = previousCaptureFramerate;
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.Destroy(go);
            }
            spawned.Clear();
            if (networkGo != null)
                Object.Destroy(networkGo);
        }

        private static object GetPrivate(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            return info.GetValue(target);
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            info.SetValue(target, value);
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Method '{method}' not found on {target.GetType()}");
            info.Invoke(target, null);
        }

        /// <summary>One-block synthetic network around <paramref name="offset"/>, with a static
        /// floor under it so a Recovering car (gravity on) rests instead of falling.</summary>
        private void BuildNetwork(Vector3 offset)
        {
            networkGo = new GameObject("TrafficNetwork");
            networkGo.transform.position = offset;
            network = networkGo.AddComponent<TrafficNetwork>();
            TrafficManager manager = networkGo.AddComponent<TrafficManager>();
            laneOccupancy = networkGo.AddComponent<TrafficLaneOccupancy>();
            SetField(network, "manager", manager);
            SetField(network, "laneOccupancy", laneOccupancy);

            axes = new[]
            {
                CityGeneratorGrid.GetStreetAxisPosition(1, 0),
                CityGeneratorGrid.GetStreetAxisPosition(1, 1),
            };
            network.SetAxes(axes, axes);
            network.Build();

            // Northbound (+z) traffic keeps right: its lane sits laneOffset east of the street axis.
            float laneOffset = (float)GetPrivate(network, "laneOffset");
            laneX = offset.x + axes[0] + laneOffset;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = offset + new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            spawned.Add(floor);
        }

        private CarAgent SpawnCar(string name, float z, bool physics, float maxSpeed = 9f)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            go.transform.SetPositionAndRotation(new Vector3(laneX, 0f, z), Quaternion.LookRotation(Vector3.forward, Vector3.up));
            go.SetActive(false);

            if (physics)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 1200f;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.75f, 0f);
            box.size = new Vector3(1.8f, 1.5f, 4f);

            CarAgent agent = go.AddComponent<CarAgent>();
            SetField(agent, "network", network);
            SetField(agent, "maxSpeed", maxSpeed);
            go.SetActive(true);
            return agent;
        }

        // In this one-block network the northbound lane runs from the south crossing (centre at
        // z0 - 28) to the north one (entry node at z0 + 25.4). The mid-block shortcut applies
        // between 12 m past the south crossing's centre and 20 m before that entry node, i.e. for
        // z0 - 16 < z < z0 + 5.4, which is where the mid-block tests keep their cars.

        /// <summary>
        /// Moves <paramref name="agent"/> (already started, so registered as (-1, target)) onto the
        /// mid-block segment it physically drives, as if it had come through the south crossing:
        /// the only state in which the mid-block shortcut applies.
        /// </summary>
        private void PutOnProperSegment(CarAgent agent)
        {
            int target = agent.TargetNode;
            Assert.GreaterOrEqual(target, 0, "The car never found its first node.");

            int exitNode = -1;
            for (int i = 0; i < network.NodeCount; i++)
            {
                TrafficNetwork.Node node = network.GetNode(i);
                if (node.IsEntry || Vector3.Dot(node.Direction, Vector3.forward) < 0.9f)
                    continue;
                foreach (TrafficNetwork.Exit exit in node.Exits)
                {
                    if (exit.Node == target)
                        exitNode = i;
                }
            }
            Assert.GreaterOrEqual(exitNode, 0, "No northbound exit node leads to the car's target.");

            laneOccupancy.Leave(agent, -1, target);
            SetField(agent, "fromNode", exitNode);
            laneOccupancy.Enter(agent, exitNode, target);
        }

        private static IEnumerator RunFor(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
                yield return null;
        }

        [UnityTest]
        public IEnumerator MidBlock_WithNobodyAhead_SkipsTheOverlapFallback()
        {
            BuildNetwork(new Vector3(130000f, 0f, 0f));
            CarAgent car = SpawnCar("Car", networkGo.transform.position.z - 13f, physics: false);
            yield return null;
            PutOnProperSegment(car);

            using var overlap = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, VehicleOverlapMarker, 8);
            using var sensors = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, SensorsMarker, 8);
            long overlapTime = 0;
            long sensorTime = 0;
            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
                overlapTime += overlap.LastValue;
                sensorTime += sensors.LastValue;
            }

            Assert.Greater(sensorTime, 0L, "The sensors never ran, so the test proves nothing.");
            Assert.AreEqual(0L, overlapTime, "Mid-block with nobody ahead, the OverlapSphere fallback must be skipped.");
        }

        [UnityTest]
        public IEnumerator MidBlock_BrakesForACarInRecoveringAhead()
        {
            BuildNetwork(new Vector3(140000f, 0f, 0f));
            float z0 = networkGo.transform.position.z;
            CarAgent wreck = SpawnCar("Wreck", z0 + 3f, physics: true, maxSpeed: 0f);
            CarAgent car = SpawnCar("Car", z0 - 13f, physics: false);
            yield return null;
            PutOnProperSegment(car);

            Invoke(wreck, "EnterRecovering");
            // Keeps it Recovering for the whole test: TickRecovering waits for elapsed >= 0.5 s.
            SetField(wreck, "recoveringSince", float.MaxValue);
            laneOccupancy.Leave(wreck, -1, wreck.TargetNode);

            yield return RunFor(6f);

            float wreckRear = wreck.transform.position.z - 2f;
            float carFront = car.transform.position.z + 2f;
            Assert.Less(carFront, wreckRear, "The car drove into the Recovering car ahead of it.");
            Assert.Less(car.Speed, 0.3f, "The car should be stopped behind the Recovering car.");
        }

        [UnityTest]
        public IEnumerator MidBlock_BrakesForACarRegisteredAsMinusOneAhead()
        {
            BuildNetwork(new Vector3(150000f, 0f, 0f));
            float z0 = networkGo.transform.position.z;
            // Spawned cars register as (-1, target) until their first node, like a re-attached one.
            CarAgent stopped = SpawnCar("Stopped", z0 + 3f, physics: false, maxSpeed: 0f);
            CarAgent car = SpawnCar("Car", z0 - 13f, physics: false);
            yield return null;
            PutOnProperSegment(car);
            Assert.AreEqual(car.TargetNode, stopped.TargetNode);

            yield return RunFor(6f);

            float stoppedRear = stopped.transform.position.z - 2f;
            float carFront = car.transform.position.z + 2f;
            Assert.Less(carFront, stoppedRear, "The car drove into the (-1, target) car ahead of it.");
            Assert.Less(car.Speed, 0.3f, "The car should be stopped behind the other car.");
        }

        [UnityTest]
        public IEnumerator ReEnabledCar_IsBackInItsSegmentAtOnce()
        {
            BuildNetwork(new Vector3(160000f, 0f, 0f));
            CarAgent car = SpawnCar("Car", networkGo.transform.position.z - 20f, physics: false);
            yield return null;
            PutOnProperSegment(car);
            int from = car.FromNodeForTests();
            int target = car.TargetNode;

            car.gameObject.SetActive(false);
            Assert.IsFalse(laneOccupancy.HasOtherOccupant(null, from, target), "OnDisable must leave the segment.");

            car.gameObject.SetActive(true);
            Assert.IsTrue(laneOccupancy.HasOtherOccupant(null, from, target), "OnEnable must enter the segment again.");
        }

        [UnityTest]
        public IEnumerator PhysicsCar_RunsSensorsAtMostOncePerFrame()
        {
            BuildNetwork(new Vector3(170000f, 0f, 0f));
            SpawnCar("Car", networkGo.transform.position.z - 20f, physics: true);
            yield return null;

            // 10 fps of game time: five physics steps per rendered frame.
            Time.captureFramerate = 10;
            using var sensors = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, SensorsMarker, 16);
            int framesWithSensors = 0;
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
                if (sensors.Count == 0)
                    continue;

                long hits = sensors.GetSample(sensors.Count - 1).Count;
                Assert.LessOrEqual(hits, 1L, "A physics-mode car ran its sensors more than once in one frame.");
                if (hits > 0)
                    framesWithSensors++;
            }

            Assert.Greater(framesWithSensors, 0, "The sensors never ran.");
        }

        [UnityTest]
        public IEnumerator StaggeredPhysicsCar_StillGetsItsTurn_WithTwoStepsPerFrame()
        {
            BuildNetwork(new Vector3(180000f, 0f, 0f));
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            spawned.Add(cameraGo);
            cameraGo.AddComponent<Camera>();
            cameraGo.transform.position = networkGo.transform.position + new Vector3(0f, 0f, -5000f);

            TrafficManager manager = network.Manager;
            SetField(manager, "staggerMinAgentCount", 0);
            SetField(manager, "staggerDistance", 1f);

            for (int i = 0; i < 4; i++)
                SpawnCar("Car" + i, networkGo.transform.position.z - 20f + i * 8f, physics: true, maxSpeed: 0f);
            yield return null;

            // 25 fps of game time: exactly two physics steps per rendered frame.
            Time.captureFramerate = 25;
            using var sensors = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, SensorsMarker, 32);
            long total = 0;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return null;
                if (sensors.Count > 0)
                    total += sensors.GetSample(sensors.Count - 1).Count;
            }

            // 4 staggered cars x 16 frames / staggerFrames (4) = 16 sensor runs expected.
            Assert.GreaterOrEqual(total, 12L, "Staggered cars must still run their sensors every staggerFrames frames.");
        }
    }

    internal static class CarAgentTestExtensions
    {
        public static int FromNodeForTests(this CarAgent agent)
        {
            FieldInfo info = typeof(CarAgent).GetField("fromNode", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)info.GetValue(agent);
        }
    }
}
