using System.Collections;
using System.Reflection;
using CityGenerator.Editor;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityGenerator.Tests.PlayMode.Traffic
{
    /// <summary>
    /// SPEC 17: a CarAgent generated with Enable Physics on (a non-kinematic Rigidbody on its own
    /// GameObject) must enter Recovering only on an impact above
    /// CarAgent's own VehicleImpactImpulseThreshold copy, releasing its crossing reservation and
    /// lane-occupancy segment exactly like ReleaseReservationWhileBlocked/AdvanceToNextNode.Leave
    /// already do elsewhere -- the same mechanism that fixed the five-minute deadlock CLAUDE.md
    /// documents, reused here so an accident can never reproduce it by another path. A roll below
    /// the threshold (a queue touching bumper-to-bumper at a red light) must never trigger it.
    /// </summary>
    internal class CarAgentPhysicsRecoveryTests
    {
        private GameObject networkGo;
        private GameObject carGo;
        private GameObject projectileGo;
        private GameObject pedestrianNetworkGo;
        private GameObject pedestrianGo;
        private GameObject groundGo;
        private GameObject pusherGo;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = previousTimeScale;
            if (carGo != null) Object.Destroy(carGo);
            if (projectileGo != null) Object.Destroy(projectileGo);
            if (pedestrianGo != null) Object.Destroy(pedestrianGo);
            if (pedestrianNetworkGo != null) Object.Destroy(pedestrianNetworkGo);
            if (groundGo != null) Object.Destroy(groundGo);
            if (pusherGo != null) Object.Destroy(pusherGo);
            if (networkGo != null) Object.Destroy(networkGo);
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

        private static string PhysicsStateName(CarAgent agent) => GetPrivate(agent, "physicsState").ToString();

        /// <summary>Builds a small synthetic network far from any other test/scene geometry, plus a
        /// physics-mode CarAgent placed on it, configured exactly like
        /// CityGeneratorTrafficBuilder.BuildVehicles would with Enable Physics on.</summary>
        private (TrafficNetwork network, CarAgent agent) BuildDrivingCar(Vector3 offset)
        {
            networkGo = new GameObject("TrafficNetwork");
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

            carGo = new GameObject("Car");
            carGo.transform.position = offset;
            carGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            carGo.SetActive(false);

            var rb = carGo.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            carGo.AddComponent<BoxCollider>();

            CarAgent agent = carGo.AddComponent<CarAgent>();
            SetField(agent, "network", network);

            carGo.SetActive(true);
            return (network, agent);
        }

        /// <summary>Fires a dynamic Rigidbody straight into the car from behind with enough
        /// velocity for its mass to produce an impulse above/below the threshold on first
        /// contact.</summary>
        private void FireProjectileAt(GameObject target, float speed, float mass)
        {
            projectileGo = new GameObject("Projectile");
            projectileGo.transform.position = target.transform.position + Vector3.back * 3f;
            var prb = projectileGo.AddComponent<Rigidbody>();
            prb.mass = mass;
            prb.useGravity = false;
            prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileGo.AddComponent<SphereCollider>().radius = 1f;
            prb.linearVelocity = Vector3.forward * speed;
        }

        /// <summary>The tool's own pedestrian layer when the project has it (this one does, created
        /// at generation time); any unnamed user layer index behaves identically for a synthetic
        /// physics test, so a project that has never generated a city still runs these.</summary>
        private static int PedestrianLayer()
        {
            int layer = LayerMask.NameToLayer(CityGeneratorConstants.PedestrianLayerName);
            return layer >= 0 ? layer : 9;
        }

        /// <summary>Builds a minimal PedestrianAgent (its own network with a single Ring node, so
        /// Start() finds one and doesn't disable the component) carrying the same kind of root
        /// proxy collider CityGeneratorColliderUtility gives a generated pedestrian: a plain
        /// collider with no Rigidbody, i.e. infinite mass to PhysX.</summary>
        private void BuildPedestrian(Vector3 position)
        {
            pedestrianNetworkGo = new GameObject("PedestrianNetwork");
            PedestrianNetwork pedestrianNetwork = pedestrianNetworkGo.AddComponent<PedestrianNetwork>();
            PedestrianManager pedestrianManager = pedestrianNetworkGo.AddComponent<PedestrianManager>();
            SetField(pedestrianNetwork, "manager", pedestrianManager);
            pedestrianNetwork.AddNode(position, PedestrianNodeKind.Ring);

            pedestrianGo = new GameObject("Pedestrian");
            pedestrianGo.transform.position = position;
            pedestrianGo.layer = PedestrianLayer();
            pedestrianGo.SetActive(false);
            pedestrianGo.AddComponent<CapsuleCollider>();
            pedestrianGo.AddComponent<Animator>();

            PedestrianAgent pedestrian = pedestrianGo.AddComponent<PedestrianAgent>();
            SetField(pedestrian, "network", pedestrianNetwork);

            pedestrianGo.SetActive(true);
        }

        [UnityTest]
        public IEnumerator PedestrianCollider_IsIgnoredByAPhysicsVehicle()
        {
            (_, CarAgent agent) = BuildDrivingCar(new Vector3(80000f, 0f, 0f));
            yield return null;

            BuildPedestrian(agent.transform.position + Vector3.forward * 5f);
            yield return null;

            Assert.IsTrue(Physics.GetIgnoreCollision(agent.OwnCollider, pedestrianGo.GetComponent<Collider>()),
                "A pedestrian is a static collider moved by transform: left colliding, it shoves the car off its lane.");
        }

        [UnityTest]
        public IEnumerator NonPedestrianColliderOnTheSameLayer_KeepsColliding()
        {
            (_, CarAgent agent) = BuildDrivingCar(new Vector3(90000f, 0f, 0f));
            yield return null;

            // The player shares the pedestrian layer (CityGeneratorSceneBuilder.AssignPedestrianLayer)
            // but is no PedestrianAgent: only registered agents are paired, which is exactly why
            // this is a per-pair ignore instead of a Vehicle/Pedestrian layer-matrix entry.
            var playerStandIn = new GameObject("PlayerStandIn") { layer = PedestrianLayer() };
            playerStandIn.transform.position = agent.transform.position + Vector3.forward * 5f;
            Collider playerCollider = playerStandIn.AddComponent<CapsuleCollider>();
            yield return null;

            bool ignored = Physics.GetIgnoreCollision(agent.OwnCollider, playerCollider);
            Object.Destroy(playerStandIn);

            Assert.IsFalse(ignored, "Only registered PedestrianAgents are ignored; the player must keep colliding with cars.");
        }

        [UnityTest]
        public IEnumerator HardImpactFromThePedestrianLayer_NeverEntersRecovering()
        {
            (_, CarAgent agent) = BuildDrivingCar(new Vector3(100000f, 0f, 0f));
            yield return null;

            LayerMask pedestrianMask = 1 << PedestrianLayer();
            SetField(agent, "pedestrianMask", pedestrianMask);

            // Well above VehicleImpactImpulseThreshold: a person must never knock a car out of
            // Driving, whatever the impulse says.
            FireProjectileAt(carGo, speed: 40f, mass: 1500f);
            projectileGo.layer = PedestrianLayer();

            for (int i = 0; i < 40; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Driving", PhysicsStateName(agent), "A contact from the pedestrian layer must never trigger Recovering.");
        }

        [UnityTest]
        public IEnumerator ImpactAboveThreshold_EntersRecovering_AndReleasesReservationAndLaneSegment()
        {
            (TrafficNetwork network, CarAgent agent) = BuildDrivingCar(new Vector3(50000f, 0f, 0f));
            yield return null; // let Start() run: assigns carId/targetNode and enters LaneOccupancy

            int targetNode = agent.TargetNode;
            Assert.GreaterOrEqual(targetNode, 0, "Setup sanity: Start() must find a node ahead.");
            TrafficNetwork.Node node = network.GetNode(targetNode);

            // Simulate this car currently holding an unsignalled crossing's priority, so the test
            // can verify it gets released on impact.
            Assert.IsTrue(network.TryReserve(node.Intersection, agent.CarId));
            SetField(agent, "reservedIntersection", node.Intersection);

            var segmentOccupants = (System.Collections.IDictionary)GetPrivate(network.LaneOccupancy, "segmentOccupants");
            Assert.IsTrue(segmentOccupants.Contains((-1, targetNode)), "Setup sanity: Start() must enter the lane segment.");

            FireProjectileAt(carGo, speed: 40f, mass: 1500f);

            float timeout = Time.time + 5f;
            while (PhysicsStateName(agent) != "Recovering" && Time.time < timeout)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Recovering", PhysicsStateName(agent), "A hard impact must move the car into Recovering.");
            Assert.AreEqual(0, network.ReservationOwner(node.Intersection), "Recovering must release the crossing reservation.");
            Assert.AreEqual(-1, (int)GetPrivate(agent, "reservedIntersection"));

            var occupantsAfter = (System.Collections.IDictionary)GetPrivate(network.LaneOccupancy, "segmentOccupants");
            var key = (-1, targetNode);
            if (occupantsAfter.Contains(key))
            {
                var list = (System.Collections.IList)occupantsAfter[key];
                CollectionAssert.DoesNotContain(list, agent, "Recovering must leave the lane-occupancy segment.");
            }
        }

        [UnityTest]
        public IEnumerator ImpactBelowThreshold_DoesNotEnterRecovering()
        {
            (_, CarAgent agent) = BuildDrivingCar(new Vector3(60000f, 0f, 0f));
            yield return null;

            // A gentle nudge -- like a queue of cars touching bumper-to-bumper at a red light --
            // must never cross VehicleImpactImpulseThreshold (400 kg.m/s).
            FireProjectileAt(carGo, speed: 0.5f, mass: 20f);

            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Driving", PhysicsStateName(agent), "A roll below the impulse threshold must never trigger Recovering.");
        }

        [UnityTest]
        public IEnumerator AfterSettling_RecoveringExits_AndReattachesToAValidNode()
        {
            (TrafficNetwork network, CarAgent agent) = BuildDrivingCar(new Vector3(70000f, 0f, 0f));
            yield return null;

            FireProjectileAt(carGo, speed: 40f, mass: 1500f);

            float enterTimeout = Time.time + 5f;
            while (PhysicsStateName(agent) != "Recovering" && Time.time < enterTimeout)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual("Recovering", PhysicsStateName(agent), "Setup sanity: the car must enter Recovering before it can exit it.");

            // With no ground and no drag, the projectile would otherwise keep resting against the
            // car forever, and a body still pushing it deliberately keeps it in Recovering (see
            // Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt).
            projectileGo.SetActive(false);

            // Speed up simulated time so this test doesn't have to wait out the full real-time
            // VehicleRecoveryMaxDuration (8s) safety cap.
            Time.timeScale = 8f;

            float exitTimeout = Time.time + 20f;
            while (PhysicsStateName(agent) != "Driving" && Time.time < exitTimeout)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Driving", PhysicsStateName(agent), "The car must eventually settle and return to Driving.");
            Assert.GreaterOrEqual(agent.TargetNode, 0, "Re-attaching must resolve a valid node ahead.");
            Assert.IsTrue(agent.enabled, "A network this small always has a node ahead, so the car must never give up and disable itself.");
        }

        /// <summary>A static floor just under a BuildDrivingCar car (whose default 1m BoxCollider
        /// reaches 0.5m below its pivot), so a Recovering car rests and slides on friction instead
        /// of falling forever. Left 5cm below the collider: flush with it, the depenetration
        /// contact alone already clears the impact threshold while the car is still Driving.</summary>
        private void BuildGround(Vector3 carPosition, bool slippery = false)
        {
            groundGo = new GameObject("Ground");
            groundGo.transform.position = carPosition + Vector3.down * 1.05f;
            BoxCollider ground = groundGo.AddComponent<BoxCollider>();
            ground.size = new Vector3(400f, 1f, 400f);

            if (slippery)
            {
                // At the default friction a knocked car stops in well under the 0.5s minimum
                // Recovering duration; a slippery floor keeps it sliding for seconds.
                ground.sharedMaterial = new PhysicsMaterial("Slippery")
                {
                    dynamicFriction = 0.1f,
                    staticFriction = 0.1f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                };
            }
        }

        /// <summary>Knocks the car sideways into Recovering with a moderate hit, then takes the
        /// projectile out of the scene so nothing keeps pushing the car afterwards.</summary>
        private IEnumerator KnockIntoRecovering(CarAgent agent)
        {
            projectileGo = new GameObject("Projectile");
            projectileGo.transform.position = carGo.transform.position + Vector3.right * 4f;
            var prb = projectileGo.AddComponent<Rigidbody>();
            prb.mass = 1500f;
            prb.useGravity = false;
            prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileGo.AddComponent<SphereCollider>().radius = 1f;
            prb.linearVelocity = Vector3.left * 8f;

            float timeout = Time.time + 5f;
            while (PhysicsStateName(agent) != "Recovering" && Time.time < timeout)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual("Recovering", PhysicsStateName(agent), "Setup sanity: the hit must trigger Recovering.");

            projectileGo.SetActive(false);
        }

        private float FlatSpeed(CarAgent agent)
        {
            Vector3 velocity = agent.GetComponent<Rigidbody>().linearVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        [UnityTest]
        public IEnumerator Recovering_WaitsUntilTheCarStopsSliding()
        {
            (_, CarAgent agent) = BuildDrivingCar(new Vector3(160000f, 0f, 0f));
            BuildGround(carGo.transform.position, slippery: true);
            yield return null;

            yield return KnockIntoRecovering(agent);
            float recoveringSince = Time.time;

            float speedAfterMinimum = 0f;
            float lastRecoveringSpeed = float.MaxValue;
            float timeout = Time.time + 9f;
            while (PhysicsStateName(agent) == "Recovering" && Time.time < timeout)
            {
                lastRecoveringSpeed = FlatSpeed(agent);
                if (Time.time - recoveringSince >= 0.5f)
                    speedAfterMinimum = Mathf.Max(speedAfterMinimum, lastRecoveringSpeed);
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(speedAfterMinimum, 0.5f, "Setup sanity: the car must still be sliding once the minimum Recovering duration is up, or this test proves nothing.");
            Assert.AreEqual("Driving", PhysicsStateName(agent), "The car must re-attach once it has stopped sliding.");
            Assert.Less(lastRecoveringSpeed, 0.6f, "Re-attaching while still sliding stops the car dead on the spot; it must wait until it has slowed down.");
        }

        [UnityTest]
        public IEnumerator Recovering_StaysFreeWhileADynamicBodyKeepsPushingIt()
        {
            (_, CarAgent agent) = BuildDrivingCar(new Vector3(170000f, 0f, 0f));
            BuildGround(carGo.transform.position);
            yield return null;

            yield return KnockIntoRecovering(agent);

            // The car slides off to the left; put the pusher right there, so the car runs into
            // it, stops (on this default-friction floor it would stop within the minimum
            // Recovering duration anyway) and is then shoved slowly back -- well under the sliding
            // threshold, so only the push can be what keeps it in Recovering below.
            pusherGo = new GameObject("Pusher");
            pusherGo.transform.position = carGo.transform.position + Vector3.left * 1.1f;
            var prb = pusherGo.AddComponent<Rigidbody>();
            prb.mass = 1500f;
            prb.useGravity = false;
            prb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            // Kept clear of the floor so only its contact with the car matters.
            pusherGo.AddComponent<BoxCollider>().size = new Vector3(1f, 0.8f, 1f);

            float pushUntil = Time.time + 3f;
            while (Time.time < pushUntil)
            {
                prb.linearVelocity = Vector3.right * 0.3f;
                yield return new WaitForFixedUpdate();
                Assert.AreEqual("Recovering", PhysicsStateName(agent), "A car still being pushed must stay free instead of turning into a wall.");
            }

            pusherGo.SetActive(false);
            float exitTimeout = Time.time + 9f;
            while (PhysicsStateName(agent) != "Driving" && Time.time < exitTimeout)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Driving", PhysicsStateName(agent), "Once the push stops, the car must re-attach.");
        }
    }
}
