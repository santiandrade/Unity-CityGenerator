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
    /// Generic density-driven placement: shuffles a set of candidate points, instantiates a
    /// random prefab from the pool at as many as the density fraction dictates, skipping any
    /// candidate whose instance would overlap an already-placed object (checked via combined
    /// Renderer bounds in the XZ plane). Shared by plaza vegetation and, later, street props.
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
            List<GameObject> obstacles)
        {
            var placed = new List<GameObject>();
            if (prefabPool.Count == 0 || density <= 0f || candidates.Count == 0)
                return placed;

            int targetCount = Mathf.Clamp(Mathf.RoundToInt(density * candidates.Count), 0, candidates.Count);
            List<int> order = Enumerable.Range(0, candidates.Count).ToList();
            CityGeneratorRandomUtility.Shuffle(order, random);

            var allObstacles = new List<GameObject>(obstacles);

            foreach (int candidateIndex in order)
            {
                if (placed.Count >= targetCount)
                    break;

                PlacementCandidate candidate = candidates[candidateIndex];
                GameObject prefab = prefabPool[random.Next(prefabPool.Count)];

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.name = $"{namePrefix}_{placed.Count}";
                instance.transform.position = candidate.position;
                instance.transform.rotation = candidate.rotation;

                if (OverlapsAny(instance, allObstacles))
                {
                    Object.DestroyImmediate(instance);
                    continue;
                }

                allObstacles.Add(instance);
                placed.Add(instance);
            }

            return placed;
        }

        private static bool OverlapsAny(GameObject instance, List<GameObject> others)
        {
            Bounds a = CityGeneratorBoundsUtility.GetWorldBounds(instance);
            var rectA = new Rect(a.min.x, a.min.z, a.size.x, a.size.z);

            foreach (GameObject other in others)
            {
                if (other == instance)
                    continue;

                Bounds b = CityGeneratorBoundsUtility.GetWorldBounds(other);
                var rectB = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
                if (rectA.Overlaps(rectB))
                    return true;
            }

            return false;
        }
    }
}
