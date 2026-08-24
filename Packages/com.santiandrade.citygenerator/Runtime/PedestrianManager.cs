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

        public static PedestrianManager Instance { get; private set; }

        private readonly List<PedestrianAgent> agents = new();
        private readonly Dictionary<Vector2Int, List<PedestrianAgent>> grid = new();
        private int frameIndex;

        // Looked up lazily rather than once in Awake: the scene's Player instance is spawned by
        // CityGeneratorSceneBuilder after PedestrianManager already exists, so an eager lookup
        // would find nothing. Cached once found since it never changes afterwards.
        private Transform playerTransform;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Register(PedestrianAgent agent) => agents.Add(agent);

        public void Unregister(PedestrianAgent agent) => agents.Remove(agent);

        private void Update()
        {
            float dt = Time.deltaTime;
            bool staggeringActive = agents.Count > staggerMinAgentCount && staggerFrames > 1;
            Camera cam = staggeringActive ? Camera.main : null;
            Vector3 camPosition = cam != null ? cam.transform.position : Vector3.zero;
            float sqrStaggerDistance = staggerDistance * staggerDistance;

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

                agent.Tick(dt, runLogic);
            }

            ApplyLocalSeparation(dt);
            ApplyPlayerAvoidance(dt);

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

        private void ApplyLocalSeparation(float dt)
        {
            RebuildGrid();

            float sqrSeparationRadius = separationRadius * separationRadius;

            for (int i = 0; i < agents.Count; i++)
            {
                PedestrianAgent agent = agents[i];
                Vector3 position = agent.transform.position;
                Vector2Int cell = CellOf(position);
                Vector3 push = Vector3.zero;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue(new Vector2Int(cell.x + dx, cell.y + dz), out List<PedestrianAgent> bucket))
                        {
                            continue;
                        }

                        for (int b = 0; b < bucket.Count; b++)
                        {
                            PedestrianAgent other = bucket[b];
                            if (other == agent)
                            {
                                continue;
                            }

                            Vector3 offset = position - other.transform.position;
                            offset.y = 0f;
                            float sqrDistance = offset.sqrMagnitude;
                            if (sqrDistance < 0.0001f || sqrDistance >= sqrSeparationRadius)
                            {
                                continue;
                            }

                            float distance = Mathf.Sqrt(sqrDistance);
                            push += offset / distance * (1f - distance / separationRadius);
                        }
                    }
                }

                if (push.sqrMagnitude > 0.0001f)
                {
                    agent.transform.position += push * separationStrength * dt;
                }
            }
        }

        private void RebuildGrid()
        {
            foreach (List<PedestrianAgent> bucket in grid.Values)
            {
                bucket.Clear();
            }

            for (int i = 0; i < agents.Count; i++)
            {
                Vector2Int cell = CellOf(agents[i].transform.position);
                if (!grid.TryGetValue(cell, out List<PedestrianAgent> bucket))
                {
                    bucket = new List<PedestrianAgent>();
                    grid[cell] = bucket;
                }

                bucket.Add(agents[i]);
            }
        }

        private Vector2Int CellOf(Vector3 position)
            => new(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
    }
}
