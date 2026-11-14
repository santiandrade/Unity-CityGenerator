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
    /// SPEC 20, steps 2-7: the delegable vehicle/pedestrian pair filter, the simulation focus and
    /// visibility staggering, the physics LOD of cars and the collider LOD of pedestrians, agent
    /// relocation and the animation hook.
    /// </summary>
    internal class AgentScalingTests
    {
        private const string SensorsMarker = "CityGenerator.Traffic.Sensors";

        private readonly List<GameObject> spawned = new();
        private GameObject generatedCity;

        [TearDown]
        public void TearDown()
        {
            CityGeneratorAPI.VehiclePedestrianPairFiltering = true;
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.Destroy(go);
            }
            spawned.Clear();
            if (generatedCity != null)
                Object.DestroyImmediate(generatedCity);
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

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        /// <summary>One-block network around <paramref name="offset"/>; returns its manager and the
        /// world x of the northbound lane.</summary>
        private (TrafficNetwork network, TrafficManager manager, float laneX) BuildNetwork(Vector3 offset)
        {
            GameObject networkGo = Track(new GameObject("TrafficNetwork"));
            networkGo.transform.position = offset;
            TrafficNetwork network = networkGo.AddComponent<TrafficNetwork>();
            TrafficManager manager = networkGo.AddComponent<TrafficManager>();
            TrafficLaneOccupancy laneOccupancy = networkGo.AddComponent<TrafficLaneOccupancy>();
            SetField(network, "manager", manager);
            SetField(network, "laneOccupancy", laneOccupancy);

            float[] axes =
            {
                CityGeneratorGrid.GetStreetAxisPosition(1, 0),
                CityGeneratorGrid.GetStreetAxisPosition(1, 1),
            };
            network.SetAxes(axes, axes);
            network.Build();

            float laneOffset = (float)network.GetType().GetField("laneOffset", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(network);
            return (network, manager, offset.x + axes[0] + laneOffset);
        }

        private CarAgent SpawnCar(TrafficNetwork network, Vector3 position, bool physics, bool withRenderer = false)
        {
            GameObject go = Track(new GameObject("Car"));
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.forward, Vector3.up));
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

            if (withRenderer)
            {
                var body = new GameObject("Body");
                body.transform.SetParent(go.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                body.AddComponent<MeshRenderer>();
            }

            CarAgent agent = go.AddComponent<CarAgent>();
            SetField(agent, "network", network);
            go.SetActive(true);
            return agent;
        }

        /// <summary>A PedestrianAgent on its own one-node network (it idles there), with a root
        /// capsule on the pedestrian layer like a generated one.</summary>
        private (PedestrianManager manager, PedestrianAgent agent) SpawnPedestrian(Vector3 position)
        {
            GameObject networkGo = Track(new GameObject("PedestrianNetwork"));
            PedestrianNetwork network = networkGo.AddComponent<PedestrianNetwork>();
            PedestrianManager manager = networkGo.AddComponent<PedestrianManager>();
            PedestrianRoadProximityGrid grid = networkGo.AddComponent<PedestrianRoadProximityGrid>();
            SetField(network, "manager", manager);
            SetField(manager, "roadProximityGrid", grid);
            network.AddNode(position, PedestrianNodeKind.Ring);

            GameObject go = Track(new GameObject("Pedestrian"));
            go.transform.position = position;
            int layer = LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName);
            go.layer = layer >= 0 ? layer : 9;
            go.SetActive(false);
            go.AddComponent<CapsuleCollider>();
            go.AddComponent<Animator>();
            PedestrianAgent agent = go.AddComponent<PedestrianAgent>();
            SetField(agent, "network", network);
            go.SetActive(true);
            return (manager, agent);
        }

        private static long SensorRunsOver(ProfilerRecorder recorder)
            => recorder.Count > 0 ? recorder.GetSample(recorder.Count - 1).Count : 0;

        // ---------- Step 2: delegable pair filter ----------

        [UnityTest]
        public IEnumerator PairFilteringOff_IgnoresNoPair_AndOnPairsEveryActiveAgent()
        {
            CityGeneratorAPI.VehiclePedestrianPairFiltering = false;
            (TrafficNetwork network, _, float laneX) = BuildNetwork(new Vector3(200000f, 0f, 0f));
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -10f), physics: true);
            (_, PedestrianAgent pedestrian) = SpawnPedestrian(new Vector3(200010f, 0f, 30f));
            yield return null;

            Collider pedestrianCollider = pedestrian.GetComponent<Collider>();
            Assert.IsFalse(Physics.GetIgnoreCollision(car.OwnCollider, pedestrianCollider), "Filtering off must register no pair.");

            CityGeneratorAPI.VehiclePedestrianPairFiltering = true;
            Assert.IsTrue(Physics.GetIgnoreCollision(car.OwnCollider, pedestrianCollider), "Switching it on must pair every active agent.");

            CityGeneratorAPI.VehiclePedestrianPairFiltering = false;
            Assert.IsFalse(Physics.GetIgnoreCollision(car.OwnCollider, pedestrianCollider), "Switching it off must restore every pair.");
        }

        // ---------- Step 3: focus and visibility staggering ----------

        [UnityTest]
        public IEnumerator SimulationFocus_ReplacesTheCamera_ForStaggering()
        {
            (TrafficNetwork network, TrafficManager manager, float laneX) = BuildNetwork(new Vector3(210000f, 0f, 0f));
            SetField(manager, "staggerMinAgentCount", 0);
            manager.SetStagger(10f, 4);

            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -10f), physics: false);
            // The main camera sits on the car (it would never be staggered), the focus far away.
            GameObject cameraGo = Track(new GameObject("Main Camera") { tag = "MainCamera" });
            cameraGo.AddComponent<Camera>();
            cameraGo.transform.position = car.transform.position;
            GameObject focus = Track(new GameObject("Focus"));
            focus.transform.position = car.transform.position + Vector3.right * 500f;
            manager.SimulationFocus = focus.transform;
            yield return null;

            using var sensors = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, SensorsMarker, 4);
            long runs = 0;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return null;
                runs += SensorRunsOver(sensors);
            }
            Assert.LessOrEqual(runs, 5L, "Far from the focus, the car must be staggered whatever the camera says.");

            manager.SimulationFocus = null;
            runs = 0;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return null;
                runs += SensorRunsOver(sensors);
            }
            Assert.GreaterOrEqual(runs, 15L, "Without a focus the camera is the reference again, and it sits on the car.");
        }

        [UnityTest]
        public IEnumerator InvisibleCar_IsStaggered_InsideTheStaggerDistance()
        {
            (TrafficNetwork network, TrafficManager manager, float laneX) = BuildNetwork(new Vector3(220000f, 0f, 0f));
            SetField(manager, "staggerMinAgentCount", 0);
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -10f), physics: false, withRenderer: true);
            // A disabled renderer is never visible, whatever camera (Scene view included) looks at it.
            car.GetComponentInChildren<MeshRenderer>().enabled = false;
            GameObject focus = Track(new GameObject("Focus"));
            focus.transform.position = car.transform.position + Vector3.right * 30f;
            manager.SimulationFocus = focus.transform;
            manager.SetStagger(60f, 4, invisibleDistance: 20f);
            yield return null;

            using var sensors = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, SensorsMarker, 4);
            long runs = 0;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return null;
                runs += SensorRunsOver(sensors);
            }
            Assert.LessOrEqual(runs, 5L, "An invisible car beyond the invisible-stagger distance must be staggered.");

            manager.SetStagger(60f, 4, invisibleDistance: 0f);
            runs = 0;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return null;
                runs += SensorRunsOver(sensors);
            }
            Assert.GreaterOrEqual(runs, 15L, "With visibility staggering off, a car inside the stagger distance runs every frame.");
        }

        // ---------- Step 4: physics LOD ----------

        [UnityTest]
        public IEnumerator PhysicsLod_SwitchesModesAtTheRadius_WithoutAJump()
        {
            (TrafficNetwork network, TrafficManager manager, float laneX) = BuildNetwork(new Vector3(230000f, 0f, 0f));
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -20f), physics: true);
            var rb = car.GetComponent<Rigidbody>();
            GameObject focus = Track(new GameObject("Focus"));
            focus.transform.position = car.transform.position + Vector3.right * 200f;
            manager.SimulationFocus = focus.transform;
            manager.PhysicsRadius = 50f;

            for (int i = 0; i < 40; i++)
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(rb.isKinematic, "Beyond the radius plus hysteresis the car must be kinematic.");
            Assert.IsFalse(car.IsPhysicsSimulated);
            Assert.Greater(car.Speed, 1f, "A kinematic car must keep driving.");

            // Each car's LOD is decided one frame in four: wait for the switch, then check that
            // the dynamic body starts at the speed the car was driving at.
            focus.transform.position = car.transform.position;
            float speedBefore = car.Speed;
            for (int i = 0; i < 40 && rb.isKinematic; i++)
            {
                speedBefore = car.Speed;
                yield return new WaitForFixedUpdate();
            }
            Assert.IsFalse(rb.isKinematic, "Within the radius the car must be dynamic again.");
            Assert.Less(Mathf.Abs(rb.linearVelocity.magnitude - speedBefore), 0.5f, "Going back to dynamic must carry the speed over.");

            manager.PhysicsRadius = 0f;
            focus.transform.position = car.transform.position + Vector3.right * 200f;
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(rb.isKinematic, "With the radius at 0 every car stays dynamic.");
        }

        [UnityTest]
        public IEnumerator PhysicsLod_LeavesARecoveringCarDynamic()
        {
            (TrafficNetwork network, TrafficManager manager, float laneX) = BuildNetwork(new Vector3(240000f, 0f, 0f));
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -20f), physics: true);
            GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            floor.transform.position = new Vector3(240000f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            yield return new WaitForFixedUpdate();

            Invoke(car, "EnterRecovering");
            SetField(car, "recoveringSince", float.MaxValue);
            GameObject focus = Track(new GameObject("Focus"));
            focus.transform.position = car.transform.position + Vector3.right * 500f;
            manager.SimulationFocus = focus.transform;
            manager.PhysicsRadius = 50f;

            for (int i = 0; i < 10; i++)
                yield return new WaitForFixedUpdate();
            Assert.IsFalse(car.GetComponent<Rigidbody>().isKinematic, "A Recovering car must stay free whatever the physics LOD says.");
        }

        [UnityTest]
        public IEnumerator ColliderLod_SwitchesPedestrianCollidersOff_AndPairsThemAgain()
        {
            (TrafficNetwork network, _, float laneX) = BuildNetwork(new Vector3(250000f, 0f, 0f));
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -20f), physics: true);
            (PedestrianManager manager, PedestrianAgent pedestrian) = SpawnPedestrian(new Vector3(250020f, 0f, 30f));
            // The proximity grid answers the car sensor as soon as there is more than 0 agents.
            SetField(manager, "staggerMinAgentCount", 0);
            GameObject focus = Track(new GameObject("Focus"));
            focus.transform.position = pedestrian.transform.position + Vector3.right * 300f;
            manager.SimulationFocus = focus.transform;
            manager.ColliderRadius = 40f;

            // Each agent's collider LOD is checked one frame in four.
            for (int frame = 0; frame < 6; frame++)
                yield return null;
            Collider pedestrianCollider = pedestrian.GetComponent<Collider>();
            Assert.IsFalse(pedestrianCollider.enabled, "Beyond the radius plus hysteresis the collider must be off.");

            focus.transform.position = pedestrian.transform.position;
            for (int frame = 0; frame < 6; frame++)
                yield return null;
            Assert.IsTrue(pedestrianCollider.enabled, "Within the radius the collider must be on again.");
            Assert.IsTrue(Physics.GetIgnoreCollision(car.OwnCollider, pedestrianCollider), "A collider switched back on must be paired again.");

            manager.ColliderRadius = 0f;
            focus.transform.position = pedestrian.transform.position + Vector3.right * 300f;
            yield return null;
            Assert.IsTrue(pedestrianCollider.enabled, "With the radius at 0 every collider stays on.");
        }

        // ---------- Step 6: relocation ----------

        private static (int from, int to) FirstSegment(TrafficNetwork network)
        {
            Assert.Greater(network.LaneSegmentCount, 0);
            network.GetLaneSegment(0, out int from, out int to);
            return (from, to);
        }

        [UnityTest]
        public IEnumerator CarRelocate_PutsItOnTheSegment_LeavesTheOldOne_AndDrivesOn()
        {
            (TrafficNetwork network, _, float laneX) = BuildNetwork(new Vector3(260000f, 0f, 0f));
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -20f), physics: false);
            yield return null;
            int oldTarget = car.TargetNode;

            (int from, int to) = FirstSegment(network);
            Vector3 start = network.GetNode(from).Position;
            Vector3 end = network.GetNode(to).Position;
            Vector3 direction = (end - start).normalized;

            Assert.IsFalse(car.Relocate(to, from, 0.5f), "An entry -> exit pair is not a street segment.");
            Assert.IsTrue(car.Relocate(from, to, 0.5f));
            Assert.AreEqual(from, car.FromNode);
            Assert.AreEqual(to, car.TargetNode);
            Vector3 expected = Vector3.Lerp(start, end, 0.5f);
            Assert.Less(Vector2.Distance(new Vector2(expected.x, expected.z), new Vector2(car.transform.position.x, car.transform.position.z)), 0.01f);
            Assert.Greater(Vector3.Dot(car.transform.forward, direction), 0.99f);

            network.LaneOccupancy.GetGap(to, start, direction, out float ahead, out _);
            Assert.Less(ahead, float.MaxValue, "The relocated car must be in its new segment's occupancy.");
            if (oldTarget != to)
            {
                network.LaneOccupancy.GetGap(oldTarget, car.transform.position, direction, out float oldAhead, out float oldBehind);
                Assert.AreEqual(float.MaxValue, oldAhead, "The relocated car must have left its old segment.");
                Assert.AreEqual(float.MaxValue, oldBehind, "The relocated car must have left its old segment.");
            }

            Vector3 before = car.transform.position;
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(Vector3.Dot(car.transform.position - before, direction), 1f, "The relocated car must drive on along its segment.");
        }

        [UnityTest]
        public IEnumerator CarRelocate_ARecoveringCarDrivesAgain()
        {
            (TrafficNetwork network, _, float laneX) = BuildNetwork(new Vector3(270000f, 0f, 0f));
            CarAgent car = SpawnCar(network, new Vector3(laneX, 0f, -20f), physics: true);
            yield return new WaitForFixedUpdate();
            Invoke(car, "EnterRecovering");
            SetField(car, "recoveringSince", float.MaxValue);

            (int from, int to) = FirstSegment(network);
            Assert.IsTrue(car.Relocate(from, to, 0.5f));
            object state = car.GetType().GetField("physicsState", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(car);
            Assert.AreEqual("Driving", state.ToString());

            Vector3 before = car.transform.position;
            for (int i = 0; i < 25; i++)
                yield return new WaitForFixedUpdate();
            Assert.Greater((car.transform.position - before).magnitude, 1f, "A relocated Recovering car must drive again.");
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

            generatedCity = new GameObject("PedestrianCity");
            generatedCity.transform.position = position;
            CityGeneratorContentAssembler.Assemble(settings, generatedCity.transform);
            return generatedCity.GetComponentsInChildren<PedestrianAgent>();
        }

        [UnityTest]
        public IEnumerator PedestrianRelocate_PutsItOnTheNode_AndItWalksOn()
        {
            PedestrianAgent[] pedestrians = GenerateSmallPedestrianCity(new Vector3(70000f, 0f, 70000f));
            yield return new WaitForSeconds(1.5f);
            PedestrianAgent pedestrian = pedestrians[0];
            PedestrianNetwork network = generatedCity.GetComponentInChildren<PedestrianNetwork>();

            int ring = -1;
            int crossing = -1;
            float farthest = 0f;
            for (int i = 0; i < network.NodeCount; i++)
            {
                PedestrianNode node = network.GetNode(i);
                if (node.Kind == PedestrianNodeKind.Crossing && crossing < 0)
                    crossing = i;
                if (node.Kind != PedestrianNodeKind.Ring || node.Blocked)
                    continue;
                float distance = (node.Position - pedestrian.transform.position).sqrMagnitude;
                if (distance > farthest)
                {
                    farthest = distance;
                    ring = i;
                }
            }
            Assert.GreaterOrEqual(ring, 0);
            Assert.GreaterOrEqual(crossing, 0);

            Assert.IsFalse(pedestrian.Relocate(crossing), "A Crossing node is the middle of a road.");
            Assert.IsTrue(pedestrian.Relocate(ring));
            Vector3 nodePosition = network.GetNode(ring).Position;
            Assert.Less(Vector2.Distance(new Vector2(nodePosition.x, nodePosition.z), new Vector2(pedestrian.transform.position.x, pedestrian.transform.position.z)), 0.01f);
            Assert.AreEqual(ring, pedestrian.CurrentNode);

            // Time, not frames: it first pauses for up to 0.5 s, and test frames can be very short.
            float walked = 0f;
            Vector3 last = pedestrian.transform.position;
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                yield return null;
                walked += (pedestrian.transform.position - last).magnitude;
                last = pedestrian.transform.position;
            }
            Assert.Greater(walked, 0.5f, "A relocated pedestrian must pick a destination and walk.");
        }

        // ---------- Step 7: animation hook ----------

        [UnityTest]
        public IEnumerator SetAnimatorDriven_HandsTheAnimatorOverAndBack()
        {
            PedestrianAgent[] pedestrians = GenerateSmallPedestrianCity(new Vector3(80000f, 0f, 80000f));
            yield return new WaitForSeconds(1.5f);

            PedestrianAgent walker = null;
            foreach (PedestrianAgent pedestrian in pedestrians)
            {
                if (pedestrian.State == PedestrianState.Walking)
                {
                    walker = pedestrian;
                    break;
                }
            }
            Assert.IsNotNull(walker, "No pedestrian is walking.");
            Animator animator = walker.GetComponent<Animator>();
            Assert.Greater(walker.AnimationSpeed, 0f, "A walking pedestrian has a Speed for its blend tree.");

            walker.SetAnimatorDriven(false);
            for (int frame = 0; frame < 5; frame++)
                yield return null;
            Assert.IsFalse(animator.enabled, "Handed over, the Animator must stay off.");

            walker.SetAnimatorDriven(true);
            Assert.IsTrue(animator.enabled);
            Assert.AreEqual(walker.AnimationSpeed, animator.GetFloat("Speed"), 0.0001f, "Handed back, the Animator must get the current Speed at once.");
        }
    }
}
