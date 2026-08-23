using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Picks a spawn point for the Player Prefab: inside a plaza block when the city has one
    /// (searched in random order, so which plaza is picked varies between generations), or a
    /// random block otherwise. Either way the position is checked against every already-placed
    /// building/plaza solid/prop/vegetation instance so the player never spawns overlapping them
    /// — ground/sidewalk/lawn are excluded, since standing on those is the whole point.
    /// </summary>
    internal static class CityGeneratorPlayerSpawner
    {
        // Kept off the block edge so a candidate can't land on/past the curb.
        private const float BlockInset = 4f;
        private const float CandidateStep = 2f;

        public static Vector3 FindSpawnPosition(
            GameObject playerPrefab,
            IReadOnlyList<BlockCell> blocks,
            IReadOnlyList<GameObject> avoidObstacles,
            System.Random random,
            Transform tempParent,
            ObstacleCache cache)
        {
            if (playerPrefab == null || blocks.Count == 0)
                return Vector3.zero;

            List<BlockCell> plazaBlocks = blocks.Where(b => b.isPlaza).ToList();
            List<BlockCell> searchOrder = plazaBlocks.Count > 0 ? plazaBlocks : new List<BlockCell>(blocks);
            CityGeneratorRandomUtility.Shuffle(searchOrder, random);

            foreach (BlockCell block in searchOrder)
            {
                List<Vector3> candidates = BuildCandidatePoints(block.center, random);
                foreach (Vector3 candidate in candidates)
                {
                    GameObject probe = cache.BorrowProbe(playerPrefab, tempParent, new PlacementCandidate(candidate, Quaternion.identity));
                    if (!OverlapsAny(probe, avoidObstacles, cache))
                        return candidate;
                }
            }

            // Every candidate collided (an extremely dense plaza/block) — fall back to the first
            // searched block's centre rather than leaving the player with no position at all.
            return searchOrder[0].center + new Vector3(0f, CityGeneratorConstants.GroundDatumY, 0f);
        }

        private static List<Vector3> BuildCandidatePoints(Vector3 blockCenter, System.Random random)
        {
            float extent = CityGeneratorConstants.BlockSize / 2f - BlockInset;
            var points = new List<Vector3>();
            for (float x = -extent; x <= extent + 0.01f; x += CandidateStep)
            {
                for (float z = -extent; z <= extent + 0.01f; z += CandidateStep)
                    points.Add(blockCenter + new Vector3(x, CityGeneratorConstants.GroundDatumY, z));
            }

            CityGeneratorRandomUtility.Shuffle(points, random);
            return points;
        }

        private static bool OverlapsAny(GameObject instance, IReadOnlyList<GameObject> others, ObstacleCache cache)
        {
            Rect rectA = cache.GetRect(instance);

            foreach (GameObject other in others)
            {
                if (other == instance)
                    continue;

                if (rectA.Overlaps(cache.GetRect(other)))
                    return true;
            }

            return false;
        }
    }
}
