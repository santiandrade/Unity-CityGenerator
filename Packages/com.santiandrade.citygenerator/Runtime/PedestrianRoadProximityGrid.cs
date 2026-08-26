using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Spatial grid of pedestrian positions, rebuilt once per frame by <see cref="PedestrianManager"/>
    /// (same pattern as its own local-separation grid), so <see cref="CarAgent"/> can find nearby
    /// pedestrians without a <c>SphereCastNonAlloc</c> once the pedestrian count justifies it (item
    /// 8, stage 4). Only ever an additional way to answer the same question the sensor already
    /// answers -- see <see cref="HasEnoughAgents"/> and CarAgent's fallback to its existing sensor.
    /// </summary>
    public sealed class PedestrianRoadProximityGrid : MonoBehaviour
    {
        [SerializeField] private float cellSize = 10f;

        private readonly Dictionary<(int x, int z), List<PedestrianAgent>> cells = new();

        // The player is on the same Pedestrian layer as every NPC (CityGeneratorSceneBuilder puts
        // it there regardless of Include Pedestrians -- see CLAUDE.md) and CarAgent's SphereCast
        // sensor always detected it, but the player is never itself a PedestrianAgent, so it can
        // never appear in `cells` above. Tracked separately and reported by TryGetPlayerPosition so
        // CarAgent's grid path can still brake for the player once this grid takes over.
        private Vector3? playerPosition;

        /// <summary>
        /// Set by the last <see cref="Rebuild"/> call: true once there are more pedestrians than
        /// the threshold that makes querying this grid worth it instead of a fresh SphereCast --
        /// mirrors PedestrianManager's own staggerMinAgentCount.
        /// </summary>
        public bool HasEnoughAgents { get; private set; }

        /// <summary>Called once per frame by PedestrianManager, after ticking every agent.</summary>
        public void Rebuild(IReadOnlyList<PedestrianAgent> agents, int minAgentCountToUse, Transform player)
        {
            foreach (List<PedestrianAgent> bucket in cells.Values)
                bucket.Clear();

            for (int i = 0; i < agents.Count; i++)
            {
                var cell = CellOf(agents[i].transform.position);
                if (!cells.TryGetValue(cell, out List<PedestrianAgent> bucket))
                {
                    bucket = new List<PedestrianAgent>();
                    cells[cell] = bucket;
                }
                bucket.Add(agents[i]);
            }

            playerPosition = player != null ? player.position : (Vector3?)null;
            HasEnoughAgents = agents.Count > minAgentCountToUse;
        }

        /// <summary>True if the player exists in this scene, with its current position.</summary>
        public bool TryGetPlayerPosition(out Vector3 position)
        {
            if (playerPosition.HasValue)
            {
                position = playerPosition.Value;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>Appends every pedestrian within <paramref name="radius"/> of <paramref name="position"/> to <paramref name="results"/> (cleared first).</summary>
        public void QueryNear(Vector3 position, float radius, List<PedestrianAgent> results)
        {
            results.Clear();
            (int cx, int cz) = CellOf(position);
            int spread = Mathf.CeilToInt(radius / cellSize);
            float sqrRadius = radius * radius;

            for (int dx = -spread; dx <= spread; dx++)
            {
                for (int dz = -spread; dz <= spread; dz++)
                {
                    if (!cells.TryGetValue((cx + dx, cz + dz), out List<PedestrianAgent> bucket))
                        continue;

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        PedestrianAgent agent = bucket[i];
                        if (agent != null && (agent.transform.position - position).sqrMagnitude <= sqrRadius)
                            results.Add(agent);
                    }
                }
            }
        }

        private (int x, int z) CellOf(Vector3 position)
            => (Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
    }
}
