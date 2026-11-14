using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Ticks every registered <see cref="PedestrianAgent"/> from a single <c>Update</c> instead of
    /// each NPC paying Unity's per-component Update marshalling cost individually — mirrors
    /// <see cref="TrafficManager"/>. Once enough pedestrians are registered it also staggers each
    /// agent's decision logic for ones far from the main camera. On top of that it rebuilds a
    /// coarse spatial grid every frame and applies a small local separation nudge between agents
    /// that end up too close, so a crowd doesn't visibly overlap — the extent of peer interaction
    /// implemented so far; it's also the base a future spec can build richer interaction on.
    ///
    /// SPEC 20: distances are measured from <see cref="SimulationFocus"/> when one is set (the
    /// main camera otherwise); an agent neither near the focus nor visible can be staggered too;
    /// root colliders beyond <see cref="ColliderRadius"/> of the focus can be switched off while
    /// cars find pedestrians through the proximity grid. All off by default.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianManager : MonoBehaviour
    {
        [Tooltip("Decision-logic staggering only activates once this many pedestrians are registered.")]
        [SerializeField] private int staggerMinAgentCount = 60;

        [Tooltip("Agents farther than this from the main camera only run their decision logic 1 out of StaggerFrames frames once staggering is active.")]
        [SerializeField] private float staggerDistance = 60f;

        [SerializeField] private int staggerFrames = 4;

        [Header("Local separation")]
        [Tooltip("Spatial grid cell size used to find nearby agents cheaply.")]
        [SerializeField] private float cellSize = 8f;

        [Tooltip("Agents closer than this push each other apart.")]
        [SerializeField] private float separationRadius = 0.6f;

        [SerializeField] private float separationStrength = 2f;

        [Header("Player avoidance")]
        [Tooltip("Agents closer than this to the player get pushed sideways, same shape as pedestrian-pedestrian separation but stronger and with a wider radius, so standing in a pedestrian's way reads as blocking them rather than merging with the crowd.")]
        [SerializeField] private float playerAvoidanceRadius = 1f;

        [SerializeField] private float playerAvoidanceStrength = 6f;

        [Header("SPEC 20 (all off by default)")]
        [Tooltip("Where staggering and collider-LOD distances are measured from. Empty: the main camera.")]
        [SerializeField] private Transform simulationFocus;

        [Tooltip("Agents farther than this from the focus that no camera renders are staggered too, even inside Stagger Distance. 0 = off.")]
        [SerializeField] private float invisibleStaggerDistance;

        [Tooltip("Agents farther than this from the focus have their root collider switched off while cars use the proximity grid (enough agents). 0 = off.")]
        [SerializeField] private float colliderRadius;

        [Tooltip("Extra distance beyond Collider Radius before a collider is switched off again.")]
        [SerializeField] private float colliderRadiusHysteresis = 5f;

        [Tooltip("Agents farther than this from the focus skip their ground raycast while they walk between two nodes on the same flat surface, and staggered ones only cast it on the frames their logic runs. 0 = off: every walking agent raycasts every frame.")]
        [SerializeField] private float groundingShortcutDistance;

        [Tooltip("This manager's PedestrianRoadProximityGrid, on the same GameObject. Set by CityGeneratorPedestrianBuilder.AddManagerComponent. Rebuilt once per frame here so CarAgent can query nearby pedestrians without a SphereCast (item 8, stage 4).")]
        [SerializeField] private PedestrianRoadProximityGrid roadProximityGrid;

        private readonly List<PedestrianAgent> agents = new();

        // SPEC 20: same purpose and naming as TrafficManager's markers. Grounding wraps each
        // agent's ground raycast (PedestrianAgent.ResolveGroundY), so it nests inside Tick.
        private static readonly ProfilerMarker TickMarker = new("CityGenerator.Pedestrians.Tick");
        private static readonly ProfilerMarker SeparationMarker = new("CityGenerator.Pedestrians.Separation");
        private static readonly ProfilerMarker ProximityGridMarker = new("CityGenerator.Pedestrians.ProximityGrid");
        internal static readonly ProfilerMarker GroundingMarker = new("CityGenerator.Pedestrians.Grounding");
        private static readonly ProfilerMarker ColliderLodMarker = new("CityGenerator.Pedestrians.ColliderLod");

        // A pedestrian walks a few centimetres a frame, so each one's collider LOD is checked one
        // frame in this many.
        private const int ColliderLodFrames = 4;
        private int disabledColliderCount;

        // Item 9: bucketed by index into `agents`, not by PedestrianAgent reference -- lets
        // ApplyLocalSeparation dedupe pairs by comparing indices instead of doing a second,
        // separate identity lookup.
        private readonly Dictionary<Vector2Int, List<int>> grid = new();
        // Only the 4 "forward" neighbour offsets (out of the full 3x3 = 9): visiting a cell pair
        // from both directions would process every agent pair twice, once from each side, exactly
        // the duplication item 9 removes. Picking one consistent half of the 8 neighbours (plus the
        // cell's own bucket, handled separately) visits each unordered cell pair exactly once.
        private static readonly Vector2Int[] ForwardNeighbourOffsets =
        {
            new(1, 0), new(1, 1), new(0, 1), new(-1, 1)
        };
        private Vector3[] separationPush = System.Array.Empty<Vector3>();
        // Same staggering condition already computed once per agent in Update, reused by
        // ApplyLocalSeparation so a pair where neither side is due for a recalculation this frame
        // is skipped entirely instead of redoing the same push it already applied last frame.
        private bool[] activeThisFrame = System.Array.Empty<bool>();
        private int frameIndex;

        [Tooltip("This manager's own PedestrianNetwork, set by CityGeneratorPedestrianBuilder.AddManagerComponent -- only used to size PathBufferPool. Deliberately not a FindAnyObjectByType lookup: with multiple independent cities/networks in the same scene (see CLAUDE.md), that could resolve a different city's network and size the pool for the wrong graph.")]
        [SerializeField] private PedestrianNetwork network;

        // Item 9: shared pool of FindPath output buffers, lazily constructed on first use (Play
        // mode only -- this field, like the pool itself, is never serialized). Every
        // PedestrianAgent rents from this instead of keeping its own permanent nodeCount-sized array.
        private PedestrianPathBufferPool pathBufferPool;
        public PedestrianPathBufferPool PathBufferPool
        {
            get
            {
                if (pathBufferPool == null)
                {
                    // `network` is only unset for a standalone PedestrianManager auto-created by
                    // PedestrianAgent.OnEnable's fallback (no generator involved) -- the generated
                    // case always pre-wires it via CityGeneratorPedestrianBuilder.AddManagerComponent.
                    PedestrianNetwork resolvedNetwork = network != null ? network : FindAnyObjectByType<PedestrianNetwork>();
                    pathBufferPool = new PedestrianPathBufferPool(resolvedNetwork != null ? resolvedNetwork.NodeCount : 0);
                }
                return pathBufferPool;
            }
        }

        // Looked up lazily rather than once in Awake: the scene's Player instance is spawned by
        // CityGeneratorSceneBuilder after PedestrianManager already exists, so an eager lookup
        // would find nothing. Cached once found since it never changes afterwards.
        private Transform playerTransform;
        // SPEC 20 (d): without a player in the scene the lookup used to run every frame.
        private float nextPlayerLookupTime;
        private const float PlayerLookupRetryInterval = 1f;

        // Contains-guarded rather than an unconditional Add: Register is called from
        // PedestrianAgent.OnEnable, so a re-enabled agent that was never Unregister-ed (SetActive
        // toggled without ever going through OnDisable in between isn't possible, but a defensive
        // guard here costs nothing and keeps the same idempotence guarantee TrafficManager's
        // HashSet gives CarAgent) must not end up ticked twice in the same frame.
        /// <summary>Live count of currently registered agents.</summary>
        public int AgentCount => agents.Count;

        public void Register(PedestrianAgent agent)
        {
            if (!agents.Contains(agent))
                agents.Add(agent);
        }

        public void Unregister(PedestrianAgent agent) => agents.Remove(agent);

        /// <summary>SPEC 20: where staggering and collider-LOD distances are measured from; null
        /// means the main camera, the behaviour before SPEC 20.</summary>
        public Transform SimulationFocus
        {
            get => simulationFocus;
            set => simulationFocus = value;
        }

        /// <summary>SPEC 20: agents beyond this distance from the focus have their root collider
        /// switched off while the proximity grid is in use. 0 (the default) keeps every collider.</summary>
        public float ColliderRadius
        {
            get => colliderRadius;
            set => colliderRadius = Mathf.Max(0f, value);
        }

        /// <summary>SPEC 20: agents beyond this distance from the focus skip the ground raycast on
        /// flat stretches and, while staggered, between their logic frames. 0 (the default) keeps
        /// one raycast per walking agent per frame.</summary>
        public float GroundingShortcutDistance
        {
            get => groundingShortcutDistance;
            set => groundingShortcutDistance = Mathf.Max(0f, value);
        }

        /// <summary>SPEC 20: sets the staggering parameters at runtime. <paramref name="invisibleDistance"/>
        /// = 0 keeps visibility out of it (the default).</summary>
        public void SetStagger(float distance, int frames, float invisibleDistance = 0f)
        {
            staggerDistance = Mathf.Max(0f, distance);
            staggerFrames = Mathf.Max(1, frames);
            invisibleStaggerDistance = Mathf.Max(0f, invisibleDistance);
        }

        /// <summary>The focus position, or false when there is neither a focus nor a main camera.</summary>
        private bool TryGetFocusPosition(out Vector3 position)
        {
            if (simulationFocus != null)
            {
                position = simulationFocus.position;
                return true;
            }

            Camera cam = Camera.main;
            position = cam != null ? cam.transform.position : Vector3.zero;
            return cam != null;
        }

        /// <summary>
        /// SPEC 20: switches root colliders off beyond colliderRadius (+ hysteresis) of the focus and
        /// back on within it -- only while the proximity grid answers CarAgent's pedestrian sensor;
        /// below that, the sensor falls back to a SphereCast that needs every collider, so all of
        /// them go back on. A collider switched back on is paired again with the physics vehicles
        /// (Unity drops a disabled collider's ignored pairs).
        /// </summary>
        private void UpdateColliderLod(bool hasFocus, Vector3 focus)
        {
            bool lodActive = colliderRadius > 0f && hasFocus && roadProximityGrid != null && roadProximityGrid.HasEnoughAgents;
            if (!lodActive && disabledColliderCount == 0)
                return;

            using var _ = ColliderLodMarker.Auto();
            float sqrInner = colliderRadius * colliderRadius;
            float outer = colliderRadius + Mathf.Max(0f, colliderRadiusHysteresis);
            float sqrOuter = outer * outer;
            int disabled = 0;

            for (int i = 0; i < agents.Count; i++)
            {
                PedestrianAgent agent = agents[i];
                bool active = agent.IsColliderActive;
                if (!lodActive)
                {
                    active = true;
                }
                else if ((frameIndex + i) % ColliderLodFrames == 0)
                {
                    float sqrDistance = (agent.transform.position - focus).sqrMagnitude;
                    if (sqrDistance <= sqrInner)
                        active = true;
                    else if (sqrDistance > sqrOuter)
                        active = false;
                }

                agent.SetColliderActive(active);
                if (!agent.IsColliderActive)
                    disabled++;
            }

            disabledColliderCount = disabled;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            bool hasFocus = TryGetFocusPosition(out Vector3 focus);
            float sqrStaggerDistance = staggerDistance * staggerDistance;
            float sqrInvisibleDistance = invisibleStaggerDistance * invisibleStaggerDistance;
            bool groundingShortcut = groundingShortcutDistance > 0f && hasFocus;
            float sqrGroundingDistance = groundingShortcutDistance * groundingShortcutDistance;

            if (activeThisFrame.Length < agents.Count)
                activeThisFrame = new bool[agents.Count];

            using (TickMarker.Auto())
            {
                for (int i = 0; i < agents.Count; i++)
                {
                    PedestrianAgent agent = agents[i];
                    bool runLogic = true;
                    float sqrDistance = hasFocus ? (agent.transform.position - focus).sqrMagnitude : 0f;

                    if (staggeringActive && hasFocus)
                    {
                        bool staggered = sqrDistance > sqrStaggerDistance
                                         || (invisibleStaggerDistance > 0f && sqrDistance > sqrInvisibleDistance && !agent.IsVisible);
                        if (staggered)
                            runLogic = (frameIndex + i) % staggerFrames == 0;
                    }

                    activeThisFrame[i] = runLogic;
                    agent.Tick(dt, runLogic, exactGrounding: !groundingShortcut || sqrDistance <= sqrGroundingDistance);
                }
            }

            UpdateColliderLod(hasFocus, focus);

            using (SeparationMarker.Auto())
            {
                ApplyLocalSeparation(dt);
                ApplyPlayerAvoidance(dt);
            }

            using (ProximityGridMarker.Auto())
                roadProximityGrid?.Rebuild(agents, staggerMinAgentCount, playerTransform);

            frameIndex++;
        }

        private void ApplyPlayerAvoidance(float dt)
        {
            if (playerTransform == null)
            {
                if (Time.time < nextPlayerLookupTime)
                    return;

                nextPlayerLookupTime = Time.time + PlayerLookupRetryInterval;
                PlayerController player = FindAnyObjectByType<PlayerController>();
                if (player == null)
                {
                    return;
                }

                playerTransform = player.transform;
            }

            Vector3 playerPosition = playerTransform.position;
            float sqrAvoidanceRadius = playerAvoidanceRadius * playerAvoidanceRadius;

            for (int i = 0; i < agents.Count; i++)
            {
                PedestrianAgent agent = agents[i];
                Vector3 offset = agent.transform.position - playerPosition;
                offset.y = 0f;
                float sqrDistance = offset.sqrMagnitude;
                if (sqrDistance < 0.0001f || sqrDistance >= sqrAvoidanceRadius)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(sqrDistance);
                Vector3 push = offset / distance * (1f - distance / playerAvoidanceRadius);
                agent.transform.position += push * playerAvoidanceStrength * dt;
            }
        }

        /// <summary>
        /// Item 9: each unordered agent pair is evaluated exactly once (instead of once per side,
        /// which computed and applied the same push twice under slightly different floating-point
        /// paths) and, when neither agent of a pair is due for a recalculation this frame (see
        /// activeThisFrame/the same staggering condition Update already computed), the pair is
        /// skipped entirely -- a stationary crowd far from the camera doesn't repeat the same
        /// push-apart work every single frame.
        /// </summary>
        private void ApplyLocalSeparation(float dt)
        {
            RebuildGrid();

            if (separationPush.Length < agents.Count)
                separationPush = new Vector3[agents.Count];
            System.Array.Clear(separationPush, 0, agents.Count);

            float sqrSeparationRadius = separationRadius * separationRadius;

            foreach (KeyValuePair<Vector2Int, List<int>> entry in grid)
            {
                Vector2Int cell = entry.Key;
                List<int> bucket = entry.Value;

                for (int a = 0; a < bucket.Count; a++)
                {
                    for (int b = a + 1; b < bucket.Count; b++)
                        AccumulateSeparationPair(bucket[a], bucket[b], sqrSeparationRadius);
                }

                for (int n = 0; n < ForwardNeighbourOffsets.Length; n++)
                {
                    if (!grid.TryGetValue(cell + ForwardNeighbourOffsets[n], out List<int> neighbourBucket))
                        continue;

                    for (int a = 0; a < bucket.Count; a++)
                    {
                        for (int b = 0; b < neighbourBucket.Count; b++)
                            AccumulateSeparationPair(bucket[a], neighbourBucket[b], sqrSeparationRadius);
                    }
                }
            }

            for (int i = 0; i < agents.Count; i++)
            {
                if (separationPush[i].sqrMagnitude > 0.0001f)
                    agents[i].transform.position += separationPush[i] * separationStrength * dt;
            }
        }

        private void AccumulateSeparationPair(int i, int j, float sqrSeparationRadius)
        {
            if (!activeThisFrame[i] && !activeThisFrame[j])
                return;

            Vector3 offset = agents[i].transform.position - agents[j].transform.position;
            offset.y = 0f;
            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance < 0.0001f || sqrDistance >= sqrSeparationRadius)
                return;

            float distance = Mathf.Sqrt(sqrDistance);
            Vector3 push = offset / distance * (1f - distance / separationRadius);
            separationPush[i] += push;
            separationPush[j] -= push;
        }

        private void RebuildGrid()
        {
            foreach (List<int> bucket in grid.Values)
            {
                bucket.Clear();
            }

            for (int i = 0; i < agents.Count; i++)
            {
                Vector2Int cell = CellOf(agents[i].transform.position);
                if (!grid.TryGetValue(cell, out List<int> bucket))
                {
                    bucket = new List<int>();
                    grid[cell] = bucket;
                }

                bucket.Add(i);
            }
        }

        private Vector2Int CellOf(Vector3 position)
            => new(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
    }
}
