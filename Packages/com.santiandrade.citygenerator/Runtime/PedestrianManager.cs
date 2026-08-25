using System.Collections.Generic;
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

        [Tooltip("This manager's PedestrianRoadProximityGrid, on the same GameObject. Set by CityGeneratorPedestrianBuilder.AddManagerComponent. Rebuilt once per frame here so CarAgent can query nearby pedestrians without a SphereCast (item 8, stage 4).")]
        [SerializeField] private PedestrianRoadProximityGrid roadProximityGrid;

        private readonly List<PedestrianAgent> agents = new();
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

        // Contains-guarded rather than an unconditional Add: Register is called from
        // PedestrianAgent.OnEnable, so a re-enabled agent that was never Unregister-ed (SetActive
        // toggled without ever going through OnDisable in between isn't possible, but a defensive
        // guard here costs nothing and keeps the same idempotence guarantee TrafficManager's
        // HashSet gives CarAgent) must not end up ticked twice in the same frame.
        public void Register(PedestrianAgent agent)
        {
            if (!agents.Contains(agent))
                agents.Add(agent);
        }

        public void Unregister(PedestrianAgent agent) => agents.Remove(agent);

        private void Update()
        {
            float dt = Time.deltaTime;
            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            Camera cam = staggeringActive ? Camera.main : null;
            Vector3 camPosition = cam != null ? cam.transform.position : Vector3.zero;
            float sqrStaggerDistance = staggerDistance * staggerDistance;

            if (activeThisFrame.Length < agents.Count)
                activeThisFrame = new bool[agents.Count];

            for (int i = 0; i < agents.Count; i++)
            {
                PedestrianAgent agent = agents[i];
                bool runLogic = true;

                if (cam != null)
                {
                    float sqrDistance = (agent.transform.position - camPosition).sqrMagnitude;
                    if (sqrDistance > sqrStaggerDistance)
                        runLogic = (frameIndex + i) % staggerFrames == 0;
                }

                activeThisFrame[i] = runLogic;
                agent.Tick(dt, runLogic);
            }

            ApplyLocalSeparation(dt);
            ApplyPlayerAvoidance(dt);

            roadProximityGrid?.Rebuild(agents, staggerMinAgentCount);

            frameIndex++;
        }

        private void ApplyPlayerAvoidance(float dt)
        {
            if (playerTransform == null)
            {
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
