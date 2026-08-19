using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Composes each plaza block: 4 lawn quadrants, an optional centerpiece, 4 optional
    /// benches facing inward, and a random scatter of vegetation (density-driven, avoiding
    /// the centerpiece/benches but not the lawns, since grass is ground cover).
    /// </summary>
    internal static class CityGeneratorPlazaBuilder
    {
        private static readonly Vector2[] LawnOffsets =
        {
            new(-CityGeneratorConstants.PlazaLawnPitch / 2f, -CityGeneratorConstants.PlazaLawnPitch / 2f),
            new(CityGeneratorConstants.PlazaLawnPitch / 2f, -CityGeneratorConstants.PlazaLawnPitch / 2f),
            new(-CityGeneratorConstants.PlazaLawnPitch / 2f, CityGeneratorConstants.PlazaLawnPitch / 2f),
            new(CityGeneratorConstants.PlazaLawnPitch / 2f, CityGeneratorConstants.PlazaLawnPitch / 2f),
        };

        // Diagonal, between the four lawn quadrants (not axis-aligned), matching the reference plaza.
        private static readonly Vector2[] BenchOffsets =
        {
            new(CityGeneratorConstants.PlazaBenchRadius, CityGeneratorConstants.PlazaBenchRadius),
            new(CityGeneratorConstants.PlazaBenchRadius, -CityGeneratorConstants.PlazaBenchRadius),
            new(-CityGeneratorConstants.PlazaBenchRadius, -CityGeneratorConstants.PlazaBenchRadius),
            new(-CityGeneratorConstants.PlazaBenchRadius, CityGeneratorConstants.PlazaBenchRadius),
        };

        /// <summary>
        /// Returns the solid instances (centerpiece, benches, vegetation — not the lawns, which
        /// are ground cover) so callers can chain them as obstacles for other categories, plus
        /// the lawn instances themselves separately in <paramref name="lawnInstances"/> (street
        /// furniture must still avoid stepping on the grass, even though the plaza's own
        /// vegetation is allowed to sit on it).
        /// </summary>
        public static List<GameObject> BuildPlazas(
            PlazaSettings plazaSettings,
            VegetationSettings vegetationSettings,
            Transform plazaGroup,
            Transform treesGroup,
            IReadOnlyList<BlockCell> blocks,
            System.Random random,
            ObstacleCache cache,
            out List<GameObject> lawnInstances)
        {
            var solidInstances = new List<GameObject>();
            lawnInstances = new List<GameObject>();
            int plazaIndex = 0;
            foreach (BlockCell block in blocks)
            {
                if (!block.isPlaza)
                    continue;

                Transform blockGroup = GetOrCreateGroup(plazaGroup, $"Plaza_{block.gridX}_{block.gridY}");
                var obstacles = new List<GameObject>();

                List<GameObject> lawns = BuildLawns(plazaSettings.lawnPrefab, blockGroup, block.center);
                lawnInstances.AddRange(lawns);

                if (plazaSettings.centerpiecePrefab != null)
                {
                    GameObject centerpiece = InstantiateAt(plazaSettings.centerpiecePrefab, blockGroup, block.center, Quaternion.identity, "Centerpiece");
                    obstacles.Add(centerpiece);
                }

                if (plazaSettings.benchPrefab != null)
                    obstacles.AddRange(BuildBenches(plazaSettings.benchPrefab, blockGroup, block.center));

                List<GameObject> vegetation = BuildVegetation(vegetationSettings, treesGroup, block.center, obstacles, random, plazaIndex, cache);

                solidInstances.AddRange(obstacles);
                solidInstances.AddRange(vegetation);
                plazaIndex++;
            }

            return solidInstances;
        }

        private static List<GameObject> BuildLawns(GameObject lawnPrefab, Transform group, Vector3 blockCenter)
        {
            var lawns = new List<GameObject>();
            for (int i = 0; i < LawnOffsets.Length; i++)
            {
                Vector2 offset = LawnOffsets[i];
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(lawnPrefab, group);
                instance.name = "Lawn_" + i;
                instance.transform.position = blockCenter + new Vector3(offset.x, CityGeneratorConstants.GroundDatumY, offset.y);
                CityGeneratorBoundsUtility.ScaleToFootprint(instance, CityGeneratorConstants.PlazaLawnFootprint, CityGeneratorConstants.PlazaLawnFootprint);
                lawns.Add(instance);
            }

            return lawns;
        }

        private static List<GameObject> BuildBenches(GameObject benchPrefab, Transform group, Vector3 blockCenter)
        {
            var benches = new List<GameObject>();
            for (int i = 0; i < BenchOffsets.Length; i++)
            {
                Vector2 offset = BenchOffsets[i];
                Vector3 facing = -new Vector3(offset.x, 0f, offset.y).normalized;
                Quaternion rotation = facing.sqrMagnitude > 0f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(benchPrefab, group);
                instance.name = "Bench_" + i;
                instance.transform.position = blockCenter + new Vector3(offset.x, CityGeneratorConstants.GroundDatumY, offset.y);
                instance.transform.rotation = rotation;
                benches.Add(instance);
            }

            return benches;
        }

        /// <summary>
        /// Scatters vegetation across the plaza, confined to the lawn quadrants (never past
        /// their footprint, so it never spills onto the sidewalk) and placed independently on
        /// each of the 4 lawns at the same density — which is what spreads it evenly across all
        /// four rather than letting randomness cluster it on just one or two. Uses a fraction of
        /// the configured density: the lawn candidate grid is much denser than the street one, so
        /// using the same density outright would pack visibly more trees inside the plaza than
        /// along the streets.
        /// </summary>
        private static List<GameObject> BuildVegetation(VegetationSettings vegetationSettings, Transform treesGroup, Vector3 blockCenter, List<GameObject> obstacles, System.Random random, int plazaIndex, ObstacleCache cache)
        {
            float density = vegetationSettings.density * CityGeneratorConstants.PlazaVegetationDensityFactor;
            if (vegetationSettings.prefabs.Count == 0 || density <= 0f)
                return new List<GameObject>();

            var placed = new List<GameObject>();

            for (int i = 0; i < LawnOffsets.Length; i++)
            {
                Vector2 lawnOffset = LawnOffsets[i];
                Vector3 lawnCenter = blockCenter + new Vector3(lawnOffset.x, 0f, lawnOffset.y);

                var candidates = new List<PlacementCandidate>();
                float extent = CityGeneratorConstants.PlazaLawnVegetationExtent;
                float step = CityGeneratorConstants.PlazaLawnVegetationStep;
                for (float x = -extent; x <= extent + 0.01f; x += step)
                {
                    for (float z = -extent; z <= extent + 0.01f; z += step)
                    {
                        Vector3 position = lawnCenter + new Vector3(x, CityGeneratorConstants.GroundDatumY, z);
                        Quaternion rotation = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
                        candidates.Add(new PlacementCandidate(position, rotation));
                    }
                }

                List<GameObject> lawnPlaced = CityGeneratorPlacementEngine.PlaceByDensity(
                    candidates, vegetationSettings.prefabs, density, random,
                    treesGroup, $"Tree_Plaza_{plazaIndex}_{i}", obstacles, cache);
                placed.AddRange(lawnPlaced);
            }

            return placed;
        }

        private static GameObject InstantiateAt(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.position = position;
            instance.transform.rotation = rotation;
            return instance;
        }

        private static Transform GetOrCreateGroup(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing;

            var group = new GameObject(name).transform;
            group.SetParent(parent);
            return group;
        }
    }
}
