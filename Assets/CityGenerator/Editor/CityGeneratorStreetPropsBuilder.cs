using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Places street-level furniture (lamps, bus stops, bins) and street vegetation on every
    /// block's sidewalk via the shared density placement engine. Callers thread the same
    /// obstacles list through each category (appending its returned instances) so later
    /// categories avoid overlapping earlier ones.
    /// </summary>
    internal static class CityGeneratorStreetPropsBuilder
    {
        /// <summary>Places lamps on every block's sidewalk: 3 per side, evenly spaced, always (no density) — clear of the plaza lawn on plaza blocks.</summary>
        public static List<GameObject> BuildLamps(GameObject lampPrefab, Transform group, IReadOnlyList<BlockCell> blocks, List<GameObject> obstacles)
        {
            if (lampPrefab == null)
                return new List<GameObject>();

            var placed = new List<GameObject>();
            var allObstacles = new List<GameObject>(obstacles);
            int blockIndex = 0;

            foreach (BlockCell block in blocks)
            {
                List<PlacementCandidate> candidates = CityGeneratorStreetCandidates.LampCandidates(block.center);
                List<GameObject> blockPlaced = CityGeneratorPlacementEngine.PlaceAll(candidates, lampPrefab, group, $"Lamp_{blockIndex}", allObstacles);
                allObstacles.AddRange(blockPlaced);
                placed.AddRange(blockPlaced);
                blockIndex++;
            }

            return placed;
        }

        public static List<GameObject> BuildBusStops(GameObject busStopPrefab, float density, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random, List<GameObject> obstacles)
        {
            return PlacePerBlock(busStopPrefab, density, group, blocks, random, obstacles, "BusStop",
                block => CityGeneratorStreetCandidates.EdgeCandidates(block.center, 1));
        }

        public static List<GameObject> BuildBins(GameObject binPrefab, float density, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random, List<GameObject> obstacles)
        {
            return PlacePerBlock(binPrefab, density, group, blocks, random, obstacles, "Bin",
                block => CityGeneratorStreetCandidates.CornerCandidates(block.center));
        }

        public static List<GameObject> BuildStreetVegetation(VegetationSettings vegetationSettings, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random, List<GameObject> obstacles)
        {
            if (vegetationSettings.prefabs.Count == 0 || vegetationSettings.density <= 0f)
                return new List<GameObject>();

            var placed = new List<GameObject>();
            var allObstacles = new List<GameObject>(obstacles);
            int blockIndex = 0;

            foreach (BlockCell block in blocks)
            {
                List<PlacementCandidate> candidates = CityGeneratorStreetCandidates.EdgeCandidates(block.center, CityGeneratorConstants.StreetVegetationPointsPerSide);
                List<GameObject> blockPlaced = CityGeneratorPlacementEngine.PlaceByDensity(
                    candidates, vegetationSettings.prefabs, vegetationSettings.density, random, group, $"Tree_Street_{blockIndex}", allObstacles);
                allObstacles.AddRange(blockPlaced);
                placed.AddRange(blockPlaced);
                blockIndex++;
            }

            return placed;
        }

        private static List<GameObject> PlacePerBlock(
            GameObject prefab, float density, Transform group, IReadOnlyList<BlockCell> blocks, System.Random random,
            List<GameObject> obstacles, string namePrefix, Func<BlockCell, List<PlacementCandidate>> candidateGenerator)
        {
            if (prefab == null || density <= 0f)
                return new List<GameObject>();

            var prefabPool = new List<GameObject> { prefab };
            var placed = new List<GameObject>();
            var allObstacles = new List<GameObject>(obstacles);
            int blockIndex = 0;

            foreach (BlockCell block in blocks)
            {
                List<PlacementCandidate> candidates = candidateGenerator(block);
                List<GameObject> blockPlaced = CityGeneratorPlacementEngine.PlaceByDensity(
                    candidates, prefabPool, density, random, group, $"{namePrefix}_{blockIndex}", allObstacles);
                allObstacles.AddRange(blockPlaced);
                placed.AddRange(blockPlaced);
                blockIndex++;
            }

            return placed;
        }
    }
}
