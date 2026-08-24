using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>NPC states. Interacting is reserved for a future spec (peer/player interaction) and never entered.</summary>
    public enum PedestrianState { Walking, WaitingToCross, Idling, Interacting }

    /// <summary>
    /// Walks the <see cref="PedestrianNetwork"/> graph from destination to destination, moving by
    /// transform (no CharacterController/Rigidbody, mirroring CarAgent). Waits at a curb until the
    /// crossing ahead is clear, and occasionally stops idle or at a point of interest before
    /// picking a new destination. Animates with the same Speed/Grounded mapping PlayerController
    /// uses, so it shares CharacterAnimator.controller's Locomotion blend tree unmodified.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PedestrianAgent : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private PedestrianNetwork network;

        [Header("Movement")]
        [Tooltip("Speed at which CharacterAnimator.controller's Locomotion blend tree reaches Speed = 0.5 (matches PlayerWalkSpeed exactly) — a calibration anchor, not this pedestrian's own pace.")]
        [SerializeField] private float walkReferenceSpeed = 4f;

        [Tooltip("Speed at which the blend tree reaches Speed = 1 (matches PlayerRunSpeed exactly) — the other calibration anchor.")]
        [SerializeField] private float runReferenceSpeed = 8f;

        [Tooltip("Most pedestrians stroll at this fraction of walkReferenceSpeed, not a full player-paced walk.")]
        [SerializeField] private float paceFraction = 0.5f;

        [Tooltip("Chance a pedestrian is a 'runner' (jogging, or late) instead of a regular stroller, moving at runReferenceSpeed.")]
        [SerializeField] private float runnerChance = 0.15f;

        [Tooltip("Per-instance +-fraction speed jitter, rolled once at spawn.")]
        [SerializeField] private float speedJitter = 0.1f;

        [Tooltip("Per-instance +-lateral offset from the path centreline, rolled once at spawn, so parallel walkers don't render as a single file line.")]
        [SerializeField] private float lateralJitter = 0.4f;

        [SerializeField] private float rotationSpeed = 360f;
        [SerializeField] private float arriveRadius = 0.3f;

        [Header("Stops")]
        [SerializeField] private float idleStopChance = 0.3f;
        [SerializeField] private float idleStopDurationMin = 2f;
        [SerializeField] private float idleStopDurationMax = 6f;
        [SerializeField] private float poiStopDurationMin = 5f;
        [SerializeField] private float poiStopDurationMax = 15f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");

        private Animator animator;
        private PedestrianManager manager;
        private PedestrianState state = PedestrianState.Walking;

        // A simple shortest path never revisits a node, so network.NodeCount is a hard upper
        // bound on its length — sizing the buffer to it (allocated once, here, after `network` is
        // known) means FindPath can only ever refuse a request for being genuinely unreachable,
        // never for a route that's merely long. A fixed constant undercounted this badly: this
        // graph has many hops per metre (a single street crossing alone costs 4), so with
        // PlanNewDestination now deliberately biasing towards far destinations, routes routinely
        // ran into a fixed-size cap that a physical-distance estimate didn't anticipate.
        private int[] path;
        private int pathLength;
        private int pathIndex;
        private int currentNode = -1;

        private float effectiveSpeed;
        // Animator Speed matching effectiveSpeed, computed once at spawn with the same
        // proportional mapping PlayerController uses (0..0.5 across the walk range, 0.5..1
        // across the run range) so the shared blend tree never foot-slides regardless of pace.
        private float normalizedSpeed;
        private float lateralOffset;
        private float stopUntilTime;
        private float distanceTravelled;

        public PedestrianState State => state;
        public int CurrentNode => currentNode;
        /// <summary>Total distance walked so far, for diagnosing stuck/jammed agents — mirrors CarAgent.DistanceTravelled.</summary>
        public float DistanceTravelled => distanceTravelled;

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        private void Start()
        {
            if (network == null)
            {
                network = FindAnyObjectByType<PedestrianNetwork>();
            }

            if (network == null)
            {
                Debug.LogError($"{name}: no PedestrianNetwork found in the scene.", this);
                enabled = false;
                return;
            }

            path = new int[network.NodeCount];

            bool isRunner = Random.value < runnerChance;
            float basePace = isRunner ? runReferenceSpeed : walkReferenceSpeed * paceFraction;
            effectiveSpeed = basePace * (1f + Random.Range(-speedJitter, speedJitter));
            normalizedSpeed = effectiveSpeed <= walkReferenceSpeed
                ? Mathf.Lerp(0f, 0.5f, effectiveSpeed / walkReferenceSpeed)
                : Mathf.Lerp(0.5f, 1f, (effectiveSpeed - walkReferenceSpeed) / (runReferenceSpeed - walkReferenceSpeed));
            lateralOffset = Random.Range(-lateralJitter, lateralJitter);

            currentNode = network.FindNearestNode(transform.position, PedestrianNodeKind.Ring);
            if (currentNode < 0)
            {
                Debug.LogError($"{name}: no Ring node found near the spawn position.", this);
                enabled = false;
                return;
            }

            PlanNewDestination();

            // Ticked centrally by PedestrianManager rather than through this component's own
            // Update, same convention as CarAgent/TrafficManager. Falls back to finding/creating
            // one so a PedestrianAgent dropped into a scene outside the generator still walks.
            manager = PedestrianManager.Instance != null ? PedestrianManager.Instance : FindAnyObjectByType<PedestrianManager>();
            if (manager == null)
            {
                manager = new GameObject("PedestrianManager").AddComponent<PedestrianManager>();
            }
            manager.Register(this);
        }

        private void OnDisable()
        {
            if (manager != null)
            {
                manager.Unregister(this);
                manager = null;
            }
        }

        /// <summary>
        /// Advances this pedestrian by <paramref name="dt"/>. Called once per frame by
        /// PedestrianManager for every registered agent, instead of through this component's own
        /// Update. Movement and animation always run; when <paramref name="runLogic"/> is false,
        /// the arrival/replanning/crossing decisions below are skipped for this tick and picked
        /// back up next time it's true — PedestrianManager only does this for agents far from the
        /// camera, same convention as TrafficManager staggering CarAgent's sensor.
        /// </summary>
        public void Tick(float dt, bool runLogic)
        {
            switch (state)
            {
                case PedestrianState.Walking:
                    TickWalking(dt, runLogic);
                    break;
                case PedestrianState.WaitingToCross:
                    animator.SetFloat(SpeedHash, 0f);
                    if (runLogic)
                    {
                        TryResumeCrossing();
                    }
                    break;
                case PedestrianState.Idling:
                    animator.SetFloat(SpeedHash, 0f);
                    if (runLogic && Time.time >= stopUntilTime)
                    {
                        PlanNewDestination();
                    }
                    break;
            }

            animator.SetBool(GroundedHash, true);
        }

        private void TickWalking(float dt, bool runLogic)
        {
            Vector3 targetPosition = AimPoint(pathIndex);
            MoveTowards(targetPosition, dt);
            animator.SetFloat(SpeedHash, normalizedSpeed);

            if (!runLogic)
            {
                return;
            }

            if (Vector3.Distance(transform.position, targetPosition) > arriveRadius)
            {
                return;
            }

            currentNode = path[pathIndex];
            pathIndex++;

            if (pathIndex >= pathLength)
            {
                OnReachedDestination();
                return;
            }

            int nextNode = path[pathIndex];
            PedestrianNode arrived = network.GetNode(currentNode);
            PedestrianNode next = network.GetNode(nextNode);
            if (arrived.Kind == PedestrianNodeKind.Curb && next.Kind == PedestrianNodeKind.Crossing && !network.CanCross(nextNode))
            {
                state = PedestrianState.WaitingToCross;
            }
        }

        private void TryResumeCrossing()
        {
            int nextNode = path[pathIndex];
            if (network.CanCross(nextNode))
            {
                state = PedestrianState.Walking;
            }
        }

        private void OnReachedDestination()
        {
            PedestrianNode arrived = network.GetNode(currentNode);
            if (arrived.Kind == PedestrianNodeKind.PointOfInterest)
            {
                if (arrived.LookAt.HasValue)
                {
                    Vector3 lookDir = arrived.LookAt.Value - transform.position;
                    lookDir.y = 0f;
                    if (lookDir.sqrMagnitude > 0.0001f)
                    {
                        transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                    }
                }

                StartIdling(poiStopDurationMin, poiStopDurationMax);
                return;
            }

            if (Random.value < idleStopChance)
            {
                StartIdling(idleStopDurationMin, idleStopDurationMax);
                return;
            }

            PlanNewDestination();
        }

        private void StartIdling(float min, float max)
        {
            state = PedestrianState.Idling;
            stopUntilTime = Time.time + Random.Range(min, max);
        }

        // Rolling several random candidates and trying them farthest-first (straight-line) biases
        // every route towards being long, instead of an NPC shuffling between adjacent blocks.
        // Reused across calls so ranking them doesn't allocate a new array every replan.
        private const int DestinationCandidateAttempts = 8;
        private readonly int[] candidateNodes = new int[DestinationCandidateAttempts];
        private readonly float[] candidateSqrDistances = new float[DestinationCandidateAttempts];

        /// <summary>
        /// Picks a new, deliberately-far destination and plans a path to it from the current node.
        /// Tries every candidate farthest-first rather than committing to only the single farthest
        /// one: a block with just one link to the rest of the network (e.g. a grid corner block,
        /// whose ring touches only one interior intersection) would otherwise see its farthest
        /// random draw be unreachable almost every time — since virtually every node in a large
        /// graph is "far" from it — leaving the agent idling forever instead of ever walking.
        /// </summary>
        private void PlanNewDestination()
        {
            Vector3 fromPosition = network.GetNode(currentNode).Position;
            int candidateCount = 0;

            for (int attempt = 0; attempt < DestinationCandidateAttempts; attempt++)
            {
                int candidate = network.PickRandomDestination();
                if (candidate < 0 || candidate == currentNode)
                {
                    continue;
                }

                candidateNodes[candidateCount] = candidate;
                candidateSqrDistances[candidateCount] = (network.GetNode(candidate).Position - fromPosition).sqrMagnitude;
                candidateCount++;
            }

            // Simple insertion sort (descending by distance): candidateCount is at most
            // DestinationCandidateAttempts, so this is cheaper than allocating for a real sort.
            for (int i = 1; i < candidateCount; i++)
            {
                int node = candidateNodes[i];
                float sqrDistance = candidateSqrDistances[i];
                int j = i - 1;
                while (j >= 0 && candidateSqrDistances[j] < sqrDistance)
                {
                    candidateNodes[j + 1] = candidateNodes[j];
                    candidateSqrDistances[j + 1] = candidateSqrDistances[j];
                    j--;
                }
                candidateNodes[j + 1] = node;
                candidateSqrDistances[j + 1] = sqrDistance;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                pathLength = network.FindPath(currentNode, candidateNodes[i], path);
                if (pathLength > 1)
                {
                    pathIndex = 1; // path[0] == currentNode
                    state = PedestrianState.Walking;
                    return;
                }
            }

            // None of this round's candidates were reachable (e.g. an isolated block on a 1xN
            // grid): settle briefly and try again with a fresh random draw instead of spinning.
            StartIdling(1f, 2f);
        }

        /// <summary>
        /// Aim point for the current path segment, offset sideways by this instance's lateral
        /// jitter. The perpendicular direction is derived from the segment's own fixed endpoints
        /// (previous node -> target node), never from the agent's live position: deriving it from
        /// "vector to target" instead is unstable at close range (a short vector's direction spins
        /// wildly from tiny position changes), which made the agent's facing flip back and forth
        /// right before arriving at every node.
        /// </summary>
        private Vector3 AimPoint(int targetPathIndex)
        {
            Vector3 nodePosition = network.GetNode(path[targetPathIndex]).Position;
            Vector3 fromPosition = currentNode >= 0 ? network.GetNode(currentNode).Position : transform.position;
            Vector3 travelDir = nodePosition - fromPosition;
            travelDir.y = 0f;
            if (travelDir.sqrMagnitude < 0.0001f)
            {
                return nodePosition;
            }

            Vector3 perpendicular = new(travelDir.z, 0f, -travelDir.x);
            return nodePosition + perpendicular.normalized * lateralOffset;
        }

        private void MoveTowards(Vector3 targetPosition, float dt)
        {
            Vector3 flatDir = targetPosition - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, rotationSpeed * dt);
            }

            Vector3 nextPosition = Vector3.MoveTowards(transform.position, targetPosition, effectiveSpeed * dt);
            distanceTravelled += Vector3.Distance(transform.position, nextPosition);
            transform.position = nextPosition;
        }
    }
}
