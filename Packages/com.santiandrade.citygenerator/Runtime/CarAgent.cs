using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Vehicle that travels along the <see cref="TrafficNetwork"/>: follows the lane graph,
    /// picks random turns at each crossing, stops at a traffic light that isn't green,
    /// and keeps its distance from the car ahead. Movement is continuous: it accelerates and
    /// brakes progressively, never teleporting or stopping for good.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarAgent : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private TrafficNetwork network;

        [Header("Driving")]
        [SerializeField] private float maxSpeed = 9f;
        [SerializeField] private float acceleration = 5f;
        [SerializeField] private float braking = 14f;
        [Tooltip("Turn speed in degrees per second. Determines the radius of the corners.")]
        [SerializeField] private float turnSpeed = 230f;
        [Tooltip("Fraction of max speed kept while taking a corner.")]
        [SerializeField] private float cornerSpeedFactor = 0.45f;

        [Header("Nodes")]
        [SerializeField] private float arriveRadius = 1.5f;

        [Header("Detection")]
        [SerializeField] private LayerMask vehicleMask = ~0;
        [Tooltip("Independent from vehicleMask: assigned per instance by CityGeneratorPedestrianBuilder, mirroring how vehicleMask is assigned per instance by CityGeneratorTrafficBuilder.")]
        [SerializeField] private LayerMask pedestrianMask;
        [SerializeField] private float sensorRange = 12f;
        [SerializeField] private float sensorRadius = 0.7f;
        [SerializeField] private float minGap = 2.2f;
        [Tooltip("Distance from which the car starts braking for an obstacle or a red light.")]
        [SerializeField] private float brakeDistance = 10f;

        [Header("Unsignalled crossings")]
        [Tooltip("Distance from the stop line at which the car slows down when approaching an unsignalled crossing.")]
        [SerializeField] private float yieldDistance = 12f;

        [Tooltip("Distance from the stop line at which the crossing's priority is claimed.")]
        [SerializeField] private float claimDistance = 5f;

        [SerializeField] private float yieldSpeed = 4.5f;

        [Header("Deadlock recovery")]
        [Tooltip("Seconds stopped before trying to break a nose-to-nose deadlock.")]
        [SerializeField] private float deadlockTimeout = 6f;

        [Tooltip("Seconds a car keeps pushing through once it starts breaking a deadlock.")]
        [SerializeField] private float deadlockBreakDuration = 4f;

        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly RaycastHit[] pedestrianHits = new RaycastHit[16];

        // Own identifier for crossing reservations; 0 means "crossing free".
        private static int nextCarId = 1;

        // Every generated vehicle instance carries exactly one collider on its root — either the
        // prefab's own, or a proxy added by CityGeneratorColliderUtility.EnsureNonTriggerCollider
        // when the prefab has none there (even if it has one deeper in its hierarchy: a collider
        // buried in a child would otherwise never be found here, and never match this instance's
        // own layer either, making the vehicle invisible to every other car's sensor) — so looking
        // up the CarAgent for a sensor hit by GetComponentInParent every frame, for every hit, for
        // every car is needless hierarchy walking: register/deregister against the collider's
        // instance ID instead. Reset on domain-reload-disabled Play sessions too (see ResetCarIdCounter).
        private static readonly Dictionary<EntityId, CarAgent> ColliderRegistry = new();

        private Collider ownCollider;
        private TrafficManager trafficManager;
        private int targetNode = -1;
        private int reservedIntersection = -1;
        private int carId;
        private float speed;
        private float distanceTravelled;
        private float stoppedTime;
        // Cached result of the forward sensor, reused on frames TrafficManager skips it for a
        // car far from the camera (see TrafficManager.staggerDistance).
        private float lastAheadClearance = float.MaxValue;
        // Same staggering as lastAheadClearance: refreshed only on frames TrafficManager runs
        // the sensor for this car (see TrafficManager.staggerDistance).
        private float lastPedestrianClearance = float.MaxValue;
        private StopReason stopReason;
        // Previous frame's reason: the current one is still being computed while the
        // forward sensor runs, so the deadlock check has to look at the last known value.
        private StopReason lastStopReason;
        // Latched deadlock override. It has to outlive stoppedTime, which resets the moment the
        // car pulls away: keying the override on stoppedTime alone made it cancel itself one frame
        // after it started working, and the pair crawled forward centimetre by centimetre instead.
        private float breakingDeadlockUntil;
        private bool approachingUnsignalled;

        /// <summary>Reason the vehicle is braking, useful for debugging jams.</summary>
        public enum StopReason
        {
            None,
            TrafficLight,
            Priority,
            VehicleAhead
        }

        public float Speed => speed;
        /// <summary>Identifier used for crossing reservations and to break ties in deadlocks.</summary>
        public int CarId => carId;
        public int TargetNode => targetNode;
        public int ReservedIntersection => reservedIntersection;
        public float DistanceTravelled => distanceTravelled;
        /// <summary>Seconds it has been continuously stopped for.</summary>
        public float StoppedTime => stoppedTime;
        public StopReason CurrentStopReason => stopReason;

        // Counter reset for Play sessions with Domain Reload disabled, where a static field
        // otherwise keeps growing across sessions and breaks the carId tie-break in IsDeadlockedWith.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCarIdCounter()
        {
            nextCarId = 1;
            ColliderRegistry.Clear();
        }

        private void OnEnable()
        {
            ownCollider = GetComponent<Collider>();
            if (ownCollider != null)
                ColliderRegistry[ownCollider.GetEntityId()] = this;

            if (network == null)
            {
                network = FindAnyObjectByType<TrafficNetwork>();
            }

            // Ticked centrally by TrafficManager rather than through this component's own Update
            // (see the technical review, A.7). Resolved through the network (set on the same
            // GameObject as the manager by CityGeneratorTrafficBuilder.AddManagerComponent)
            // instead of a global static Instance, so multiple cities/networks coexisting in the
            // same scene never share, or fight over, a single manager. Falls back to
            // finding/creating one so a CarAgent dropped into a scene outside the generator still
            // drives. Register is idempotent (TrafficManager.agents is a HashSet), so re-enabling
            // an already-registered agent here is harmless.
            trafficManager = network != null && network.Manager != null ? network.Manager : FindAnyObjectByType<TrafficManager>();
            if (trafficManager == null)
            {
                trafficManager = new GameObject("TrafficManager").AddComponent<TrafficManager>();
            }
            trafficManager.Register(this);
        }

        private void OnDisable()
        {
            if (ownCollider != null)
                ColliderRegistry.Remove(ownCollider.GetEntityId());
            if (trafficManager != null)
            {
                trafficManager.Unregister(this);
                trafficManager = null;
            }
        }

        private void Start()
        {
            if (network == null)
            {
                network = FindAnyObjectByType<TrafficNetwork>();
            }

            if (network == null)
            {
                Debug.LogError($"{name}: no TrafficNetwork found in the scene.", this);
                enabled = false;
                return;
            }

            carId = nextCarId++;
            targetNode = network.FindNodeAhead(transform.position, transform.forward);
            if (targetNode < 0)
            {
                Debug.LogError($"{name}: no network node found ahead of the vehicle.", this);
                enabled = false;
                return;
            }
        }

        /// <summary>
        /// Advances this vehicle by <paramref name="dt"/>. Called once per frame by
        /// <see cref="TrafficManager"/> for every registered car, instead of through this
        /// component's own Update. When <paramref name="runSensor"/> is false the forward-sensor
        /// SphereCast is skipped and the previous frame's clearance is reused — TrafficManager
        /// only does this for cars far from the camera once enough cars are registered.
        /// </summary>
        public void Tick(float dt, bool runSensor)
        {
            TrafficNetwork.Node node = network.GetNode(targetNode);

            Vector3 toTarget = node.Position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            lastStopReason = stopReason;
            stopReason = StopReason.None;
            approachingUnsignalled = false;

            float clearance = float.MaxValue;
            if (node.IsEntry)
            {
                clearance = IntersectionClearance(node, distance);
            }

            if (runSensor)
            {
                lastAheadClearance = VehicleAheadClearance();
                lastPedestrianClearance = PedestrianAheadClearance();
            }

            // A detected pedestrian is treated exactly like a car ahead: same progressive
            // braking, no dedicated StopReason or state of its own.
            float aheadClearance = Mathf.Min(lastAheadClearance, lastPedestrianClearance);
            if (aheadClearance < clearance)
            {
                clearance = aheadClearance;
                stopReason = StopReason.VehicleAhead;
            }

            ReleaseStaleReservation(node);
            ReleaseReservationWhileBlocked();

            // Target speed: the maximum, trimmed down when approaching an obstacle and on corners.
            float targetSpeed = maxSpeed;
            if (clearance < brakeDistance)
            {
                targetSpeed = maxSpeed * Mathf.Clamp01(clearance / brakeDistance);
            }

            if (approachingUnsignalled)
            {
                targetSpeed = Mathf.Min(targetSpeed, yieldSpeed);
            }

            // Steering runs even while stopped, on purpose: a car waiting at a crossing lines
            // itself up with the exit it picked, so when it pulls away it tracks its own lane
            // instead of swinging wide through the middle of the crossing and into oncoming traffic.
            Quaternion rotation = transform.rotation;
            if (distance > 0.05f)
            {
                Vector3 desired = toTarget / distance;
                float turn = Vector3.Angle(transform.forward, desired);
                if (turn > 25f)
                {
                    targetSpeed = Mathf.Min(targetSpeed, maxSpeed * cornerSpeedFactor);
                }

                rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(desired, Vector3.up), turnSpeed * dt);
            }

            if (clearance < 0.4f)
            {
                targetSpeed = 0f;
            }

            float rate = targetSpeed > speed ? acceleration : braking;
            speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);

            float step = speed * dt;
            distanceTravelled += step;
            stoppedTime = speed < 0.3f ? stoppedTime + dt : 0f;

            // Uses the just-computed rotation's forward, not transform.forward: matches the
            // original behaviour where rotation was applied before this was read.
            Vector3 position = transform.position + (rotation * Vector3.forward) * step;
            position.y = 0f;
            transform.SetPositionAndRotation(position, rotation);

            if (distance < arriveRadius)
            {
                AdvanceToNextNode();
            }
        }

        /// <summary>
        /// Clear distance to the stop line of the crossing the vehicle is approaching:
        /// infinite if it has right of way, and the remaining distance to the line if it doesn't.
        /// Once past the line the vehicle keeps going, so it never stops inside the crossing.
        /// </summary>
        private float IntersectionClearance(TrafficNetwork.Node node, float distance)
        {
            float toStopLine = distance - network.StopLineBack;
            if (toStopLine < -0.5f)
            {
                return float.MaxValue;
            }

            bool mustStop;
            TrafficLightState? state = network.LightState(targetNode);
            if (state.HasValue)
            {
                // On amber only a car that still has enough room to brake comfortably stops.
                mustStop = state.Value == TrafficLightState.Red
                           || (state.Value == TrafficLightState.Amber && toStopLine > 5f);
                if (mustStop)
                {
                    stopReason = StopReason.TrafficLight;
                }
            }
            else if (toStopLine < yieldDistance)
            {
                // Unsignalled crossing: slows down like at a give-way sign, and priority is only
                // claimed right at the stop line, once the vehicle can cross immediately.
                // Claiming it from further away held it unused and blocked everyone else.
                approachingUnsignalled = true;
                mustStop = toStopLine < claimDistance && !network.TryReserve(node.Intersection, carId);
                if (mustStop)
                {
                    stopReason = StopReason.Priority;
                }
                else if (toStopLine < claimDistance)
                {
                    reservedIntersection = node.Intersection;
                }
            }
            else
            {
                mustStop = false;
            }

            return mustStop ? toStopLine : float.MaxValue;
        }

        /// <summary>Clear distance to the car ahead, minus the minimum gap.</summary>
        private float VehicleAheadClearance()
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f + transform.forward * 2.2f;
            int count = Physics.SphereCastNonAlloc(origin, sensorRadius, transform.forward, hits,
                sensorRange, vehicleMask, QueryTriggerInteraction.Ignore);

            if (count == hits.Length)
            {
                Debug.LogWarning($"{name}: forward sensor hit its {hits.Length}-collider limit; the closest vehicle may be missing from this frame's results.", this);
            }

            float clearance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                // Discarded by identity, never by distance: a zero-distance hit is
                // the car's own collider, but also the car already bumper-to-bumper ahead, and
                // filtering by distance made it invisible and drove into it. A hit collider that
                // isn't in ColliderRegistry at all (rather than resolving to `this`) can no longer
                // be a car with its collider buried in a child, out of the sensor's layer mask —
                // CityGeneratorColliderUtility guarantees every generated vehicle has a root-level,
                // correctly-layered collider; an unregistered hit here is unrelated scene geometry.
                if (!ColliderRegistry.TryGetValue(hits[i].collider.GetEntityId(), out CarAgent other) || other == this)
                {
                    continue;
                }

                // Only head-on obstacles can deadlock: regular traffic never produces one
                // (opposing lanes are separated by laneOffset), so this cannot fire on a queue.
                if (Vector3.Dot(transform.forward, other.transform.forward) < -0.3f)
                {
                    if (IsDeadlockedWith(other))
                    {
                        breakingDeadlockUntil = Time.time + deadlockBreakDuration;
                    }

                    if (Time.time < breakingDeadlockUntil)
                    {
                        continue;
                    }
                }

                float gap = hits[i].distance <= 0.001f ? 0f : hits[i].distance - minGap;
                clearance = Mathf.Min(clearance, gap);
            }

            return clearance;
        }

        /// <summary>
        /// Clear distance to the nearest detected pedestrian, minus the minimum gap. Independent
        /// of vehicleMask/VehicleAheadClearance: pedestrian and player colliders sit on their own
        /// Pedestrian layer (see pedestrianMask), and QueryTriggerInteraction.Collide is set
        /// explicitly here rather than relying on the project's global trigger-query setting —
        /// harmless for pedestrians/the player, whose colliders are solid, not triggers.
        /// </summary>
        private float PedestrianAheadClearance()
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f + transform.forward * 2.2f;
            int count = Physics.SphereCastNonAlloc(origin, sensorRadius, transform.forward, pedestrianHits,
                sensorRange, pedestrianMask, QueryTriggerInteraction.Collide);

            if (count == pedestrianHits.Length)
            {
                Debug.LogWarning($"{name}: pedestrian sensor hit its {pedestrianHits.Length}-collider limit; the closest pedestrian may be missing from this frame's results.", this);
            }

            float clearance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float gap = pedestrianHits[i].distance <= 0.001f ? 0f : pedestrianHits[i].distance - minGap;
                clearance = Mathf.Min(clearance, gap);
            }

            return clearance;
        }

        /// <summary>
        /// Whether this car and a head-on obstacle are in a mutual deadlock. Two cars whose paths
        /// cross inside a crossing can end up nose to nose, each one seeing the other and setting
        /// its own speed to zero: that state is absorbing, nothing else in the logic undoes it,
        /// and the queues behind them grow until the whole city stops.
        ///
        /// The lower <see cref="CarId"/> pulls away first so only one of the pair moves; if that
        /// one is itself blocked and the jam outlives three timeouts, the tie-break is dropped so
        /// the deadlock always resolves.
        /// </summary>
        private bool IsDeadlockedWith(CarAgent other)
        {
            if (stoppedTime < deadlockTimeout || lastStopReason != StopReason.VehicleAhead)
            {
                return false;
            }

            return carId < other.CarId || stoppedTime > deadlockTimeout * 3f;
        }

        /// <summary>
        /// Releases priority if the vehicle holding it has stopped for another reason
        /// and hasn't entered the crossing yet: holding it while waiting for another car is what
        /// caused the mutual deadlock. If it's already inside the crossing it keeps it until leaving.
        /// </summary>
        private void ReleaseReservationWhileBlocked()
        {
            if (reservedIntersection < 0 || speed > 0.5f || stopReason != StopReason.VehicleAhead)
            {
                return;
            }

            Vector3 centre = network.IntersectionCentre(reservedIntersection);
            if (Vector3.Distance(transform.position, centre) > 7f)
            {
                network.Release(reservedIntersection, carId);
                reservedIntersection = -1;
            }
        }

        /// <summary>Releases a crossing's priority as soon as the vehicle has moved away from it.</summary>
        private void ReleaseStaleReservation(TrafficNetwork.Node node)
        {
            if (reservedIntersection < 0 || reservedIntersection == node.Intersection)
            {
                return;
            }

            Vector3 centre = network.IntersectionCentre(reservedIntersection);
            if (Vector3.Distance(transform.position, centre) > 10f)
            {
                network.Release(reservedIntersection, carId);
                reservedIntersection = -1;
            }
        }

        private void AdvanceToNextNode()
        {
            int next = network.PickNextNode(targetNode);
            if (next < 0)
            {
                // No exits (shouldn't happen on a grid): fall back to the nearest lane.
                next = network.FindNodeAhead(transform.position, transform.forward);
            }

            if (next >= 0)
            {
                targetNode = next;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (network == null || targetNode < 0)
            {
                return;
            }

            TrafficNetwork.Node node = network.GetNode(targetNode);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + Vector3.up, node.Position + Vector3.up);
            Gizmos.DrawWireSphere(node.Position + Vector3.up * 0.3f, 0.6f);

            if (node.IsEntry)
            {
                Gizmos.color = Color.red;
                Vector3 stop = network.StopLinePosition(targetNode);
                Gizmos.DrawLine(stop + Vector3.up * 0.1f, stop + Vector3.up * 1.5f);
            }
        }
    }
}
