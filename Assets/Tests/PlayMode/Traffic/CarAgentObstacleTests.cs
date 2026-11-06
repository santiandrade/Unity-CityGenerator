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
    /// SPEC 19: a generated car must brake for a TrafficObstacle on any layer (the player's own
    /// car, typically), a physics-mode car must stop dead instead of dragging a dynamic body its
    /// front is pushing, and a car knocked into Recovering must pull away from rest afterwards
    /// instead of resuming its pre-impact speed.
    /// </summary>
    internal class CarAgentObstacleTests
    {
        private GameObject networkGo;
        private GameObject carGo;
        private GameObject obstacleGo;
        private GameObject projectileGo;

        [TearDown]
        public void TearDown()
        {
            if (carGo != null) Object.Destroy(carGo);
            if (obstacleGo != null) Object.Destroy(obstacleGo);
            if (projectileGo != null) Object.Destroy(projectileGo);
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

        /// <summary>
        /// Builds a one-block synthetic network around <paramref name="offset"/> and a car on the
        /// northbound lane of its west street, 20m south of the block's centre line, so it has a
        /// long straight run ahead before its first crossing. Returns the car and the world-space
        /// x of the lane it drives on.
        /// </summary>
        private (CarAgent agent, float laneX) BuildCarOnStraightLane(Vector3 offset, bool physics)
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

            // Northbound (+z) traffic keeps right: its lane sits laneOffset east of the street axis.
            float laneOffset = (float)GetPrivate(network, "laneOffset");
            float laneX = offset.x + axes[0] + laneOffset;

            carGo = new GameObject("Car");
            carGo.transform.SetPositionAndRotation(new Vector3(laneX, 0f, offset.z - 20f), Quaternion.LookRotation(Vector3.forward, Vector3.up));
            carGo.SetActive(false);

            if (physics)
            {
                var rb = carGo.AddComponent<Rigidbody>();
                rb.mass = 1200f;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            BoxCollider box = carGo.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.75f, 0f);
            box.size = new Vector3(1.8f, 1.5f, 4f);

            CarAgent agent = carGo.AddComponent<CarAgent>();
            SetField(agent, "network", network);

            carGo.SetActive(true);
            return (agent, laneX);
        }

        private void BuildObstacle(Vector3 position, bool trafficObstacle, bool dynamicBody)
        {
            obstacleGo = new GameObject("Obstacle");
            obstacleGo.transform.position = position;
            obstacleGo.SetActive(false);

            BoxCollider box = obstacleGo.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(1.4f, 1.1f, 2.6f);

            if (dynamicBody)
            {
                var rb = obstacleGo.AddComponent<Rigidbody>();
                rb.mass = 500f;
                rb.useGravity = false;
                // Heavy damping: a nudge barely moves it, so the car never gets a run-up long
                // enough to hit it again above the impact threshold.
                rb.linearDamping = 50f;
                rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            }

            if (trafficObstacle)
                obstacleGo.AddComponent<TrafficObstacle>();

            obstacleGo.SetActive(true);
        }

        [UnityTest]
        public IEnumerator KinematicCar_StopsShortOfATrafficObstacle_OnAnyLayer()
        {
            (CarAgent agent, float laneX) = BuildCarOnStraightLane(new Vector3(110000f, 0f, 0f), physics: false);

            // Deliberately not on the Vehicle or Pedestrian layer: the sensor must not care.
            BuildObstacle(new Vector3(laneX, 0f, carGo.transform.position.z + 14f), trafficObstacle: true, dynamicBody: false);
            obstacleGo.layer = 0;

            float timeout = Time.time + 6f;
            while (Time.time < timeout)
                yield return null;

            float rearFace = obstacleGo.transform.position.z - 1.3f;
            float frontBumper = carGo.transform.position.z + 2f;
            Assert.Less(agent.Speed, 0.05f, "The car must come to a stop behind the obstacle.");
            Assert.AreEqual(CarAgent.StopReason.VehicleAhead, agent.CurrentStopReason, "An obstacle is braked for exactly like a car ahead.");
            Assert.Greater(rearFace - frontBumper, 1f, "The car must keep a gap, never drive into the obstacle.");
        }

        [UnityTest]
        public IEnumerator TrafficObstacleInTheOppositeLane_IsIgnored()
        {
            (CarAgent agent, float laneX) = BuildCarOnStraightLane(new Vector3(120000f, 0f, 0f), physics: false);

            // Southbound lane: two lane offsets west of this car's own lane.
            float laneOffset = (float)GetPrivate(networkGo.GetComponent<TrafficNetwork>(), "laneOffset");
            float obstacleZ = carGo.transform.position.z + 14f;
            BuildObstacle(new Vector3(laneX - 2f * laneOffset, 0f, obstacleZ), trafficObstacle: true, dynamicBody: false);

            float timeout = Time.time + 6f;
            while (Time.time < timeout && carGo.transform.position.z < obstacleZ + 3f)
                yield return null;

            Assert.Greater(carGo.transform.position.z, obstacleZ + 3f, "An obstacle in the oncoming lane must not stop the car.");
        }

        [UnityTest]
        public IEnumerator PhysicsCar_PushingADynamicBody_StopsInsteadOfDraggingIt()
        {
            (CarAgent agent, float laneX) = BuildCarOnStraightLane(new Vector3(130000f, 0f, 0f), physics: true);

            // No TrafficObstacle: the bumper is the safety net for a body the sensor can't see.
            // Placed just touching the car's front so the first push is far below the impact
            // threshold, i.e. exactly the sustained-contact case OnCollisionEnter never escalates.
            Vector3 start = new Vector3(laneX, 0f, carGo.transform.position.z + 2f + 1.3f + 0.01f);
            BuildObstacle(start, trafficObstacle: false, dynamicBody: true);

            float timeout = Time.time + 4f;
            while (Time.time < timeout)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Driving", GetPrivate(agent, "physicsState").ToString(), "A gentle push must never become an impact.");
            Assert.Greater((float)GetPrivate(agent, "bumperBlockedUntil"), 0f, "The front contact must have engaged the bumper.");
            float dragged = obstacleGo.transform.position.z - start.z;
            Assert.Less(dragged, 1.5f, $"The car dragged the body {dragged:F2}m; it must stop instead of pushing it along.");
        }

        [UnityTest]
        public IEnumerator PhysicsCar_ContactFromBehind_NeverEngagesTheBumper()
        {
            (CarAgent agent, _) = BuildCarOnStraightLane(new Vector3(140000f, 0f, 0f), physics: true);
            yield return null;

            projectileGo = new GameObject("Projectile");
            projectileGo.transform.position = carGo.transform.position + Vector3.back * 4f + Vector3.up * 0.75f;
            var prb = projectileGo.AddComponent<Rigidbody>();
            prb.mass = 20f;
            prb.useGravity = false;
            projectileGo.AddComponent<SphereCollider>().radius = 0.5f;
            prb.linearVelocity = Vector3.forward * 12f;

            for (int i = 0; i < 40; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(0f, (float)GetPrivate(agent, "bumperBlockedUntil"), "A hit from behind must never stop the car.");
        }

        [UnityTest]
        public IEnumerator PhysicsCar_SideScrapeNearTheNose_NeverEngagesTheBumper()
        {
            (CarAgent agent, float laneX) = BuildCarOnStraightLane(new Vector3(160000f, 0f, 0f), physics: true);
            yield return null;

            // A dynamic body alongside the car's right flank, spanning its front quarter (the
            // car's collider is 4m long, so that quarter starts 1m ahead of its centre): contact
            // points there sit on the side, with a sideways normal -- a scrape, not a push.
            Vector3 carPosition = carGo.transform.position;
            BuildObstacle(new Vector3(laneX + 0.9f + 0.7f, 0f, carPosition.z + 1.4f), trafficObstacle: false, dynamicBody: true);
            var obstacleBody = obstacleGo.GetComponent<Rigidbody>();

            float until = Time.time + 1.5f;
            while (Time.time < until)
            {
                // Lean on the car gently -- far below the impact threshold.
                obstacleBody.linearVelocity = Vector3.left * 0.2f;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual("Driving", GetPrivate(agent, "physicsState").ToString(), "Setup sanity: a gentle lean must never become an impact.");
            Assert.AreEqual(0f, (float)GetPrivate(agent, "bumperBlockedUntil"), "A side scrape must never stop the car, even along its front quarter.");
            Assert.Greater(agent.Speed, 0.5f, "The car must keep driving past a body scraping its side.");
        }

        [UnityTest]
        public IEnumerator EnteringRecovering_DropsSpeedToZero()
        {
            (CarAgent agent, _) = BuildCarOnStraightLane(new Vector3(150000f, 0f, 0f), physics: true);

            // Let it get up to speed first, so a kept pre-impact speed would show.
            float warmup = Time.time + 2f;
            while (Time.time < warmup)
                yield return new WaitForFixedUpdate();
            Assert.Greater(agent.Speed, 1f, "Setup sanity: the car must be moving before the impact.");

            projectileGo = new GameObject("Projectile");
            projectileGo.transform.position = carGo.transform.position + Vector3.right * 4f + Vector3.up * 0.75f;
            var prb = projectileGo.AddComponent<Rigidbody>();
            prb.mass = 1500f;
            prb.useGravity = false;
            prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileGo.AddComponent<SphereCollider>().radius = 1f;
            prb.linearVelocity = Vector3.left * 40f;

            float timeout = Time.time + 5f;
            while (GetPrivate(agent, "physicsState").ToString() != "Recovering" && Time.time < timeout)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual("Recovering", GetPrivate(agent, "physicsState").ToString(), "Setup sanity: a hard side impact must trigger Recovering.");
            Assert.AreEqual(0f, agent.Speed, "On re-attach the car must pull away from rest, not at its pre-impact speed.");
        }
    }
}
