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

        // Own identifier for crossing reservations; 0 means "crossing free".
        private static int nextCarId = 1;

        // Every vehicle prefab carries a single BoxCollider on its root, so looking up the
        // CarAgent for a sensor hit by GetComponentInParent every frame, for every hit, for every
        // car is needless hierarchy walking: register/deregister against the collider's instance
        // ID instead. Reset on domain-reload-disabled Play sessions too (see ResetCarIdCounter).
        private static readonly Dictionary<EntityId, CarAgent> ColliderRegistry = new();

        private Collider ownCollider;
        private int targetNode = -1;
        private int reservedIntersection = -1;
        private int carId;
        private float speed;
        private float distanceTravelled;
        private float stoppedTime;
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
        }

        private void OnDisable()
        {
            if (ownCollider != null)
                ColliderRegistry.Remove(ownCollider.GetEntityId());
        }

        private void Start()
        {
            if (network == null)
            {
                network = FindFirstObjectByType<TrafficNetwork>();
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
            }
        }

        private void Update()
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

            float ahead = VehicleAheadClearance();
            if (ahead < clearance)
            {
                clearance = ahead;
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
                    Quaternion.LookRotation(desired, Vector3.up), turnSpeed * Time.deltaTime);
            }

            if (clearance < 0.4f)
            {
                targetSpeed = 0f;
            }

            float rate = targetSpeed > speed ? acceleration : braking;
            speed = Mathf.MoveTowards(speed, targetSpeed, rate * Time.deltaTime);

            float step = speed * Time.deltaTime;
            distanceTravelled += step;
            stoppedTime = speed < 0.3f ? stoppedTime + Time.deltaTime : 0f;

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
                // filtering by distance made it invisible and drove into it.
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
