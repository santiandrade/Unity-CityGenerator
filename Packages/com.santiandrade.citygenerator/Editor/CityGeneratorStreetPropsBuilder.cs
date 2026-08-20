using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Places street-level furniture (lamps, bins) and street vegetation on every
    /// block's sidewalk via the shared density placement engine. Callers thread the same
    /// obstacles list and <see cref="ObstacleCache"/> through each category so later categories
    /// avoid overlapping earlier ones.
    /// </summary>
    internal static class CityGeneratorStreetPropsBuilder
    {
        public static List<GameObject> BuildLamps(GameObject lampPrefab, float density, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random, List<GameObject> obstacles, ObstacleCache cache)
        {
            return PlacePerBlock(lampPrefab, density, group, blocks, random, obstacles, cache, "Lamp",
                block => CityGeneratorStreetCandidates.LampCandidates(block.center));
        }

        public static List<GameObject> BuildBins(GameObject binPrefab, float density, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random, List<GameObject> obstacles, ObstacleCache cache)
        {
            return PlacePerBlock(binPrefab, density, group, blocks, random, obstacles, cache, "Bin",
                block => CityGeneratorStreetCandidates.CornerCandidates(block.center));
        }

        public static List<GameObject> BuildStreetVegetation(VegetationSettings vegetationSettings, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random, List<GameObject> obstacles, ObstacleCache cache)
        {
            if (vegetationSettings.prefabs.Count == 0 || vegetationSettings.density <= 0f)
                return new List<GameObject>();

            var placed = new List<GameObject>();
            int blockIndex = 0;

            foreach (BlockCell block in blocks)
            {
                List<PlacementCandidate> candidates = CityGeneratorStreetCandidates.EdgeCandidates(block.center, CityGeneratorConstants.StreetVegetationPointsPerSide);
                List<GameObject> blockPlaced = CityGeneratorPlacementEngine.PlaceByDensity(
                    candidates, vegetationSettings.prefabs, vegetationSettings.density, random, group, $"Tree_Street_{blockIndex}", obstacles, cache);
                placed.AddRange(blockPlaced);
                blockIndex++;
            }

            return placed;
        }

        private static List<GameObject> PlacePerBlock(
            GameObject prefab, float density, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random,
            List<GameObject> obstacles, ObstacleCache cache, string namePrefix, Func<BlockCell, List<PlacementCandidate>> candidateGenerator)
        {
            if (prefab == null || density <= 0f)
                return new List<GameObject>();

            var prefabPool = new List<GameObject> { prefab };
            var placed = new List<GameObject>();
            int blockIndex = 0;

            foreach (BlockCell block in blocks)
            {
                List<PlacementCandidate> candidates = candidateGenerator(block);
                List<GameObject> blockPlaced = CityGeneratorPlacementEngine.PlaceByDensity(
                    candidates, prefabPool, density, random, group, $"{namePrefix}_{blockIndex}", obstacles, cache);
                placed.AddRange(blockPlaced);
                blockIndex++;
            }

            return placed;
        }
    }
}
