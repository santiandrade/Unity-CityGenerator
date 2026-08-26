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

        private readonly RaycastHit[] pedestrianHits = new RaycastHit[16];
        // Sized generously above the old SphereCast sensor's 16: VehicleAheadClearance now scans
        // every vehicle within the full sensorRange of this car's own position (see its comment),
        // which in a dense jam can genuinely have more than 16 vehicles in range at once.
        private readonly Collider[] overlaps = new Collider[32];
        private readonly Collider[] pedestrianOverlaps = new Collider[16];
        // Resolved lazily on first use (not OnEnable): a generated vehicle's OnEnable fires during
        // generation itself, before CityGeneratorPedestrianBuilder has created this GameObject --
        // vehicles are built first. By the time Play actually starts and the sensor first runs, the
        // full scene (including this) exists.
        private PedestrianRoadProximityGrid pedestrianRoadProximity;
        private readonly List<PedestrianAgent> pedestrianQueryResults = new();

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
        // Node the vehicle most recently departed from -- together with targetNode, identifies the
        // directed lane segment it currently occupies in TrafficNetwork.LaneOccupancy. -1 before
        // the first segment is known (nothing to report as "departed from" yet).
        private int fromNode = -1;
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
        /// <summary>Root-level proxy collider assigned by CityGeneratorColliderUtility, read by
        /// another CarAgent's <see cref="VehicleAheadClearance"/> lane-occupancy fast path to
        /// measure real surface distance instead of raw center-to-center distance.</summary>
        public Collider OwnCollider => ownCollider;

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
            if (network != null && network.LaneOccupancy != null && targetNode >= 0)
                network.LaneOccupancy.Leave(this, fromNode, targetNode);
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

            network.LaneOccupancy?.Enter(this, fromNode, targetNode);
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

        /// <summary>
        /// Clear distance to the car ahead, minus the minimum gap. Tries
        /// <see cref="TrafficNetwork.LaneOccupancy"/> first for the common "another car ahead on
        /// this same lane segment" case (item 8, stage 3) -- cheap, no physics query -- and only
        /// falls back to the SphereCast sensor below when it has no answer (empty/absent segment,
        /// a car about to turn, or genuine cross-traffic at a crossing, none of which the index
        /// attempts to resolve). The two opposing-traffic deadlock rules below only ever fire from
        /// the SphereCast path: cars sharing one directed lane segment share the same heading by
        /// construction, so they can never be "head-on" to each other.
        /// </summary>
        private float VehicleAheadClearance()
        {
            // The direction actually used to decide "ahead" everywhere below, in place of
            // transform.forward: mid-corner, RotateTowards keeps this car's heading lagging behind
            // its actual path for the whole turn, so two cars rounding the same corner a few metres
            // apart can have noticeably different transform.forward -- enough that a projection
            // using either car's live heading puts the other one "behind" it, even head-on into its
            // rear bumper. The vector to this car's own target node follows the path, not the lag,
            // so it stays reliable through the turn.
            TrafficNetwork.Node targetNodeInfo = network.GetNode(targetNode);
            Vector3 towardTarget = targetNodeInfo.Position - transform.position;
            towardTarget.y = 0f;
            towardTarget = towardTarget.sqrMagnitude > 0.0001f ? towardTarget.normalized : transform.forward;

            if (network.LaneOccupancy != null && network.LaneOccupancy.TryGetCarAhead(this, fromNode, targetNode, towardTarget, out CarAgent laneAhead))
            {
                // Measured the same way the SphereCast below measures it -- from the sensor
                // origin (2.2m ahead of this car's centre) to the other car's actual collider
                // surface -- not centre-to-centre. Centre-to-centre ignored both cars' own length,
                // so a queue converging on the same lane segment settled with `minGap` between
                // centres while their (2.5-3m long) bodies still overlapped by a couple of metres:
                // a real pile-up, not just a visual one. Falls back to centre-to-centre only if the
                // other car has no collider yet (shouldn't happen for a generated vehicle, but the
                // proxy collider is assigned by the generator, not guaranteed by this component).
                Vector3 laneOrigin = transform.position + Vector3.up * 0.9f + towardTarget * 2.2f;
                float laneDistance = laneAhead.OwnCollider != null
                    ? Vector3.Distance(laneOrigin, laneAhead.OwnCollider.ClosestPoint(laneOrigin))
                    : Vector3.Distance(transform.position, laneAhead.transform.position);
                return laneDistance <= 0.001f ? 0f : laneDistance - minGap;
            }

            // OverlapSphere centred on this car's own position, not on a point offset ahead of it --
            // a SphereCast (or an OverlapSphere at that offset point) has a blind spot for any
            // vehicle whose body spans past the offset point without its surface actually touching
            // the small sensor sphere there, which a long vehicle (a Truck/Garbage-Truck a couple
            // of metres ahead) triggers easily: the cast/overlap finds nothing, this car never
            // brakes, and it drives straight into it. Scanning from the car's own centre out to the
            // full sensorRange has no such gap; forward/lateral projection below re-applies the same
            // narrow forward cone a directional cast gave for free, so a car in an adjacent lane
            // still isn't mistaken for one ahead in this one.
            Vector3 egoPosition = transform.position;
            Vector3 origin = egoPosition + Vector3.up * 0.9f + towardTarget * 2.2f;
            int count = Physics.OverlapSphereNonAlloc(egoPosition + Vector3.up * 0.9f, sensorRange, overlaps,
                vehicleMask, QueryTriggerInteraction.Ignore);

            if (count == overlaps.Length)
            {
                Debug.LogWarning($"{name}: forward sensor hit its {overlaps.Length}-collider limit; the closest vehicle may be missing from this frame's results.", this);
            }

            float clearance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                Vector3 offset = collider.transform.position - egoPosition;
                float along = Vector3.Dot(offset, towardTarget);
                if (along <= 0f)
                {
                    continue;
                }

                Vector3 lateral = offset - along * towardTarget;
                if (lateral.sqrMagnitude > sensorRadius * sensorRadius)
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, collider.ClosestPoint(origin));
                clearance = ProcessVehicleCollider(collider, distance, clearance);
            }

            return clearance;
        }

        /// <summary>
        /// Shared by the SphereCast sweep and its OverlapSphere fallback in
        /// <see cref="VehicleAheadClearance"/>: resolves a hit collider to the other <see cref="CarAgent"/>,
        /// applies the head-on deadlock rule, and folds its gap into <paramref name="clearance"/>.
        /// </summary>
        private float ProcessVehicleCollider(Collider collider, float distance, float clearance)
        {
            // Discarded by identity, never by distance: a zero-distance hit is
            // the car's own collider, but also the car already bumper-to-bumper ahead, and
            // filtering by distance made it invisible and drove into it. A hit collider that
            // isn't in ColliderRegistry at all (rather than resolving to `this`) can no longer
            // be a car with its collider buried in a child, out of the sensor's layer mask —
            // CityGeneratorColliderUtility guarantees every generated vehicle has a root-level,
            // correctly-layered collider; an unregistered hit here is unrelated scene geometry.
            if (!ColliderRegistry.TryGetValue(collider.GetEntityId(), out CarAgent other) || other == this)
            {
                return clearance;
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
                    return clearance;
                }
            }

            float gap = distance <= 0.001f ? 0f : distance - minGap;
            return Mathf.Min(clearance, gap);
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
            if (pedestrianRoadProximity == null)
                pedestrianRoadProximity = FindAnyObjectByType<PedestrianRoadProximityGrid>();

            // Item 8, stage 4: once the pedestrian count justifies it, query the shared proximity
            // grid instead of casting -- falls back to the SphereCast below whenever the grid
            // doesn't exist (no PedestrianManager in the scene, e.g. Include Pedestrians off) or
            // hasn't reported enough agents yet.
            if (pedestrianRoadProximity != null && pedestrianRoadProximity.HasEnoughAgents)
                return PedestrianAheadClearanceFromGrid();

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

            // See the `overlaps` field comment on VehicleAheadClearance: catches a pedestrian (or
            // the player, on the same layer) already flush against the sensor origin, which the
            // sweep above silently misses.
            if (Physics.OverlapSphereNonAlloc(origin, sensorRadius, pedestrianOverlaps, pedestrianMask,
                    QueryTriggerInteraction.Collide) > 0)
            {
                clearance = 0f;
            }

            return clearance;
        }

        /// <summary>
        /// Same intent as the SphereCast fallback above, answered from
        /// <see cref="PedestrianRoadProximityGrid"/> instead: only pedestrians roughly ahead (a
        /// forward-dot check, mirroring the cast's cone) within sensorRange count. The player is on
        /// the same Pedestrian layer as every NPC and the SphereCast sensor always detected it, but
        /// the player is never itself a PedestrianAgent, so it can never be one of the grid's own
        /// bucketed entries -- checked separately via TryGetPlayerPosition instead.
        /// </summary>
        private float PedestrianAheadClearanceFromGrid()
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f + transform.forward * 2.2f;
            pedestrianRoadProximity.QueryNear(origin, sensorRange, pedestrianQueryResults);

            // The "ahead" cone is measured from this car's own centre, not from `origin`: origin
            // already sits 2.2m ahead of centre, so a pedestrian closer than that to the car (the
            // exact "standing right at the bumper" case) projects *behind* origin along forward,
            // scoring a negative dot and getting filtered out as "not ahead" -- never detected, no
            // matter how close. Distance/gap still uses `origin`, matching the SphereCast fallback's
            // calibration of minGap.
            Vector3 carPosition = transform.position;
            float clearance = float.MaxValue;
            for (int i = 0; i < pedestrianQueryResults.Count; i++)
            {
                if (!IsPedestrianInPath(pedestrianQueryResults[i].transform.position, carPosition, out float distance))
                    continue;

                float gap = distance <= 0.001f ? 0f : distance - minGap;
                clearance = Mathf.Min(clearance, gap);
            }

            if (pedestrianRoadProximity.TryGetPlayerPosition(out Vector3 playerPosition)
                && IsPedestrianInPath(playerPosition, carPosition, out float playerDistance))
            {
                float gap = playerDistance <= 0.001f ? 0f : playerDistance - minGap;
                clearance = Mathf.Min(clearance, gap);
            }

            return clearance;

            bool IsPedestrianInPath(Vector3 pedestrianPosition, Vector3 egoPosition, out float distanceFromOrigin)
            {
                distanceFromOrigin = 0f;
                Vector3 fromCar = pedestrianPosition - egoPosition;
                if (fromCar.sqrMagnitude > 0.001f)
                {
                    // Lateral cutoff on top of the forward cone: without it, a pedestrian standing
                    // still on the sidewalk kerb -- never stepping onto the road -- reads as "ahead"
                    // for several metres of approach (a wide cone reaches far to the side at range)
                    // and the car brakes hard for someone who was never in its way. PedestrianNetwork
                    // places a kerb roughly one lane-width-and-a-bit out from this car's own lane
                    // centre; 1.6m keeps a pedestrian already in or stepping into this lane covered
                    // while excluding one still waiting at the kerb.
                    Vector3 direction = fromCar.normalized;
                    if (Vector3.Dot(direction, transform.forward) < 0.5f)
                        return false;

                    Vector3 lateral = fromCar - Vector3.Dot(fromCar, transform.forward) * transform.forward;
                    if (lateral.sqrMagnitude > 1.6f * 1.6f)
                        return false;
                }

                distanceFromOrigin = Vector3.Distance(pedestrianPosition, origin);
                return distanceFromOrigin <= sensorRange;
            }
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
                network.LaneOccupancy?.Leave(this, fromNode, targetNode);
                fromNode = targetNode;
                targetNode = next;
                network.LaneOccupancy?.Enter(this, fromNode, targetNode);
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
