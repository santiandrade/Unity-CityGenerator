using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    internal readonly struct PlacementCandidate
    {
        public readonly Vector3 position;
        public readonly Quaternion rotation;

        public PlacementCandidate(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }

    /// <summary>
    /// Caches shared across one whole generation run: the XZ overlap rect of every already-placed
    /// obstacle (measured once, not on every comparison — see <see cref="GetRect"/>) and, per
    /// prefab, a single reusable "probe" instance that gets repositioned to each rejected
    /// candidate instead of instantiating and destroying one throwaway object per candidate (see
    /// <see cref="BorrowProbe"/>). Call <see cref="DestroyRemainingProbes"/> once at the end of
    /// the run: a probe left over from a prefab's last candidate being rejected is a stray
    /// instance that never got consumed and must not survive into the generated scene.
    /// </summary>
    internal sealed class ObstacleCache
    {
        private readonly Dictionary<GameObject, Rect> rects = new();
        private readonly Dictionary<GameObject, GameObject> availableProbes = new();

        // Auxiliary index over the obstacles list threaded through a whole generation run (item
        // 7): never the source of truth, just a derived structure kept in sync with it via
        // SyncSpatialHash, so it can never disagree with what the list itself contains.
        private readonly CityGeneratorSpatialHash spatialHash = new(CityGeneratorConstants.BlockSize);
        private int indexedObstacleCount;

        public Rect GetRect(GameObject instance)
        {
            if (rects.TryGetValue(instance, out Rect cached))
                return cached;

            Bounds bounds = CityGeneratorBoundsUtility.GetWorldBounds(instance);
            var rect = new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);
            rects[instance] = rect;
            return rect;
        }

        /// <summary>
        /// Inserts every obstacle appended to <paramref name="obstacles"/> since the last call into
        /// the spatial hash. Cheap to call before every overlap check: since obstacles are only
        /// ever appended (never removed/reordered) across a whole generation run, this only ever
        /// does work for the entries actually added since the last sync.
        /// </summary>
        public void SyncSpatialHash(List<GameObject> obstacles)
        {
            for (int i = indexedObstacleCount; i < obstacles.Count; i++)
            {
                GameObject obstacle = obstacles[i];
                if (obstacle != null)
                    spatialHash.Insert(GetRect(obstacle));
            }
            indexedObstacleCount = obstacles.Count;
        }

        public bool SpatialHashOverlaps(Rect candidate) => spatialHash.Overlaps(candidate);

        public GameObject BorrowProbe(GameObject prefab, Transform parent, PlacementCandidate candidate)
        {
            if (!availableProbes.TryGetValue(prefab, out GameObject probe) || probe == null)
            {
                probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                if (probe.transform.parent != parent)
                    probe.transform.SetParent(parent);

                // The cached rect (if any) reflects the previous candidate's transform, not this one.
                rects.Remove(probe);
            }

            probe.transform.SetPositionAndRotation(candidate.position, candidate.rotation);
            availableProbes[prefab] = probe;
            return probe;
        }

        /// <summary>Marks a prefab's current probe as kept for real: the next borrow for that prefab instantiates a fresh one.</summary>
        public void Consume(GameObject prefab)
        {
            availableProbes.Remove(prefab);
        }

        public void DestroyRemainingProbes()
        {
            foreach (GameObject probe in availableProbes.Values)
            {
                if (probe != null)
                    Object.DestroyImmediate(probe);
            }

            availableProbes.Clear();
        }
    }

    /// <summary>
    /// Generic density-driven placement: shuffles a set of candidate points, instantiates a
    /// random prefab from the pool at as many as the density fraction dictates, skipping any
    /// candidate whose instance would overlap an already-placed object (checked via combined
    /// Renderer bounds in the XZ plane). Shared by plaza vegetation and street props.
    ///
    /// <paramref name="obstacles"/> is the shared, growing obstacle list for the whole
    /// generation run: accepted instances are appended to it directly rather than the caller
    /// copying it before or after every call.
    /// </summary>
    internal static class CityGeneratorPlacementEngine
    {
        public static List<GameObject> PlaceByDensity(
            IReadOnlyList<PlacementCandidate> candidates,
            IReadOnlyList<GameObject> prefabPool,
            float density,
            System.Random random,
            Transform parent,
            string namePrefix,
            List<GameObject> obstacles,
            ObstacleCache cache)
        {
            var placed = new List<GameObject>();
            if (prefabPool.Count == 0 || density <= 0f || candidates.Count == 0)
                return placed;

            int targetCount = Mathf.Clamp(Mathf.RoundToInt(density * candidates.Count), 0, candidates.Count);
            List<int> order = Enumerable.Range(0, candidates.Count).ToList();
            CityGeneratorRandomUtility.Shuffle(order, random);

            foreach (int candidateIndex in order)
            {
                if (placed.Count >= targetCount)
                    break;

                PlacementCandidate candidate = candidates[candidateIndex];
                GameObject prefab = prefabPool[random.Next(prefabPool.Count)];

                GameObject instance = cache.BorrowProbe(prefab, parent, candidate);
                if (OverlapsAny(instance, obstacles, cache))
                    continue; // the probe stays alive, reused by the next candidate that tries this prefab

                instance.name = $"{namePrefix}_{placed.Count}";
                cache.Consume(prefab);
                obstacles.Add(instance);
                placed.Add(instance);
            }

            return placed;
        }

        private static bool OverlapsAny(GameObject instance, List<GameObject> others, ObstacleCache cache)
        {
            // `instance` (the probe being tested) is never itself a member of `others`: it's only
            // ever appended to the shared obstacles list *after* being accepted, at which point the
            // next candidate borrows a fresh probe instead of reusing this one -- so, unlike the
            // pre-item-7 linear scan, no self-exclusion check is needed here.
            cache.SyncSpatialHash(others);
            Rect rectA = cache.GetRect(instance);
            return cache.SpatialHashOverlaps(rectA);
        }
    }
}
